using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using AspxLint.Server;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace AspxLint.Desktop;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private StartedServer? _server;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Mode test : --qr-only <url> ouvre directement la fenetre QR sans serveur
        // ni icone tray. Permet a la suite FlaUI de l'inspecter.
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--qr-only")
            {
                var win = new QrWindow(args[i + 1]);
                MainWindow = win;
                win.Show();
                return;
            }
        }

        try
        {
            _server = ServerHost.Start(new ServerStartOptions());
            ServerHost.PrintBannerAndQr(_server);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Demarrage impossible : {ex.Message}",
                "ASPX-LINT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        BuildTray(_server);

        _trayIcon!.ShowBalloonTip(
            3000,
            $"ASPX-LINT  build {_server.BuildId}",
            $"Dashboard : {_server.LocalUrl}\nClic droit sur l'icone pour le QR code.",
            ToolTipIcon.Info);
    }

    private void BuildTray(StartedServer s)
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Visible = true,
            Text = $"ASPX-LINT  {s.BuildId}"
        };

        var menu = new ContextMenuStrip();

        menu.Items.Add("Ouvrir le dashboard (local)", null,
            (_, _) => OpenInBrowser(s.LocalUrl));

        menu.Items.Add("Copier l'URL LAN (telephone)", null,
            (_, _) => Clipboard.SetText(s.LanUrl));

        menu.Items.Add("Afficher le QR code", null,
            (_, _) => ShowQrWindow(s.LanUrl));

        menu.Items.Add("Ouvrir le dossier des logs", null,
            (_, _) => Process.Start("explorer.exe", Path.GetDirectoryName(s.LogFile)!));

        menu.Items.Add(new ToolStripSeparator());
        var buildLabel = new ToolStripMenuItem($"Build  {s.BuildId}") { Enabled = false };
        menu.Items.Add(buildLabel);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Quitter", null, (_, _) => Shutdown());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => OpenInBrowser(s.LocalUrl);
    }

    private static void ShowQrWindow(string url)
    {
        var win = new QrWindow(url);
        win.Show();
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>
    /// Genere une icone 16x16 a la volee : carre noir + lettre "A" en jaune-vert
    /// (l'accent du dashboard, #d4ff3a). Evite d'avoir a livrer un .ico.
    /// </summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(Color.FromArgb(20, 24, 28)), 0, 0, 16, 16);
            using var font = new Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(212, 255, 58));
            g.DrawString("A", font, brush, 1, 0);
        }
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        try { _server?.App.StopAsync().GetAwaiter().GetResult(); }
        catch { /* on ferme, on s'en fout */ }
        base.OnExit(e);
    }
}
