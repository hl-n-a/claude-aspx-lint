namespace AspxLint.E2E.Tests;

public class DashboardE2ETests : IClassFixture<E2EFixture>
{
    private readonly E2EFixture _fx;
    public DashboardE2ETests(E2EFixture fx) => _fx = fx;

    [Fact]
    public async Task Page_loads_and_brand_is_visible()
    {
        var page = await _fx.NewPageAsync();
        await page.GotoAsync(_fx.AuthUrl);

        var brand = await page.Locator(".brand-mark").TextContentAsync();
        Assert.NotNull(brand);
        Assert.Contains("aspx", brand.ToLowerInvariant());
    }

    [Fact]
    public async Task Server_mode_buttons_are_visible_when_served_via_http()
    {
        var page = await _fx.NewPageAsync();
        await page.GotoAsync(_fx.AuthUrl);

        // Les 3 boutons "mode serveur" doivent etre visibles (display != none)
        await page.Locator("#btnScanServer").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Locator("#btnSaveServer").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Locator("#btnRestoreServer").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    [Fact]
    public async Task Wrong_token_shows_401()
    {
        var page = await _fx.NewPageAsync();
        var resp = await page.GotoAsync($"{_fx.BaseUrl}/?token=00000000000000000000000000000000");
        Assert.NotNull(resp);
        Assert.Equal(401, resp.Status);
    }

    [Fact]
    public async Task Scan_button_loads_files_into_sidebar()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("test.aspx", "<%@ Page %>\n<br>\n");

        var page = await _fx.NewPageAsync();
        // Le prompt() natif est intercepte par Playwright via Dialog.
        page.Dialog += async (_, dialog) =>
        {
            if (dialog.Type == "prompt")
                await dialog.AcceptAsync(tmp.Path);
            else
                await dialog.AcceptAsync();
        };

        await page.GotoAsync(_fx.AuthUrl);
        await page.Locator("#btnScanServer").WaitForAsync();
        await page.Locator("#btnScanServer").ClickAsync();

        // Le fichier doit apparaitre dans la sidebar
        await page.Locator(".tree-file").First.WaitForAsync(new() { Timeout = 5000 });
        var count = await page.Locator(".tree-file").CountAsync();
        Assert.Equal(1, count);

        var name = await page.Locator(".tree-file").First.TextContentAsync();
        Assert.Contains("test.aspx", name!);
    }

    [Fact]
    public async Task Full_scan_fix_save_round_trip_writes_to_disk()
    {
        using var tmp = new TempDir();
        // Fichier crade : trailing whitespace + <br> non auto-ferme
        var aspxPath = tmp.WriteFile("page.aspx", "<%@ Page %>\n<html>\n<body>\n<br>\nline   \n</body>\n</html>\n");

        var page = await _fx.NewPageAsync();
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync(tmp.Path);

        await page.GotoAsync(_fx.AuthUrl);
        await page.Locator("#btnScanServer").WaitForAsync();
        await page.Locator("#btnScanServer").ClickAsync();
        await page.Locator(".tree-file").First.WaitForAsync();

        // Selectionner le fichier (souvent fait automatiquement par scanServerFolder)
        await page.Locator(".tree-file").First.ClickAsync();

        // "Tout corriger"
        await page.Locator("#btnFixAll:not([disabled])").WaitForAsync(new() { Timeout = 5000 });
        await page.Locator("#btnFixAll").ClickAsync();

        // "Enregistrer sur le serveur" devient cliquable apres fix
        await page.Locator("#btnSaveServer:not([disabled])").WaitForAsync(new() { Timeout = 5000 });
        await page.Locator("#btnSaveServer").ClickAsync();

        // Attendre specifiquement le toast "Enregistre" (pas celui de "Tout corriger" qui le precede).
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.toast')?.textContent?.includes('Enregistr')",
            options: new() { Timeout = 5000 });

        // Verification disque : trailing whitespace ET <br> ont ete corriges
        var saved = await File.ReadAllTextAsync(aspxPath);
        Assert.DoesNotContain("line   \n", saved);   // WS-001 fixed
        Assert.Contains("<br />", saved);             // TAG-001 fixed

        // Le .bak garde l'original
        Assert.True(File.Exists(aspxPath + ".bak"));
        var bak = await File.ReadAllTextAsync(aspxPath + ".bak");
        Assert.Contains("line   \n", bak);
        Assert.Contains("<br>", bak);
    }

    [Fact]
    public async Task Restore_button_reverts_disk_to_bak_content()
    {
        using var tmp = new TempDir();
        var aspxPath = tmp.WriteFile("page.aspx", "<%@ Page %>\n<br>\n");

        var page = await _fx.NewPageAsync();
        var dialogQueue = new Queue<string>(new[] { tmp.Path });
        page.Dialog += async (_, dialog) =>
        {
            // 1er prompt = scan path. Suivants = confirm() pour restore => Accept.
            if (dialog.Type == "prompt" && dialogQueue.Count > 0)
                await dialog.AcceptAsync(dialogQueue.Dequeue());
            else
                await dialog.AcceptAsync();
        };

        await page.GotoAsync(_fx.AuthUrl);
        await page.Locator("#btnScanServer").ClickAsync();
        await page.Locator(".tree-file").First.WaitForAsync();
        await page.Locator(".tree-file").First.ClickAsync();

        // Fix + Save => cree le .bak
        await page.Locator("#btnFixAll:not([disabled])").WaitForAsync();
        await page.Locator("#btnFixAll").ClickAsync();
        await page.Locator("#btnSaveServer:not([disabled])").WaitForAsync();
        await page.Locator("#btnSaveServer").ClickAsync();
        await page.Locator(".toast.show").WaitForAsync();

        // Verifier qu'on est bien dans l'etat "modifie sur disque"
        var afterSave = await File.ReadAllTextAsync(aspxPath);
        Assert.Contains("<br />", afterSave);

        // Restore
        await page.Locator("#btnRestoreServer:not([disabled])").WaitForAsync();
        await page.Locator("#btnRestoreServer").ClickAsync();

        // Toast "Restaure"
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.toast')?.textContent?.includes('Restaur')",
            options: new() { Timeout = 5000 });

        // Disque revenu a l'original
        var afterRestore = await File.ReadAllTextAsync(aspxPath);
        Assert.Contains("<br>\n", afterRestore);
        Assert.DoesNotContain("<br />", afterRestore);
    }

    [Fact]
    public async Task Mobile_viewport_stacks_panels_vertically()
    {
        // viewport telephone => media query @max-width:600px doit s'appliquer
        var ctx = await _fx.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 } // iPhone 12 Pro
        });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(_fx.AuthUrl);
        await page.Locator(".brand-mark").WaitForAsync();

        // En mobile, .brand-tag est cache (display:none)
        var tagDisplay = await page.Locator(".brand-tag").EvaluateAsync<string>(
            "el => getComputedStyle(el).display");
        Assert.Equal("none", tagDisplay);

        // Et la grille main passe en 1 colonne
        var mainCols = await page.Locator("main").EvaluateAsync<string>(
            "el => getComputedStyle(el).gridTemplateColumns");
        // 1 colonne = une seule valeur largeur (pas de "Xpx Xpx Xpx")
        Assert.False(mainCols.Contains(" "), $"main devrait avoir 1 colonne en mobile, vu : {mainCols}");
    }
}
