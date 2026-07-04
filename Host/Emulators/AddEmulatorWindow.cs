// Add Emulator dialog (LB parity). A preset combo — seeded from LB's own
// Add-Emulator preset DB (see EmuPresets) — auto-fills the name, default
// command line, behaviour flags and the per-platform command lines (RetroArch
// cores etc.). Recommended platforms come pre-checked; BIOS hints are shown
// inline. Nothing is created until OK: then the emulator + checked platforms
// go through the normal write path (AddNewEmulator → op-log → XML flush) and
// the full editor opens for review.

#nullable enable

using System.IO;
using LbApiHost.Host.UiKit;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbApiHost.Host.Emulators;

internal sealed class AddEmulatorWindow : LiteBoxForm
{
    private readonly string _lbRoot;
    private readonly ComboBox _preset;
    private readonly TextBox _name;
    private readonly TextBox _path;
    private readonly TextBox _cmd;
    private readonly CheckBox _noQuotes, _noSpace, _hideConsole, _fileNameOnly, _autoExtract;
    private readonly Label _hint;
    private readonly Button _download;
    private readonly CheckedListBox _platforms;
    private List<EmuPresetPlatform> _platformRows = new();

    // Set when the "Download" button ran an emulator plugin's InstallEmulator (on a mirrored library): the
    // core Emulator it produced + the game/add-app assignment its own logic chose. On OK we translate the
    // whole thing (all fields + AHK + platforms + assignment) instead of the minimal set.
    private PluginInstallResult? _pluginResult;

    /// <summary>The emulator created on OK (null if cancelled).</summary>
    public IEmulator? Created { get; private set; }

    public AddEmulatorWindow(string lbRoot)
    {
        _lbRoot = lbRoot;
        Text = "Add Emulator";
        ClientSize = new Size(S(680), S(640));
        MinimumSize = new Size(S(620), S(520));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var presets = EmuPresets.Load(lbRoot);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(12)), BackColor = LiteBoxTheme.Bg };
        int y = S(10);

        Lbl(body, S(8), y, "Preset");
        _preset = new ComboBox
        {
            Location = new Point(S(8), y + S(20)), Width = S(320), DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = LiteBoxTheme.Panel2, ForeColor = LiteBoxTheme.Fg, FlatStyle = FlatStyle.Flat,
        };
        _preset.Items.Add("(Custom emulator)");
        foreach (var p in presets) _preset.Items.Add(p.Name);
        _preset.SelectedIndex = 0;
        _preset.SelectedIndexChanged += (_, _) => ApplyPreset(
            _preset.SelectedIndex > 0 ? presets[_preset.SelectedIndex - 1] : null);
        body.Controls.Add(_preset);

        _hint = new Label { Location = new Point(S(340), y + S(23)), AutoSize = true, ForeColor = LiteBoxTheme.SubFg, BackColor = LiteBoxTheme.Bg };
        body.Controls.Add(_hint);
        y += S(52);

        Lbl(body, S(8), y, "Name");
        _name = Txt(body, new Point(S(8), y + S(20)), S(320));
        _download = new Button
        {
            Text = "Download", Location = new Point(S(340), y + S(18)), Size = new Size(S(100), S(26)),
            FlatStyle = FlatStyle.Flat, BackColor = LiteBoxTheme.Panel2, ForeColor = LiteBoxTheme.Fg,
            FlatAppearance = { BorderSize = 0 }, Font = new Font("Segoe UI", 8.5f), Enabled = false,
        };
        _download.Click += (_, _) => StartDownload();
        body.Controls.Add(_download);
        _name.TextChanged += (_, _) => UpdateDownloadEnabled();
        y += S(52);

        Lbl(body, S(8), y, "Application Path");
        _path = Txt(body, new Point(S(8), y + S(20)), S(524));
        var browse = new Button
        {
            Text = "Browse…", Location = new Point(S(540), y + S(18)), Size = new Size(S(88), S(26)),
            FlatStyle = FlatStyle.Flat, BackColor = LiteBoxTheme.Panel2, ForeColor = LiteBoxTheme.Fg,
            FlatAppearance = { BorderSize = 0 }, Font = new Font("Segoe UI", 8.5f),
        };
        browse.Click += (_, _) => BrowseExe();
        body.Controls.Add(browse);
        y += S(52);

        Lbl(body, S(8), y, "Default Command-Line Parameters");
        _cmd = Txt(body, new Point(S(8), y + S(20)), S(620));
        y += S(52);

        _noQuotes = Chk(body, new Point(S(8), y), "Don't use quotes");
        _noSpace = Chk(body, new Point(S(160), y), "No space before ROM");
        _hideConsole = Chk(body, new Point(S(340), y), "Attempt to hide console");
        y += S(26);
        _fileNameOnly = Chk(body, new Point(S(8), y), "Use file name only (no path/extension)");
        _autoExtract = Chk(body, new Point(S(340), y), "Extract ROM archives");
        y += S(34);

        Lbl(body, S(8), y, "Associated Platforms   (check the ones you use — BIOS requirements shown inline)");
        _platforms = new CheckedListBox
        {
            Location = new Point(S(8), y + S(20)), Size = new Size(S(620), S(240)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = LiteBoxTheme.PanelC, ForeColor = LiteBoxTheme.Fg, BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true, IntegralHeight = false,
        };
        body.Controls.Add(_platforms);

        var footer = new FooterBar();
        var cancel = footer.AddButton("Cancel", LiteBoxTheme.CancelBtn, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        var ok = footer.AddButton("Add", LiteBoxTheme.Ok, (_, _) => { if (CreateEmulator()) { DialogResult = DialogResult.OK; Close(); } });
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(body);
        Controls.Add(footer);
        body.BringToFront();
    }

    private void ApplyPreset(EmuPreset? p)
    {
        _name.Text = p?.Name ?? "";
        _cmd.Text = p?.CommandLine ?? "";
        _noQuotes.Checked = p?.NoQuotes ?? false;
        _noSpace.Checked = p?.NoSpace ?? false;
        _hideConsole.Checked = p?.HideConsole ?? false;
        _fileNameOnly.Checked = p?.FileNameOnly ?? false;
        _autoExtract.Checked = p?.AutoExtract ?? false;
        _hint.Text = p == null ? "" : "Executable: " + p.BinaryFileName + (p.Url.Length > 0 ? "   —   " + p.Url : "");

        _platformRows = p?.Platforms ?? new List<EmuPresetPlatform>();
        _platforms.Items.Clear();
        // Pre-check the recommended rows; when the preset flags none as recommended
        // (4DO, …), pre-check everything — an empty association list helps nobody.
        bool anyRecommended = _platformRows.Any(r => r.Recommended);
        foreach (var row in _platformRows)
        {
            string text = row.Platform;
            if (row.RequiredBiosFile.Length > 0) text += "   [BIOS: " + row.RequiredBiosFile + "]";
            if (anyRecommended && !row.Recommended) text += "   (not recommended)";
            _platforms.Items.Add(text, row.Recommended || !anyRecommended);
        }
    }

    // Download is offered only when the typed name matches an emulator-integration plugin (ScummVM, RetroArch…)
    // AND the obfuscated core is shim-able here — exactly LaunchBox's own gating.
    private void UpdateDownloadEnabled()
    {
        bool can = false;
        try { can = EmuInstall.CanShim && EmuInstall.FindPluginByName(_name.Text.Trim()) != null; } catch { }
        _download.Enabled = can;
    }

    private void StartDownload()
    {
        var name = _name.Text.Trim();
        var plugin = EmuInstall.FindPluginByName(name);
        if (plugin == null) return;
        _download.Enabled = false; _download.Text = "…";
        _hint.Text = "Downloading " + name + "…";
        // Empty platform = general install: the plugin uses its own LocalDb platform set. Passing the emulator
        // NAME as a platform would make e.g. RetroArch add a bogus "RetroArch" EmulatorPlatform.
        var platform = "";
        System.Threading.Tasks.Task.Run(() =>
        {
            var res = EmuInstall.RunPluginInstall(plugin, platform,
                progress: (m, f) => { try { BeginInvoke(new Action(() => _hint.Text = $"{m} {(int)(f * 100)}%")); } catch { } },
                cancel: null, log: s => Console.WriteLine(s));
            try { BeginInvoke(new Action(() => DownloadDone(res))); } catch { }
        });
    }

    private void DownloadDone(PluginInstallResult res)
    {
        _download.Text = "Download"; _download.Enabled = true;
        if (!res.Ok || res.Core == null)
        {
            _hint.Text = "";
            MessageBox.Show(this, "Download failed:\n\n" + res.Message, "Download Emulator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _pluginResult = res;
        var core = res.Core;
        try { _path.Text = core.ApplicationPath ?? ""; } catch { }
        try { _cmd.Text = core.CommandLine ?? ""; } catch { }
        try { _hideConsole.Checked = core.HideConsole; } catch { }
        try { _autoExtract.Checked = core.AutoExtract; } catch { }
        try { _noQuotes.Checked = core.NoQuotes; } catch { }
        try { _noSpace.Checked = core.NoSpace; } catch { }
        try { _fileNameOnly.Checked = core.FileNameWithoutExtensionAndPath; } catch { }
        _hint.Text = $"Downloaded — click Add to create the emulator ({res.GamesToAssign.Count} game(s) will be assigned).";
    }

    private void BrowseExe()
    {
        using var dlg = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var sel = dlg.FileName;
        // Warn (don't block) when the chosen exe doesn't match the preset's expected binary.
        if (_preset.SelectedIndex > 0)
        {
            var expected = _hint.Text.Length > 0 ? EmuPresets.Load(_lbRoot)[_preset.SelectedIndex - 1].BinaryFileName : "";
            if (expected.Length > 0 && !string.Equals(Path.GetFileName(sel), expected, StringComparison.OrdinalIgnoreCase))
                MessageBox.Show(this, $"The preset expects \"{expected}\" — you selected \"{Path.GetFileName(sel)}\".\nKeeping your choice; double-check it is the right emulator.",
                    "Executable name mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        // Keep LB-relative when the exe sits under the LB root (LB convention).
        try
        {
            if (_lbRoot.Length > 0 && sel.StartsWith(_lbRoot, StringComparison.OrdinalIgnoreCase))
                sel = sel.Substring(_lbRoot.Length).TrimStart('\\', '/');
        }
        catch { }
        _path.Text = sel;
    }

    private bool CreateEmulator()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Please give the emulator a name.", "Add Emulator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        IEmulator? e;
        try { e = PluginHelper.DataManager?.AddNewEmulator(); }
        catch (Exception ex) { Console.WriteLine("[addemu] AddNewEmulator failed: " + ex.Message); e = null; }
        if (e == null)
        {
            MessageBox.Show(this, "Could not create the emulator (data manager unavailable).", "Add Emulator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        // Downloaded via a plugin: translate the whole result (every field + AHK + platforms + the plugin's
        // own game/add-app assignment), then let the user's visible edits win on the shown fields. Skip the
        // preset platform loop.
        if (_pluginResult != null)
        {
            try { EmuInstall.ApplyToHost(_pluginResult, e, s => Console.WriteLine(s)); }
            catch (Exception ex) { Console.WriteLine("[addemu] ApplyToHost failed: " + ex.Message); }
            Set(() => e.Title = name);
            Set(() => e.ApplicationPath = _path.Text.Trim());
            Set(() => e.CommandLine = _cmd.Text.Trim());
            Set(() => e.HideConsole = _hideConsole.Checked);
            Set(() => e.AutoExtract = _autoExtract.Checked);
            Set(() => e.NoQuotes = _noQuotes.Checked);
            Set(() => e.NoSpace = _noSpace.Checked);
            Set(() => e.FileNameWithoutExtensionAndPath = _fileNameOnly.Checked);
            try { PluginHelper.DataManager?.Save(false); } catch { }
            Created = e;
            return true;
        }

        Set(() => e.Title = name);
        Set(() => e.ApplicationPath = _path.Text.Trim());
        Set(() => e.CommandLine = _cmd.Text.Trim());
        Set(() => e.NoQuotes = _noQuotes.Checked);
        Set(() => e.NoSpace = _noSpace.Checked);
        Set(() => e.HideConsole = _hideConsole.Checked);
        Set(() => e.FileNameWithoutExtensionAndPath = _fileNameOnly.Checked);
        Set(() => e.AutoExtract = _autoExtract.Checked);

        string? defaultPlatform = null;
        for (int i = 0; i < _platforms.Items.Count && i < _platformRows.Count; i++)
        {
            if (!_platforms.GetItemChecked(i)) continue;
            var row = _platformRows[i];
            defaultPlatform ??= row.Platform;
            Set(() =>
            {
                var ep = e.AddNewEmulatorPlatform();
                ep.Platform = row.Platform;
                ep.CommandLine = row.CommandLine;
            });
        }
        if (defaultPlatform != null) Set(() => e.DefaultPlatform = defaultPlatform);

        Created = e;
        return true;
    }

    private void Lbl(Control p, int x, int y, string text)
        => p.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = LiteBoxTheme.Fg, BackColor = LiteBoxTheme.Bg });

    private TextBox Txt(Control p, Point loc, int width)
    {
        var tb = new TextBox
        {
            Location = loc, Width = width,
            BackColor = LiteBoxTheme.Panel2, ForeColor = LiteBoxTheme.Fg, BorderStyle = BorderStyle.FixedSingle,
        };
        p.Controls.Add(tb);
        return tb;
    }

    private CheckBox Chk(Control p, Point loc, string text)
    {
        var cb = new CheckBox { Text = text, Location = loc, AutoSize = true, ForeColor = LiteBoxTheme.Fg, BackColor = LiteBoxTheme.Bg };
        p.Controls.Add(cb);
        return cb;
    }

    private static void Set(Action a) { try { a(); } catch (Exception ex) { Console.WriteLine("[addemu] write failed: " + ex.Message); } }
}
