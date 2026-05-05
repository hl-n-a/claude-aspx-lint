using System.IO;

namespace AspxLint.Desktop.Tests;

/// <summary>
/// Tests UI Automation sur la fenetre QR du Desktop, via FlaUI.
///
/// On invoque l'exe Desktop avec le flag --qr-only &lt;url&gt; (mode dedie aux tests),
/// ce qui ouvre directement la QrWindow sans demarrer le serveur ni l'icone tray.
/// FlaUI s'attache au process et inspecte la fenetre via UIA3.
///
/// /!\ Les tests necessitent une session Windows interactive (display reel).
/// Ils sont skip automatiquement sur runner Linux/Mac via TargetFramework windows.
/// </summary>
public class QrWindowTests : IClassFixture<QrWindowTests.AppFixture>
{
    private readonly AppFixture _app;

    public QrWindowTests(AppFixture app) => _app = app;

    [Fact]
    [Trait("Category", "RequiresDesktop")]
    public void Window_opens_with_correct_title()
    {
        using var automation = new UIA3Automation();
        var win = WaitForWindow(_app.Process, automation, _app.ExpectedTitle);
        Assert.NotNull(win);
    }

    [Fact]
    [Trait("Category", "RequiresDesktop")]
    public void Window_displays_the_test_url()
    {
        using var automation = new UIA3Automation();
        var win = WaitForWindow(_app.Process, automation, _app.ExpectedTitle);
        Assert.NotNull(win);

        // On cherche un descendant Text dont le contenu contient l'URL passee.
        var allTexts = win!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
        var matchingText = allTexts.FirstOrDefault(t => t.Name?.Contains(_app.TestUrl) == true);
        Assert.NotNull(matchingText);
    }

    [Fact]
    [Trait("Category", "RequiresDesktop")]
    public void Window_displays_a_qr_image()
    {
        using var automation = new UIA3Automation();
        var win = WaitForWindow(_app.Process, automation, _app.ExpectedTitle);
        Assert.NotNull(win);

        var images = win!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Image));
        Assert.NotEmpty(images);
    }

    private static Window? WaitForWindow(Process proc, UIA3Automation automation, string titleSubstring, int timeoutMs = 10000)
    {
        var app = Application.Attach(proc);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var win = app.GetMainWindow(automation, TimeSpan.FromSeconds(1));
                if (win != null && (win.Title?.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase) ?? false))
                    return win;
            }
            catch { /* not ready yet */ }
            Thread.Sleep(200);
        }
        return null;
    }

    /// <summary>
    /// Spawn 1 instance d'AspxLint.Desktop.exe en --qr-only avec une URL test,
    /// partagee par toutes les methodes de test. Tue le process en sortie.
    /// </summary>
    public sealed class AppFixture : IDisposable
    {
        public Process Process { get; }
        public string TestUrl { get; } = "http://e2e.local:9999/?token=test123";
        public string ExpectedTitle => "ASPX-LINT";

        public AppFixture()
        {
            var exe = LocateDesktopExe();
            Process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--qr-only {TestUrl}",
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Process.Start a renvoye null");

            // Attente courte pour que la fenetre WPF s'affiche.
            Thread.Sleep(1500);
        }

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(2000);
                }
            }
            catch { }
            Process.Dispose();
        }

        private static string LocateDesktopExe()
        {
            // Le test exe est dans tests/AspxLint.Desktop.Tests/bin/Debug/net9.0-windows/.
            // Le Desktop exe est dans src/AspxLint.Desktop/bin/Debug/net9.0-windows/AspxLint.Desktop.exe.
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "src", "AspxLint.Desktop", "bin", "Debug", "net9.0-windows", "AspxLint.Desktop.exe"),
                Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "src", "AspxLint.Desktop", "bin", "Release", "net9.0-windows", "AspxLint.Desktop.exe"),
            };
            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            throw new FileNotFoundException(
                "AspxLint.Desktop.exe introuvable. Lance d'abord `dotnet build src/AspxLint.Desktop`.");
        }
    }
}
