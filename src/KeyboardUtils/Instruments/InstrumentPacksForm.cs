namespace KeyboardUtils;

/// <summary>
/// Settings > Instruments: every instrument pack, whether it's here and how big it is. Tick several and download
/// them at once, or remove ones not needed any more. The piano is built in and isn't listed.
/// </summary>
sealed class InstrumentPacksForm : AntdUI.Window
{
    readonly MainForm owner;
    readonly List<(Instrument Instrument, AntdUI.Checkbox Box, AntdUI.Label State)> rows = [];
    readonly AntdUI.Button install = new() { AutoSize = true, Type = AntdUI.TTypeMini.Primary, Radius = 8 };
    readonly AntdUI.Button remove = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Button close = new() { AutoSize = true, Radius = 8 };
    readonly AntdUI.Label status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 10, 0, 0) };
    List<PackEntry> catalog = InstrumentPacks.CachedCatalogEntries();
    bool busy;

    IEnumerable<Instrument> Ticked => rows.Where(r => r.Box.Checked).Select(r => r.Instrument);

    public InstrumentPacksForm(MainForm owner)
    {
        this.owner = owner;
        Text = T("Nástroje", "Instruments");
        Icon = AppIcon.Icon;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        MaximizeBox = MinimizeBox = false;
        Size = new Size(500, 520);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 10, 20, 4) };
        layout.Controls.Add(new AntdUI.Label
        {
            Text = T("Piano je v aplikaci vždycky. Ostatní nástroje jsou balíčky se skutečným zvukem nástroje - stáhni si ty, na které hraješ.",
                     "The piano is always there. Other instruments are packs with the instrument's real sound - download the ones you play."),
            AutoSize = false,
            Size = new Size(440, 44),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        });
        foreach (var instrument in Instrument.All.Where(i => !i.IsPiano))
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            var box = new AntdUI.Checkbox { Text = instrument.Name, AutoSize = false, Size = new Size(230, 26) };
            var state = new AntdUI.Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 4, 0, 0) };
            box.CheckedChanged += (_, _) => UpdateButtons();
            row.Controls.AddRange([box, state]);
            rows.Add((instrument, box, state));
            layout.Controls.Add(row);
        }
        layout.Controls.Add(status);

        install.Click += (_, _) => _ = InstallTickedAsync();
        remove.Click += (_, _) => RemoveTicked();
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        buttons.Controls.AddRange([close, install, remove]);
        var titleBar = new TitleBar(this);
        Controls.Add(layout);
        Controls.Add(buttons);
        Controls.Add(titleBar);
        CancelButton = close;
        FormClosing += (_, e) => e.Cancel = busy; // a download in progress finishes first
        Load += async (_, _) =>
        {
            var content = layout.GetPreferredSize(Size.Empty);
            ClientSize = new Size(Math.Max(ClientSize.Width, content.Width), titleBar.Height + content.Height + buttons.GetPreferredSize(Size.Empty).Height);
            CenterToParent();
            Refresh();
            if (catalog.Count == 0)
                status.Text = T("Zjišťuji velikosti...", "Checking sizes...");
            catalog = await InstrumentPacks.CatalogAsync();
            status.Text = catalog.Count == 0 ? T("Seznam balíčků se nepodařilo načíst - zkontroluj připojení k internetu.", "Couldn't load the list of packs - check your internet connection.") : "";
            RefreshRows();
        };
        RefreshRows();
    }

    void RefreshRows()
    {
        foreach (var (instrument, _, state) in rows)
        {
            var entry = catalog.FirstOrDefault(e => e.Id == instrument.Id);
            state.Text = InstrumentPacks.IsInstalled(instrument)
                ? T("✓ staženo", "✓ downloaded")
                : entry != null ? InstrumentPacks.SizeText(entry.Bytes) : T("ke stažení", "to download");
        }
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var ticked = Ticked.ToList();
        var toInstall = ticked.Where(i => !InstrumentPacks.IsInstalled(i)).ToList();
        var toRemove = ticked.Where(InstrumentPacks.IsInstalled).ToList();
        long bytes = toInstall.Sum(i => catalog.FirstOrDefault(e => e.Id == i.Id)?.Bytes ?? 0);
        install.Text = toInstall.Count > 0 && bytes > 0
            ? T($"Stáhnout ({InstrumentPacks.SizeText(bytes)})", $"Download ({InstrumentPacks.SizeText(bytes)})")
            : T("Stáhnout", "Download");
        remove.Text = T("Odebrat", "Remove");
        close.Text = T("Zavřít", "Close");
        install.Enabled = !busy && toInstall.Count > 0;
        remove.Enabled = !busy && toRemove.Count > 0;
        close.Enabled = !busy;
        foreach (var row in rows)
            row.Box.Enabled = !busy;
    }

    async Task InstallTickedAsync()
    {
        var wanted = Ticked.Where(i => !InstrumentPacks.IsInstalled(i)).ToList();
        busy = true;
        UpdateButtons();
        var failed = new List<string>();
        try
        {
            if (wanted.Any(i => catalog.All(e => e.Id != i.Id)))
                catalog = await InstrumentPacks.CatalogAsync();
            foreach (var instrument in wanted)
            {
                var entry = catalog.FirstOrDefault(e => e.Id == instrument.Id);
                if (entry == null)
                {
                    failed.Add($"{instrument.Name}: " + T("není v seznamu balíčků.", "not in the list of packs."));
                    continue;
                }
                var progress = new Progress<double>(p => status.Text = T($"Stahuji {instrument.Name}... {p:P0}", $"Downloading {instrument.Name}... {p:P0}"));
                string? error = await InstrumentPacks.InstallAsync(entry, progress);
                if (error != null)
                    failed.Add($"{instrument.Name}: {error}");
                RefreshRows();
            }
        }
        finally
        {
            busy = false;
        }
        foreach (var row in rows)
            row.Box.Checked = false;
        RefreshRows();
        status.Text = failed.Count == 0 ? T("Hotovo.", "Done.") : string.Join("\n", failed);
    }

    void RemoveTicked()
    {
        var removing = Ticked.Where(InstrumentPacks.IsInstalled).ToList();
        if (!Confirm(this, T("Odebrat", "Remove"),
                T($"Odebrat {string.Join(", ", removing.Select(i => i.Name))}? Záložky zůstanou, nástroj půjde kdykoli znovu stáhnout.",
                  $"Remove {string.Join(", ", removing.Select(i => i.Name))}? The tabs stay; the instrument can be downloaded again any time."),
                T("Odebrat", "Remove")))
            return;
        var failed = new List<string>();
        foreach (var instrument in removing)
        {
            owner.ReleaseInstrumentPack(instrument);
            if (!InstrumentPacks.Remove(instrument.Id))
                failed.Add(instrument.Name);
        }
        foreach (var row in rows)
            row.Box.Checked = false;
        RefreshRows();
        status.Text = failed.Count == 0 ? T("Odebráno.", "Removed.")
            : T($"Nepodařilo se odebrat: {string.Join(", ", failed)} (soubor se právě používá - zkus to po restartu aplikace).",
                $"Couldn't remove: {string.Join(", ", failed)} (the file is in use - try again after restarting the app).");
    }
}
