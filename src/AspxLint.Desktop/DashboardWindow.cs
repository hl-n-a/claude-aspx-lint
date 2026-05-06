using System.IO;
using System.Windows;
using System.Windows.Media;
using AspxLint.Server;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MessageBox = System.Windows.MessageBox;
using WpfColor = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace AspxLint.Desktop;

/// <summary>
/// Fenetre WPF qui affiche la dashboard via un WebView2 embarque. Pas d'URL
/// bar, pas de devtools (F12 / Ctrl+Shift+I), pas de menu contextuel (donc
/// "Voir le code source" desactive), pas de raccourcis browser (Ctrl+S, Ctrl+P,
/// Ctrl+F, etc.) — l'utilisateur ne voit qu'une appli desktop "perso".
/// </summary>
public sealed class DashboardWindow : Window
{
    private readonly WebView2 _webView;
    private readonly string _url;
    private readonly string? _allowedRoot;
    private FileSystemWatcher? _watcher;

    public DashboardWindow(StartedServer server, string? allowedRoot = null)
    {
        _url = server.LocalUrl;
        _allowedRoot = allowedRoot;
        Title = $"ASPX·LINT  •  build {server.BuildId}";
        Width = 1400;
        Height = 900;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(WpfColor.FromRgb(20, 24, 28));

        // Icone de fenetre : title bar Windows + taskbar. Ressource embarquee
        // par AspxLint.Desktop.csproj (<Resource Include="icon.ico" />).
        try
        {
            var uri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(uri);
        }
        catch { /* fallback : pas d'icone de fenetre, le tray icon suffit. */ }

        _webView = new WebView2();
        Content = _webView;

        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => _watcher?.Dispose();
    }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        try
        {
            // Dossier de donnees WebView2 dedie a notre app, pour ne pas
            // partager le profil avec un autre Edge / autre app.
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AspxLint", "WebView2");
            Directory.CreateDirectory(dataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            var s = _webView.CoreWebView2.Settings;
            s.AreDevToolsEnabled = false;                // F12 et Ctrl+Shift+I bloques
            s.AreDefaultContextMenusEnabled = false;     // pas de "Voir source" / "Inspecter"
            s.AreBrowserAcceleratorKeysEnabled = false;  // Ctrl+S / Ctrl+P / Ctrl+F / F5 / F11 bloques
            s.IsStatusBarEnabled = false;                // pas de barre d'URL en bas au survol des liens
            s.IsZoomControlEnabled = true;               // Ctrl+molette pour zoomer reste utile

            // Si un lien externe est cliquable depuis la dashboard (rare),
            // on l'ouvre dans le navigateur systeme plutot que dans le WebView,
            // ce qui evite que l'utilisateur "sorte" de la dashboard.
            _webView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri)
                { UseShellExecute = true });
            };

            _webView.CoreWebView2.Navigate(_url);

            // Si on a un AllowedRoot, on lance un FileSystemWatcher dessus pour
            // notifier la dashboard quand un fichier change sur disque (utile
            // quand l'utilisateur edite en parallele dans VS / un autre editeur).
            if (!string.IsNullOrEmpty(_allowedRoot) && Directory.Exists(_allowedRoot))
            {
                StartFileWatcher(_allowedRoot);
            }

            // Drag-and-drop natif Windows : on intercepte le drop sur le WebView
            // pour recuperer le chemin absolu, qui n'est PAS expose au JS pur
            // (sandbox navigateur). Permet a la dashboard de declencher /api/scan
            // direct avec le serverPath, donc avec save-to-disk active.
            _webView.AllowDrop = true;
            _webView.PreviewDragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                }
            };
            _webView.PreviewDrop += (s, e) =>
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths == null || paths.Length == 0) return;
                e.Handled = true;
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    kind = "droppedNativePaths",
                    paths   // chemins absolus Windows
                });
                try { _webView.CoreWebView2.PostWebMessageAsString(json); }
                catch { /* WebView pas pret */ }
            };
        }
        catch (Exception ex)
        {
            // Cas typique : Runtime WebView2 absent (Windows < 11 22H2 sans Edge a jour).
            // On affiche un message d'erreur lisible plutot qu'un crash silencieux.
            MessageBox.Show(
                $"Impossible de charger la dashboard.\n\n{ex.Message}\n\n" +
                "Le runtime WebView2 est probablement absent. Installe-le depuis " +
                "https://developer.microsoft.com/microsoft-edge/webview2/",
                "ASPX-LINT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>
    /// Surveille les fichiers ASPX/ASCX/MASTER/ASAX sous AllowedRoot. Quand
    /// un changement disque est detecte, on poste un message JSON a la page
    /// JS via window.chrome.webview qui peut alors rafraichir le fichier
    /// concerne. Coalesced 300 ms pour eviter les bursts d'events.
    /// </summary>
    private void StartFileWatcher(string root)
    {
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Filters.Add("*.aspx");
        _watcher.Filters.Add("*.ascx");
        _watcher.Filters.Add("*.master");
        _watcher.Filters.Add("*.asax");

        // Debounce simple : on accumule les events et on flush apres 300 ms
        // d'inactivite, pour absorber les bursts (size + lastWrite + ...).
        var pending = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var debounce = new System.Threading.Timer(_ =>
        {
            var paths = pending.Keys.ToArray();
            pending.Clear();
            if (paths.Length == 0) return;
            Dispatcher.Invoke(() => PostFileChanges(paths));
        });

        void Bump(string path)
        {
            pending[path] = 0;
            debounce.Change(300, System.Threading.Timeout.Infinite);
        }

        _watcher.Changed += (_, e) => Bump(e.FullPath);
        _watcher.Created += (_, e) => Bump(e.FullPath);
        _watcher.Deleted += (_, e) => Bump(e.FullPath);
        _watcher.Renamed += (_, e) => { Bump(e.OldFullPath); Bump(e.FullPath); };
    }

    private void PostFileChanges(string[] paths)
    {
        if (_webView.CoreWebView2 == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            kind = "fileChanges",
            paths
        });
        try { _webView.CoreWebView2.PostWebMessageAsString(json); }
        catch { /* WebView pas encore pret ou ferme */ }
    }

    /// <summary>
    /// Force le focus sur la fenetre (rappele depuis le tray ou un --activate).
    /// </summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
