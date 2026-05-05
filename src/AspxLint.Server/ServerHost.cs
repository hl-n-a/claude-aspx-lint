using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AspxLint.Core;
using QRCoder;

namespace AspxLint.Server;

public sealed record ServerStartOptions(int Port = 5173, string? PreferredInterface = null);

public sealed record StartedServer(
    WebApplication App,
    string BuildId,
    string Token,
    string LocalUrl,
    string LanUrl,
    string LogFile,
    string DashboardPath,
    string ProjectRoot
);

public sealed record ScanRequest(string Path);
public sealed record SaveRequest(string Path, string Content);
public sealed record RestoreRequest(string Path);

public static class ServerHost
{
    /// <summary>
    /// Configure le builder (URLs, logging, services), cree la ServerSession
    /// et l'enregistre comme singleton DI. A appeler avant builder.Build().
    /// </summary>
    public static ServerSession Configure(WebApplicationBuilder builder, int port)
    {
        var session = CreateSession();

        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(session);

        session.Log("INFO", $"server starting, dashboard={session.DashboardPath}");
        return session;
    }

    /// <summary>
    /// Branche les middlewares + routes sur l'application construite.
    /// A appeler apres builder.Build() et avant app.Run().
    /// </summary>
    public static void MapRoutes(WebApplication app)
    {
        var session = app.Services.GetRequiredService<ServerSession>();

        // Auth : token en query (premier hit) ou cookie (hits suivants).
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/healthz")) { await next(); return; }

            var supplied = ctx.Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(supplied))
                supplied = ctx.Request.Cookies["aspx_lint_token"] ?? "";

            var ok = supplied.Length == session.Token.Length &&
                     CryptographicOperations.FixedTimeEquals(
                         Encoding.ASCII.GetBytes(supplied),
                         Encoding.ASCII.GetBytes(session.Token));

            if (!ok)
            {
                session.Log("WARN", $"auth refused from {ctx.Connection.RemoteIpAddress} path={ctx.Request.Path}");
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Token requis ou invalide.");
                return;
            }

            ctx.Response.Cookies.Append("aspx_lint_token", session.Token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(12)
            });
            await next();
        });

        app.MapGet("/healthz", () => Results.Ok(new { ok = true, buildId = session.BuildId }));

        app.MapGet("/", async (HttpContext ctx) =>
        {
            session.Log("INFO", $"dashboard served to {ctx.Connection.RemoteIpAddress}");
            var html = await File.ReadAllTextAsync(session.DashboardPath);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(html);
        });

        app.MapGet("/api/rules", () => Results.Ok(
            RuleRegistry.All.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                severity = r.Severity.ToString().ToLowerInvariant(),
                desc = r.Description,
                hasFix = r.HasFix
            })
        ));

        app.MapPost("/api/scan", (ScanRequest req) =>
        {
            session.Log("INFO", $"scan requested path={req.Path}");
            try
            {
                var scanned = ProjectScanner.Scan(req.Path, RuleRegistry.All).ToList();
                var files = scanned.Select(f => new
                {
                    path = f.AbsolutePath,
                    relativePath = f.RelativePath,
                    lineCount = f.LineCount,
                    content = f.Content,
                    issues = f.Issues.Select(i => new
                    {
                        ruleId = i.RuleId,
                        ruleName = i.RuleName,
                        severity = i.Severity.ToString().ToLowerInvariant(),
                        line = i.Line,
                        col = i.Col,
                        snippet = i.Snippet,
                        hint = i.Hint
                    }).ToList()
                }).ToList();

                var totalIssues = files.Sum(f => f.issues.Count);

                foreach (var f in scanned)
                    session.AddWritable(Path.GetFullPath(f.AbsolutePath));

                session.Log("INFO", $"scan done path={req.Path} files={files.Count} issues={totalIssues}");

                return Results.Ok(new
                {
                    scannedAt = DateTime.UtcNow,
                    buildId = session.BuildId,
                    path = req.Path,
                    fileCount = files.Count,
                    issueCount = totalIssues,
                    files
                });
            }
            catch (DirectoryNotFoundException ex)
            {
                session.Log("WARN", $"scan failed: {ex.Message}");
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                session.Log("ERROR", $"scan crashed: {ex}");
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/save", (SaveRequest req) =>
        {
            string full;
            try { full = Path.GetFullPath(req.Path); }
            catch (Exception ex)
            {
                session.Log("WARN", $"save rejected (bad path): {ex.Message}");
                return Results.BadRequest(new { error = "Chemin invalide." });
            }

            if (!session.IsWritable(full))
            {
                session.Log("WARN", $"save refused (not scanned) path={full}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var backupPath = full + ".bak";
                var backedUp = false;
                if (!File.Exists(backupPath) && File.Exists(full))
                {
                    File.Copy(full, backupPath);
                    backedUp = true;
                }

                var bytes = Encoding.UTF8.GetBytes(req.Content);
                File.WriteAllBytes(full, bytes);

                session.Log("INFO", $"saved path={full} bytes={bytes.Length} backup={backedUp}");
                return Results.Ok(new { ok = true, path = full, bytes = bytes.Length, backedUp });
            }
            catch (Exception ex)
            {
                session.Log("ERROR", $"save crashed: {ex}");
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/restore", (RestoreRequest req) =>
        {
            string full;
            try { full = Path.GetFullPath(req.Path); }
            catch (Exception ex)
            {
                session.Log("WARN", $"restore rejected (bad path): {ex.Message}");
                return Results.BadRequest(new { error = "Chemin invalide." });
            }

            if (!session.IsWritable(full))
            {
                session.Log("WARN", $"restore refused (not scanned) path={full}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var backupPath = full + ".bak";
            if (!File.Exists(backupPath))
            {
                session.Log("INFO", $"restore failed (no .bak) path={full}");
                return Results.NotFound(new { error = "Aucun .bak pour ce fichier (jamais sauvegarde via /api/save)." });
            }

            try
            {
                var bytes = File.ReadAllBytes(backupPath);
                File.WriteAllBytes(full, bytes);

                var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var decoded = hasBom
                    ? "﻿" + Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                    : Encoding.UTF8.GetString(bytes);

                session.Log("INFO", $"restored from .bak path={full} bytes={bytes.Length}");
                return Results.Ok(new { ok = true, path = full, bytes = bytes.Length, content = decoded });
            }
            catch (Exception ex)
            {
                session.Log("ERROR", $"restore crashed: {ex}");
                return Results.Problem(ex.Message);
            }
        });
    }

    /// <summary>
    /// Calcule l'URL locale et l'URL LAN pour le banner / QR code.
    /// </summary>
    public static (string LocalUrl, string LanUrl) ResolveUrls(int port, string token, string? preferredInterface = null)
    {
        var ip = ResolveLocalIPv4(preferredInterface);
        return ($"http://localhost:{port}/?token={token}",
                $"http://{ip}:{port}/?token={token}");
    }

    public static void PrintBannerAndQr(string buildId, string localUrl, string lanUrl, string logFile)
    {
        Console.WriteLine();
        Console.WriteLine($"  ASPX-LINT  build {buildId}");
        Console.WriteLine($"  ----------------------------------------------------");
        Console.WriteLine($"  Local : {localUrl}");
        Console.WriteLine($"  LAN   : {lanUrl}");
        Console.WriteLine($"  Logs  : {logFile}");
        Console.WriteLine();
        Console.WriteLine("  Scan ce QR depuis ton telephone (meme Wi-Fi) :");
        Console.WriteLine();

        using var qrGen = new QRCodeGenerator();
        using var data = qrGen.CreateQrCode(lanUrl, QRCodeGenerator.ECCLevel.M);
        var ascii = new AsciiQRCode(data).GetGraphic(1, drawQuietZones: true);
        foreach (var line in ascii.Split('\n'))
            Console.WriteLine("  " + line.TrimEnd('\r'));
        Console.WriteLine();
    }

    /// <summary>
    /// Back-compat pour AspxLint.Desktop : configure, build, MapRoutes,
    /// StartAsync synchrone, retourne StartedServer pret a l'emploi.
    /// </summary>
    public static StartedServer Start(ServerStartOptions opt)
    {
        var builder = WebApplication.CreateBuilder();
        var session = Configure(builder, opt.Port);
        var app = builder.Build();
        MapRoutes(app);
        app.StartAsync().GetAwaiter().GetResult();

        var (localUrl, lanUrl) = ResolveUrls(opt.Port, session.Token, opt.PreferredInterface);
        session.Log("INFO", $"server listening on :{opt.Port}, lanUrl={lanUrl}");

        return new StartedServer(
            app, session.BuildId, session.Token,
            localUrl, lanUrl, session.LogFile, session.DashboardPath,
            Path.GetDirectoryName(session.DashboardPath)!);
    }

    public static void PrintBannerAndQr(StartedServer s) =>
        PrintBannerAndQr(s.BuildId, s.LocalUrl, s.LanUrl, s.LogFile);

    private static ServerSession CreateSession()
    {
        var buildId = $"b-{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                      Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var dashboardPath = FindUpwards("aspx_lint_dashboard.html")
            ?? throw new FileNotFoundException(
                "aspx_lint_dashboard.html introuvable en remontant depuis " + AppContext.BaseDirectory);
        var projectRoot = Path.GetDirectoryName(dashboardPath)!;

        var logsDir = Path.Combine(projectRoot, "logs");
        Directory.CreateDirectory(logsDir);
        var logFile = Path.Combine(logsDir, $"{buildId}.log");

        return new ServerSession
        {
            BuildId = buildId,
            Token = token,
            DashboardPath = dashboardPath,
            LogFile = logFile
        };
    }

    static string? FindUpwards(string filename)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, filename);
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) return null;
            dir = parent.FullName;
        }
        return null;
    }

    /// <summary>
    /// Choisit l'interface IPv4 la plus probable pour servir le LAN domestique.
    /// Penalite forte sur switches virtuels (Hyper-V, WSL, Docker, VirtualBox),
    /// bonus sur ranges RFC1918 192.168.* / 10.*, bonus sur Wi-Fi et Ethernet physiques.
    /// L'option --interface (substring case-insensitive) ecrase tout.
    /// </summary>
    public static string ResolveLocalIPv4(string? preferredInterface = null)
    {
        try
        {
            var candidates = new List<(int score, string ip, string name)>();
            string[] virtualMarkers =
            {
                "vethernet", "wsl", "virtualbox", "vmware",
                "docker", "hyper-v", "tap", "tun", "loopback"
            };

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;

                    var ip = addr.Address.ToString();
                    var name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    int score = 0;

                    if (!string.IsNullOrEmpty(preferredInterface) &&
                        name.Contains(preferredInterface.ToLowerInvariant()))
                        score += 10000;

                    if (virtualMarkers.Any(m => name.Contains(m))) score -= 500;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 100;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 80;

                    if (ip.StartsWith("192.168.")) score += 200;
                    else if (ip.StartsWith("10.")) score += 150;
                    else if (ip.StartsWith("172."))
                    {
                        var parts = ip.Split('.');
                        if (parts.Length > 1 && int.TryParse(parts[1], out var b) && b >= 16 && b <= 31)
                            score += 50;
                    }

                    candidates.Add((score, ip, ni.Name));
                }
            }

            if (candidates.Count == 0) return "127.0.0.1";
            return candidates.OrderByDescending(c => c.score).First().ip;
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
