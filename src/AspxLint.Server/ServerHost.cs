using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AspxLint.Core;
using QRCoder;

namespace AspxLint.Server;

public sealed record ServerStartOptions(
    int Port = 5173,
    string? PreferredInterface = null,
    /// <summary>API key fixe a accepter. Si null, un token aleatoire est genere
    /// au demarrage (mode "personnel" / Desktop). Posez ASPXLINT_API_KEY pour
    /// un serveur heberge avec une cle stable.</summary>
    string? ApiKey = null,
    /// <summary>Si pose, scope tous les chemins manipules (scan/save/restore) a
    /// ce dossier racine. Hors-scope = 403. Indispensable en hosting public.</summary>
    string? AllowedRoot = null,
    /// <summary>Si true, /api/save et /api/restore renvoient 403. Mode lecture
    /// seule pour les deploiements ou seul le linting est autorise.</summary>
    bool ReadOnly = false
);

public sealed record StartedServer(
    WebApplication App,
    string BuildId,
    string Token,
    string LocalUrl,
    string LanUrl,
    string LogFile,
    string DashboardSource,
    string? ProjectRoot
);

public sealed record ScanRequest(string Path);
public sealed record SaveRequest(string Path, string Content);
public sealed record RestoreRequest(string Path);
public sealed record ReadRequest(string Path);
public sealed record AnalyzeRequest(string Content, string Ext);
public sealed record FixRequest(string Content, string Ext, string RuleId);
public sealed record FixOneRequest(string Content, string Ext, string RuleId, int Line);
public sealed record FixAllRequest(string Content, string Ext);

public static class ServerHost
{
    /// <summary>
    /// Configure le builder (URLs, logging, services), cree la ServerSession
    /// et l'enregistre comme singleton DI. A appeler avant builder.Build().
    /// </summary>
    public static ServerSession Configure(WebApplicationBuilder builder, int port)
        => Configure(builder, new ServerStartOptions(port));

    public static ServerSession Configure(WebApplicationBuilder builder, ServerStartOptions opt)
    {
        var session = CreateSession(opt);

        builder.WebHost.UseUrls($"http://0.0.0.0:{opt.Port}");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(session);

        // CORS : ouvert pour permettre les frontends multi-origines (Web hostee
        // ailleurs que le Server, extensions Chrome, etc.). On autorise les
        // credentials (cookie aspx_lint_token) avec un origin reflexif au lieu
        // de "*", parce que la spec CORS interdit "*" + AllowCredentials.
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.SetIsOriginAllowed(_ => true)
                      .AllowCredentials()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // OpenAPI / Swagger : auto-decouverte des minimal API endpoints.
        // Specifie "Bearer" pour que les futurs frontends puissent tester depuis
        // le UI Swagger.
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "aspx-lint API",
                Version = "v1",
                Description = "Linter et auto-fixer pour ASP.NET Web Forms (.aspx, .ascx, .master).",
                License = new Microsoft.OpenApi.Models.OpenApiLicense { Name = "MIT" }
            });
            options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Token affiche en console au demarrage du serveur."
            });
            options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        session.Log("INFO", $"server starting, dashboard={session.DashboardSource}");
        return session;
    }

    /// <summary>
    /// Branche les middlewares + routes sur l'application construite.
    /// A appeler apres builder.Build() et avant app.Run().
    /// </summary>
    public static void MapRoutes(WebApplication app)
    {
        var session = app.Services.GetRequiredService<ServerSession>();

        app.UseCors();

        // Swagger UI a /swagger, spec OpenAPI a /swagger/v1/swagger.json.
        // Sans auth pour faciliter l'integration externe (les frontends peuvent
        // pomper le contrat sans avoir le token).
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "aspx-lint v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "aspx-lint API";
        });

        // Auth : token accepte via 3 canaux, dans cet ordre :
        //   1. ?token=...                       (URL, premiere visite navigateur)
        //   2. Cookie `aspx_lint_token`         (collant pour navigateur)
        //   3. Header `Authorization: Bearer X` (extensions, CI, frontends remote)
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/healthz") ||
                ctx.Request.Path.StartsWithSegments("/swagger"))
            { await next(); return; }

            var supplied = ctx.Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(supplied))
                supplied = ctx.Request.Cookies["aspx_lint_token"] ?? "";
            if (string.IsNullOrEmpty(supplied))
            {
                var auth = ctx.Request.Headers.Authorization.ToString();
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    supplied = auth["Bearer ".Length..];
            }

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

        // Server-Sent Events : flux longue duree qui pousse les changements
        // (file save, scan, fix) a tous les clients connectes. Permet a
        // plusieurs onglets / appareils de voir les mises a jour en temps reel.
        app.MapGet("/api/events", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";   // disable proxy buffering
            await ctx.Response.WriteAsync("retry: 5000\n\n");   // hint browser
            await ctx.Response.Body.FlushAsync();

            var channel = System.Threading.Channels.Channel.CreateBounded<string>(
                new System.Threading.Channels.BoundedChannelOptions(64)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                });
            var subId = session.SubscribeToEvents(channel);
            session.Log("INFO", $"SSE client connected ({subId})");

            try
            {
                // Heartbeat toutes les 30 s pour eviter les coupures de proxy.
                using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                _ = Task.Run(async () =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        await Task.Delay(30_000, cts.Token).ContinueWith(_ => { });
                        if (cts.IsCancellationRequested) return;
                        try { channel.Writer.TryWrite(": heartbeat\n\n"); } catch { }
                    }
                }, cts.Token);

                await foreach (var ev in channel.Reader.ReadAllAsync(ctx.RequestAborted))
                {
                    await ctx.Response.WriteAsync(ev, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { /* client gone */ }
            finally
            {
                session.UnsubscribeFromEvents(subId);
                session.Log("INFO", $"SSE client disconnected ({subId})");
            }
        });

        app.MapGet("/", async (HttpContext ctx) =>
        {
            session.Log("INFO", $"dashboard served to {ctx.Connection.RemoteIpAddress}");
            var html = await session.LoadDashboardHtml();
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(html);
        });

        app.MapGet("/api/rules", (string? lang) => Results.Ok(
            RuleRegistry.All.Select(r =>
            {
                var (name, desc) = Translations.Resolve(r, lang);
                return new
                {
                    id = r.Id,
                    name,
                    severity = r.Severity.ToString().ToLowerInvariant(),
                    desc,
                    hasFix = r.HasFix
                };
            })
        ));

        // ------------------------------------------------------------------
        // Endpoints inline (path-less). Le client envoie le contenu brut, le
        // serveur applique le moteur Core et renvoie issues / corrections,
        // sans toucher au disque. Utilises par la dashboard pour analyser
        // les fichiers droppes / colles, et par les frontends futurs (Chrome,
        // VS extension, etc.) qui n'ont pas de chemin a fournir.
        // ------------------------------------------------------------------

        app.MapPost("/api/analyze", (AnalyzeRequest req) =>
        {
            var ctx = new RuleContext((req.Ext ?? "").ToLowerInvariant(), "");
            var lines = (req.Content ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var issues = RuleRegistry.All
                .SelectMany(r => r.Detect(req.Content ?? "", lines, ctx))
                .Select(i => new
                {
                    ruleId = i.RuleId,
                    ruleName = i.RuleName,
                    severity = i.Severity.ToString().ToLowerInvariant(),
                    line = i.Line,
                    col = i.Col,
                    snippet = i.Snippet,
                    hint = i.Hint
                })
                .ToList();
            return Results.Ok(new { issues });
        });

        app.MapPost("/api/fix", (FixRequest req) =>
        {
            var rule = RuleRegistry.All.FirstOrDefault(r =>
                r.Id.Equals(req.RuleId, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
                return Results.NotFound(new { error = $"Regle inconnue : {req.RuleId}" });
            if (!rule.HasFix)
                return Results.BadRequest(new { error = $"Regle {req.RuleId} n'a pas d'auto-fix" });

            var ctx = new RuleContext((req.Ext ?? "").ToLowerInvariant(), "");
            var content = req.Content ?? "";
            var beforeCount = CountRuleIssues(rule, content, ctx);
            var fixedContent = rule.Fix(content, ctx) ?? content;
            var afterCount = CountRuleIssues(rule, fixedContent, ctx);
            return Results.Ok(new
            {
                content = fixedContent,
                applied = Math.Max(0, beforeCount - afterCount)
            });
        });

        // Fix per-occurrence : applique le fix de la regle UNIQUEMENT a la ligne
        // donnee. Pour les rules line-local (TAG-001, ATTR-002, ASP-005, WS-001...)
        // on extrait la ligne, on fait tourner Fix dessus, on recolle. Pour les
        // rules file-level (WS-005 BOM, WS-004 final-newline, DOC-001...) il n'y a
        // qu'une issue de toute facon, on tombe sur le fix complet.
        app.MapPost("/api/fix-one", (FixOneRequest req) =>
        {
            var rule = RuleRegistry.All.FirstOrDefault(r =>
                r.Id.Equals(req.RuleId, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
                return Results.NotFound(new { error = $"Regle inconnue : {req.RuleId}" });
            if (!rule.HasFix)
                return Results.BadRequest(new { error = $"Regle {req.RuleId} n'a pas d'auto-fix" });

            var ctx = new RuleContext((req.Ext ?? "").ToLowerInvariant(), "");
            var content = req.Content ?? "";

            // Strategie line-local : on extrait la ligne du conflit, on fait tourner
            // Fix dessus en isolation, et on remplace dans le contenu.
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (req.Line < 1 || req.Line > lines.Length)
                return Results.BadRequest(new { error = "Numero de ligne hors fichier." });

            var idx = req.Line - 1;
            var oneLine = lines[idx];
            var fixedLine = rule.Fix(oneLine, ctx);
            if (fixedLine != null && fixedLine != oneLine)
            {
                lines[idx] = fixedLine;
                var rebuilt = string.Join("\n", lines);
                // Si le contenu original utilisait \r\n, on tente de le preserver.
                if (content.Contains("\r\n")) rebuilt = rebuilt.Replace("\n", "\r\n");
                return Results.Ok(new { content = rebuilt, applied = 1, strategy = "line-local" });
            }

            // Fallback file-level : on applique le fix complet (couvre WS-005,
            // WS-004, DOC-001, etc. — rules a une seule occurrence par fichier).
            var fixedContent = rule.Fix(content, ctx) ?? content;
            return Results.Ok(new
            {
                content = fixedContent,
                applied = fixedContent != content ? 1 : 0,
                strategy = "file-level"
            });
        });

        app.MapPost("/api/fix-all", (FixAllRequest req) =>
        {
            var ctx = new RuleContext((req.Ext ?? "").ToLowerInvariant(), "");
            var content = req.Content ?? "";
            var history = new List<object>();
            var fixableRules = RuleRegistry.All.Where(r => r.HasFix).ToList();

            // Matche le comportement JS d'origine : jusqu'a 5 passes pour
            // convergence (un fix peut creer un nouveau probleme detectable).
            for (int pass = 0; pass < 5; pass++)
            {
                var before = content;
                foreach (var rule in fixableRules)
                {
                    var beforeCount = CountRuleIssues(rule, content, ctx);
                    if (beforeCount == 0) continue;

                    var attempted = rule.Fix(content, ctx);
                    if (attempted is null || attempted == content) continue;

                    var afterCount = CountRuleIssues(rule, attempted, ctx);
                    var resolved = beforeCount - afterCount;
                    content = attempted;
                    if (resolved > 0)
                        history.Add(new { ruleId = rule.Id, count = resolved });
                }
                if (content == before) break;
            }

            return Results.Ok(new { content, history });
        });

        static int CountRuleIssues(IRule rule, string content, RuleContext ctx)
        {
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return rule.Detect(content, lines, ctx).Count();
        }

        // Recherche d'un dossier par nom (BFS) sous AllowedRoot. Utilise par le drop
        // d'un dossier dans la dashboard : on a le nom mais pas le chemin absolu
        // (le navigateur ne l'expose pas), donc on le cherche cote serveur.
        app.MapGet("/api/find-folder", (string? name, int? limit) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { error = "Parametre 'name' requis." });

            var max = Math.Clamp(limit ?? 20, 1, 100);
            var root = session.AllowedRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return Results.Ok(new { matches = Array.Empty<object>(), root = (string?)null, visited = 0 });

            session.Log("INFO", $"find-folder name='{name}' root={root} limit={max}");

            var matches = new List<object>();
            var queue = new Queue<string>();
            queue.Enqueue(root);
            int visited = 0;
            const int MaxVisits = 8000;   // garde-fou contre les arbo enormes (node_modules, etc.)

            while (queue.Count > 0 && matches.Count < max && visited < MaxVisits)
            {
                var dir = queue.Dequeue();
                visited++;
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        if (Path.GetFileName(sub).Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            int aspxCount = 0;
                            try
                            {
                                aspxCount = Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories)
                                    .Take(500)   // capping pour pas exploser sur un .git ou node_modules
                                    .Count(f =>
                                    {
                                        var ext = Path.GetExtension(f).ToLowerInvariant();
                                        return ext is ".aspx" or ".ascx" or ".master" or ".asax";
                                    });
                            }
                            catch { }
                            matches.Add(new { name = Path.GetFileName(sub), path = sub, aspxCount });
                            if (matches.Count >= max) break;
                        }
                        queue.Enqueue(sub);
                    }
                }
                catch { /* acces refuse, dossier disparu, etc. — on continue */ }
            }

            return Results.Ok(new { matches, root, visited });
        });

        // Folder explorer : liste les sous-dossiers (et un apercu fichiers .aspx/.ascx/.master/.asax)
        // sous AllowedRoot. Si AllowedRoot est null, le path demande peut etre n'importe ou,
        // mais sans path on retourne les drives Windows ou "/" sur Unix.
        app.MapGet("/api/browse", (string? path) =>
        {
            string? requestedFull = null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { requestedFull = Path.GetFullPath(path); }
                catch (Exception ex)
                {
                    session.Log("WARN", $"browse rejected (bad path): {ex.Message}");
                    return Results.BadRequest(new { error = "Chemin invalide." });
                }
            }

            // Pas de path → racine logique : AllowedRoot s'il existe, sinon les drives (Windows) / "/" (Unix).
            if (requestedFull is null)
            {
                if (session.AllowedRoot is not null)
                {
                    requestedFull = Path.GetFullPath(session.AllowedRoot);
                }
                else
                {
                    // Liste des drives / racine Unix
                    var roots = DriveInfo.GetDrives()
                        .Where(d => d.IsReady)
                        .Select(d => new
                        {
                            name = d.Name,
                            path = d.RootDirectory.FullName,
                            isDirectory = true,
                            aspxCount = 0
                        })
                        .ToList();
                    return Results.Ok(new
                    {
                        path = (string?)null,
                        parent = (string?)null,
                        allowedRoot = (string?)null,
                        entries = roots
                    });
                }
            }

            if (!session.IsUnderAllowedRoot(requestedFull))
            {
                session.Log("WARN", $"browse refused (out of allowed root) path={requestedFull}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (!Directory.Exists(requestedFull))
                return Results.NotFound(new { error = "Dossier introuvable." });

            string? parent = null;
            try
            {
                var p = Directory.GetParent(requestedFull)?.FullName;
                if (p is not null && session.IsUnderAllowedRoot(p)) parent = p;
            }
            catch { /* root, pas de parent */ }

            try
            {
                var dirs = Directory.EnumerateDirectories(requestedFull)
                    .Select(d =>
                    {
                        int aspxCount = 0;
                        try
                        {
                            // Compte rapide (non recursif) des fichiers .aspx/.ascx/.master/.asax dans le sous-dossier
                            // pour donner un indice visuel a l'utilisateur. Erreurs silencieuses (acces refuse, etc.).
                            aspxCount = Directory.EnumerateFiles(d)
                                .Count(f =>
                                {
                                    var ext = Path.GetExtension(f).ToLowerInvariant();
                                    return ext is ".aspx" or ".ascx" or ".master" or ".asax";
                                });
                        }
                        catch { }
                        return new
                        {
                            name = Path.GetFileName(d),
                            path = d,
                            isDirectory = true,
                            aspxCount
                        };
                    })
                    .OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int hereAspxCount = 0;
                try
                {
                    hereAspxCount = Directory.EnumerateFiles(requestedFull)
                        .Count(f =>
                        {
                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            return ext is ".aspx" or ".ascx" or ".master" or ".asax";
                        });
                }
                catch { }

                return Results.Ok(new
                {
                    path = requestedFull,
                    parent,
                    allowedRoot = session.AllowedRoot,
                    hereAspxCount,
                    entries = dirs
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                session.Log("WARN", $"browse access denied: {ex.Message}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                session.Log("ERROR", $"browse crashed: {ex}");
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/scan", (ScanRequest req) =>
        {
            session.Log("INFO", $"scan requested path={req.Path}");

            // Scoping : si AllowedRoot pose, le chemin demande doit etre dessous.
            string requestedFull;
            try { requestedFull = Path.GetFullPath(req.Path); }
            catch (Exception ex)
            {
                session.Log("WARN", $"scan rejected (bad path): {ex.Message}");
                return Results.BadRequest(new { error = "Chemin invalide." });
            }
            if (!session.IsUnderAllowedRoot(requestedFull))
            {
                session.Log("WARN", $"scan refused (out of allowed root) path={requestedFull}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

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
                session.BroadcastEvent("scanned", new { path = req.Path, fileCount = files.Count, issueCount = totalIssues });

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
            if (session.ReadOnly)
            {
                session.Log("WARN", "save refused (read-only mode)");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            string full;
            try { full = Path.GetFullPath(req.Path); }
            catch (Exception ex)
            {
                session.Log("WARN", $"save rejected (bad path): {ex.Message}");
                return Results.BadRequest(new { error = "Chemin invalide." });
            }

            if (!session.IsUnderAllowedRoot(full))
            {
                session.Log("WARN", $"save refused (out of allowed root) path={full}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
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
                session.BroadcastEvent("fileSaved", new { path = full, bytes = bytes.Length });
                return Results.Ok(new { ok = true, path = full, bytes = bytes.Length, backedUp });
            }
            catch (Exception ex)
            {
                session.Log("ERROR", $"save crashed: {ex}");
                return Results.Problem(ex.Message);
            }
        });

        // Lit un fichier depuis le disque (pour rafraichir la dashboard apres un
        // changement detecte par le file watcher Desktop). Renvoie content + issues.
        app.MapPost("/api/read", (ReadRequest req) =>
        {
            string full;
            try { full = Path.GetFullPath(req.Path); }
            catch (Exception ex) { return Results.BadRequest(new { error = $"Chemin invalide : {ex.Message}" }); }

            if (!session.IsUnderAllowedRoot(full))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!File.Exists(full))
                return Results.NotFound(new { error = "Fichier introuvable." });

            try
            {
                var bytes = File.ReadAllBytes(full);
                var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var content = hasBom
                    ? "﻿" + Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                    : Encoding.UTF8.GetString(bytes);

                var ext = Path.GetExtension(full).TrimStart('.').ToLowerInvariant();
                var issues = Analyzer.Analyze(full, content, RuleRegistry.All)
                    .Select(i => new
                    {
                        ruleId = i.RuleId,
                        ruleName = i.RuleName,
                        severity = i.Severity.ToString().ToLowerInvariant(),
                        line = i.Line,
                        col = i.Col,
                        snippet = i.Snippet,
                        hint = i.Hint
                    });

                return Results.Ok(new { path = full, content, issues });
            }
            catch (Exception ex)
            {
                session.Log("ERROR", $"read crashed: {ex}");
                return Results.Problem(ex.Message);
            }
        });

        app.MapPost("/api/restore", (RestoreRequest req) =>
        {
            if (session.ReadOnly)
            {
                session.Log("WARN", "restore refused (read-only mode)");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            string full;
            try { full = Path.GetFullPath(req.Path); }
            catch (Exception ex)
            {
                session.Log("WARN", $"restore rejected (bad path): {ex.Message}");
                return Results.BadRequest(new { error = "Chemin invalide." });
            }

            if (!session.IsUnderAllowedRoot(full))
            {
                session.Log("WARN", $"restore refused (out of allowed root) path={full}");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
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
        var session = Configure(builder, opt);
        var app = builder.Build();
        MapRoutes(app);
        app.StartAsync().GetAwaiter().GetResult();

        var (localUrl, lanUrl) = ResolveUrls(opt.Port, session.Token, opt.PreferredInterface);
        session.Log("INFO", $"server listening on :{opt.Port}, lanUrl={lanUrl}");

        return new StartedServer(
            app, session.BuildId, session.Token,
            localUrl, lanUrl, session.LogFile, session.DashboardSource,
            ResolveProjectRoot(session.DashboardSource));
    }

    /// <summary>
    /// Si DashboardSource est un chemin disque ("disk:..."), renvoie le
    /// dossier parent (le project root). Sinon (mode embedded), null.
    /// </summary>
    private static string? ResolveProjectRoot(string dashboardSource)
    {
        const string prefix = "disk:";
        if (!dashboardSource.StartsWith(prefix)) return null;
        var path = dashboardSource[prefix.Length..];
        // Le HTML est a src/AspxLint.Web/index.html → on remonte 2 dossiers
        // pour avoir la racine du repo. Heuristique simple suffisante en dev.
        var dir = Path.GetDirectoryName(path);
        if (dir != null) dir = Path.GetDirectoryName(dir);   // src/AspxLint.Web
        if (dir != null) dir = Path.GetDirectoryName(dir);   // src
        return dir;
    }

    public static void PrintBannerAndQr(StartedServer s) =>
        PrintBannerAndQr(s.BuildId, s.LocalUrl, s.LanUrl, s.LogFile);

    private static ServerSession CreateSession(ServerStartOptions opt)
    {
        var buildId = $"b-{DateTime.UtcNow:yyyyMMdd-HHmmss}-" +
                      Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();

        // Token : prefere l'API key explicite, puis l'env var, puis aleatoire.
        var token = opt.ApiKey
                    ?? Environment.GetEnvironmentVariable("ASPXLINT_API_KEY")
                    ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var allowedRoot = opt.AllowedRoot
                          ?? Environment.GetEnvironmentVariable("ASPXLINT_ALLOWED_ROOT");
        var readOnly = opt.ReadOnly
                       || string.Equals(Environment.GetEnvironmentVariable("ASPXLINT_READ_ONLY"),
                                        "true", StringComparison.OrdinalIgnoreCase);

        var (loadDashboard, dashboardSource, projectRoot) = ResolveDashboard();
        var logsDir = ResolveLogDir(projectRoot);
        Directory.CreateDirectory(logsDir);
        var logFile = Path.Combine(logsDir, $"{buildId}.log");

        return new ServerSession
        {
            BuildId = buildId,
            Token = token,
            LogFile = logFile,
            DashboardSource = dashboardSource,
            LoadDashboardHtml = loadDashboard,
            AllowedRoot = allowedRoot,
            ReadOnly = readOnly
        };
    }

    /// <summary>
    /// Resout la source de la dashboard HTML, par ordre de preference :
    ///   1. src/AspxLint.Web/index.html sur disque (mode dev, hot-reload)
    ///   2. AspxLint.Web/index.html (si on tourne depuis src/)
    ///   3. Ressource embarquee "AspxLint.Web.index.html" dans le .dll
    ///      (mode .exe self-contained, conteneur Docker, etc.)
    /// </summary>
    /// <summary>
     /// Marqueur d'inclusion dans index.html : {{include:NAME}}.
     /// NAME est resolu relativement au dossier src/AspxLint.Web/ en mode disk,
     /// ou comme nom de ressource AspxLint.Web.NAME (avec / -> .) en mode embedded.
     /// Les noms doivent etre des chemins relatifs simples (lettres, chiffres,
     /// '.', '/', '-', '_') — pas de '..' pour eviter de remonter en dehors du dossier.
     /// </summary>
    private static readonly Regex IncludeMarker =
        new(@"\{\{include:([a-zA-Z0-9_./\-]+)\}\}", RegexOptions.Compiled);

    private static (Func<Task<string>> Load, string Source, string? ProjectRoot) ResolveDashboard()
    {
        // Disk d'abord pour le hot-reload en dev.
        var diskCandidate = FindUpwards(Path.Combine("src", "AspxLint.Web", "index.html"))
                         ?? FindUpwards(Path.Combine("AspxLint.Web", "index.html"));
        if (diskCandidate != null)
        {
            var path = diskCandidate;
            var webDir = Path.GetDirectoryName(path)!;
            var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
            return (
                async () =>
                {
                    var raw = await File.ReadAllTextAsync(path);
                    return await ExpandIncludes(raw, async name =>
                    {
                        var inc = Path.Combine(webDir, name.Replace('/', Path.DirectorySeparatorChar));
                        return await File.ReadAllTextAsync(inc);
                    });
                },
                $"disk:{path}", root);
        }

        // Fallback : ressource embarquee. On verifie aussi que les ressources
        // referencees existent (sinon on aura des marqueurs non resolus a runtime).
        var asm = typeof(ServerHost).Assembly;
        const string resourceName = "AspxLint.Web.index.html";
        using (var probe = asm.GetManifestResourceStream(resourceName))
        {
            if (probe == null)
            {
                var available = string.Join(", ", asm.GetManifestResourceNames());
                throw new FileNotFoundException(
                    $"Dashboard HTML introuvable. Ressources embarquees disponibles : [{available}]");
            }
        }

        return (LoadEmbedded, $"embedded:{resourceName}", null);

        static async Task<string> LoadEmbedded()
        {
            var asm = typeof(ServerHost).Assembly;
            string raw;
            using (var stream = asm.GetManifestResourceStream("AspxLint.Web.index.html")!)
            using (var reader = new StreamReader(stream))
                raw = await reader.ReadToEndAsync();

            return await ExpandIncludes(raw, async name =>
            {
                // {{include:partials/modal-paste.html}} -> "AspxLint.Web.partials.modal-paste.html"
                var resName = "AspxLint.Web." + name.Replace('/', '.');
                using var s = asm.GetManifestResourceStream(resName);
                if (s == null)
                {
                    var available = string.Join(", ", asm.GetManifestResourceNames());
                    throw new FileNotFoundException(
                        $"Ressource embarquee '{resName}' introuvable. Disponibles : [{available}]");
                }
                using var r = new StreamReader(s);
                return await r.ReadToEndAsync();
            });
        }
    }

    /// <summary>
    /// Remplace tous les {{include:NAME}} par le contenu retourne par <paramref name="loader"/>.
    /// Inclusion plate (1 niveau) : si un fichier inclus contient lui-meme un marqueur,
    /// il sera ignore. Suffisant pour la dashboard ou app.js / styles.css n'incluent rien.
    /// </summary>
    private static async Task<string> ExpandIncludes(string content, Func<string, Task<string>> loader)
    {
        var matches = IncludeMarker.Matches(content);
        if (matches.Count == 0) return content;

        var sb = new StringBuilder(content.Length + 4096);
        int last = 0;
        foreach (Match m in matches)
        {
            sb.Append(content, last, m.Index - last);
            var name = m.Groups[1].Value;
            sb.Append(await loader(name));
            last = m.Index + m.Length;
        }
        sb.Append(content, last, content.Length - last);
        return sb.ToString();
    }

    /// <summary>
    /// Logs : a cote du repo en dev (logs/), dans %LOCALAPPDATA%\AspxLint\logs
    /// quand on est en mode embedded (pas de repo a cote du .exe).
    /// </summary>
    private static string ResolveLogDir(string? projectRoot)
    {
        if (projectRoot != null)
            return Path.Combine(projectRoot, "logs");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "AspxLint", "logs");
    }

    static string? FindUpwards(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, relativePath);
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
