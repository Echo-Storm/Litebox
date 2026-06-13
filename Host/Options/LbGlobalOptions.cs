// LaunchBox GLOBAL options (LB parity — the Tools ▸ Options window), backed by
// LB\Data\Settings.xml through LbSettingsStore → op-log → scoped flush. These are
// LAUNCHBOX's settings: editing them here writes what LB will read on its next
// boot. Options LiteBox itself never reads carry NoImpact = true and render a red
// "No impact on LiteBox" note (the value still round-trips for LB's benefit).
//
// This first pass covers the simple sections. Deferred to later rounds:
//   - Save Management (big chantier, explicitly parked)
//   - Startup Applications / Game Progress / Related Games / Media priorities
//     (grids & trees — incl. the Related-Games mirror file: LB STRIPS unknown
//     tags from Settings.xml, verified empirically, so the mirror cannot live there)

#nullable enable

using LbApiHost.Host.Data;

namespace LbApiHost.Host.Options;

internal static class LbGlobalOptions
{
    /// <summary>Appends the LaunchBox-settings sections to an options window.
    /// <paramref name="readOnly"/> greys them out entirely.</summary>
    public static void AddSections(OptionsWindow w, LbSettingsStore s, bool readOnly)
    {
        if (!s.Loaded) return;   // no Settings.xml → nothing to edit

        OptionItem B(string label, string field, bool noImpact, string? help = null)
            => new(label, label, OptionKind.Bool)
            { Get = () => s.Get(field), Set = v => s.Set(field, v), NoImpact = noImpact, Help = help };

        // LB's language picker: display the native names, store the culture code
        // (same 16 entries as LB's own dropdown; codes from the Core\* satellite
        // folders — .NET culture fallback makes the specific form always safe).
        string[] langNames =
        {
            "Arabic", "Deutsch", "English", "Español", "Ελληνικά", "Français", "Italiano",
            "日本語", "한국어", "Nederlands", "Português do Brasil", "Русский", "Svenska",
            "Türkçe", "简体中文", "繁體中文",
        };
        string[] langCodes =
        {
            "ar-SA", "de-DE", "en-US", "es", "el-GR", "fr-FR", "it-IT",
            "ja-JP", "ko-KR", "nl-NL", "pt-BR", "ru-RU", "sv-SE",
            "tr-TR", "zh-Hans", "zh-Hant",
        };
        string GetLanguage()
        {
            var v = s.Get("Language", "en-US");
            if (Array.IndexOf(langCodes, v) >= 0) return v;
            // Tolerant match: "fr" or "fr-CA" in the file still selects Français.
            var two = v.Split('-')[0];
            foreach (var c in langCodes) if (c.Split('-')[0].Equals(two, StringComparison.OrdinalIgnoreCase)) return c;
            return "en-US";
        }

        w.AddSection("LB · General", new[]
        {
            OptionItem.Choice("g", "Language", langNames,
                GetLanguage, v => s.Set("Language", v),
                "LaunchBox UI language — applies to LaunchBox's next start.")
                .Values(langCodes).Tag(noImpact: true),
            B("Show splash screen during load", "ShowLaunchBoxSplashScreen", true),
            B("Allow deleting ROMs when deleting games", "AllowDeletingRoms", true),
            B("Share optional usage data", "EnableTelemetry", true),
            B("Minimize LaunchBox when launching games", "MinimizeOnGameLaunch", true,
                "Only when the startup screen is disabled."),
            B("Restore LaunchBox when exiting games", "RestoreOnGameExit", true,
                "Only when the startup screen is disabled."),
        }, readOnly);

        w.AddSection("LB · Debugging", new[]
        {
            B("Enable Debug Logs", "DebugLog", true,
                "LaunchBox writes log files to LaunchBox\\Logs on each startup. LiteBox has its own debug log."),
        }, readOnly);

        w.AddSection("LB · Notifications", new[]
        {
            OptionItem.Choice("n", "Notification System",
                new[] { "LaunchBox Notifications", "Windows Notifications", "Message Boxes" },
                () => s.Get("NotificationType", "0"), v => s.Set("NotificationType", v),
                "What system LaunchBox uses to display notifications.")
                .Values("0", "1", "2").Tag(noImpact: true),
        }, readOnly);

        w.AddSection("LB · Automated Imports", new[]
        {
            B("Enable Automatic ROM Imports", "EnableRomAutoImports", true,
                "LaunchBox scans for new ROMs on startup and while open."),
        }, readOnly);

        w.AddSection("LB · Startup Applications", BuildStartupAppsPanel(s, readOnly, out var applyStartupApps),
            readOnly ? null : applyStartupApps);

        w.AddSection("LB · System Tray", new[]
        {
            B("Enable System Tray", "EnableSystemTray", true),
            B("Minimize to System Tray", "MinimizeToSystemTray", true),
            B("Close to System Tray", "CloseToSystemTray", true),
            OptionItem.Toggle("t", "Show notification when sent to the system tray",
                () => !s.GetBool("DontSendTrayReminder"), v => s.SetBool("DontSendTrayReminder", !v))
                .Tag(noImpact: true),
        }, readOnly);

        w.AddSection("LB · Updates", new[]
        {
            B("Check for Updates on Startup", "CheckForUpdates", true),
            B("Automatically Download Updates in the Background", "BackgroundUpdateDownloads", true),
            B("Update to Beta Releases", "BetaUpgrades", true),
        }, readOnly);

        w.AddSection("LB · Video Playback", new[]
        {
            OptionItem.Choice("v", "Video playback engine",
                new[] { "Windows Media Player", "FFmpeg" },
                () => s.Get("VideoPlaybackEngine", "Windows Media Player"), v => s.Set("VideoPlaybackEngine", v))
                .Tag(noImpact: true),
        }, readOnly);

        w.AddSection("LB · Backups", new[]
        {
            B("Automatically back up the LaunchBox XML data files", "AutoBackup", true,
                "LaunchBox backs up Data\\ to Backups\\ on its own startup/shutdown (up to 25 kept). " +
                "LiteBox makes its own pre-write backups independently of this."),
        }, readOnly);

        w.AddSection("LB · Region Priorities", BuildRegionPrioritiesPanel(s, readOnly, out var applyRegions),
            readOnly ? null : applyRegions);

        w.AddSection("LB · Auto-Import Media", BuildAutoImportMediaPanel(s, readOnly, out var applyMedia),
            readOnly ? null : applyMedia);

        w.AddSection("LB · Search", new[]
        {
            B("Enable LaunchBox Metadata Search", "EnableLocalDBSearch", true),
            B("Upload Star Ratings to the LaunchBox Games Database", "UploadStarRatings", true),
            B("Use Community Star Ratings when Filtering or Arranging", "ConsiderCommunityStarRatings", true),
            OptionItem.Text("s", "Minimum number of community ratings in order to use",
                () => s.Get("MinimumCommunityRatingCountBeforeConsidering", "5"),
                v => s.Set("MinimumCommunityRatingCountBeforeConsidering", v))
                .Tag(noImpact: true),
            OptionItem.Toggle("s", "Use Advanced Search Syntax",
                () => !s.GetBool("UseOldSearchSyntax"), v => s.SetBool("UseOldSearchSyntax", !v),
                "Filter switches in the search bar (e.g. genre: Action).")
                .Tag(noImpact: true),
        }, readOnly);
    }

    // ── Startup Applications grid (LB parity; LiteBox LAUNCHES the LaunchBox-
    //    flagged rows at its own boot — see StartupApps.LaunchAll) ────────────
    private static Control BuildStartupAppsPanel(LbSettingsStore s, bool readOnly, out Action apply)
    {
        var panel = new Panel { BackColor = Color.FromArgb(30, 30, 30) };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 34, ForeColor = Color.FromArgb(150, 150, 152),
            Text = "Started when LaunchBox/Big Box launch — LiteBox starts the LaunchBox-flagged rows too.\nDelete key removes the selected row.",
            Font = new Font("Segoe UI", 8.25f),
        };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(37, 37, 38), BorderStyle = BorderStyle.None,
            AllowUserToAddRows = !readOnly, AllowUserToDeleteRows = !readOnly, ReadOnly = readOnly,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(222, 222, 222) },
            DefaultCellStyle =
            {
                BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(222, 222, 222),
                SelectionBackColor = Color.FromArgb(0, 122, 204), SelectionForeColor = Color.White,
            },
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Application Path", FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Command-Line Parameters", FillWeight = 28 });
        var startWith = new DataGridViewComboBoxColumn { HeaderText = "Start With?", FillWeight = 18, FlatStyle = FlatStyle.Flat };
        startWith.Items.AddRange("Both", "LaunchBox", "Big Box");
        grid.Columns.Add(startWith);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Allow Multiple Instances?", FillWeight = 14 });
        grid.DataError += (_, e) => e.ThrowException = false;

        foreach (var a in s.StartupApps)
        {
            string sw = a.StartWithLaunchBox && a.StartWithBigBox ? "Both" : (a.StartWithLaunchBox ? "LaunchBox" : "Big Box");
            int ri = grid.Rows.Add(a.ApplicationPath, a.CommandLine, sw, a.AllowMultipleInstances);
            // Carry the original app on the row so its Extra (unmodelled fields) survives
            // an edit/reorder — new rows have no Tag and start with empty Extra.
            grid.Rows[ri].Tag = a;
        }

        panel.Controls.Add(grid);
        panel.Controls.Add(hint);
        grid.BringToFront();

        apply = () =>
        {
            var list = new List<LbStartupApp>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string path = (row.Cells[0].Value?.ToString() ?? "").Trim();
                if (path.Length == 0) continue;
                string sw = row.Cells[2].Value?.ToString() ?? "Both";
                var app = (row.Tag as LbStartupApp)?.Clone() ?? new LbStartupApp();   // keep Extra of an existing row
                app.ApplicationPath = path;
                app.CommandLine = (row.Cells[1].Value?.ToString() ?? "").Trim();
                app.StartWithLaunchBox = sw is "Both" or "LaunchBox";
                app.StartWithBigBox = sw is "Both" or "Big Box";
                app.AllowMultipleInstances = row.Cells[3].Value is true;
                list.Add(app);
            }
            // Only log a replace op when something actually changed.
            var old = s.StartupApps;
            bool same = old.Count == list.Count && !old.Where((o, i) =>
                o.ApplicationPath != list[i].ApplicationPath || o.CommandLine != list[i].CommandLine ||
                o.StartWithLaunchBox != list[i].StartWithLaunchBox || o.StartWithBigBox != list[i].StartWithBigBox ||
                o.AllowMultipleInstances != list[i].AllowMultipleInstances).Any();
            if (!same) s.SetStartupApps(list);
        };
        return panel;
    }

    // ── Region Priorities (LB parity: checklist + Move Up/Down) ─────────────
    // LB's catalog order: 5 promoted regions then alphabetical. Stored field
    // RegionPriorities holds ONLY the checked regions, comma-joined, in order.
    private static readonly string[] _regionCatalog =
    {
        "World", "Europe", "North America", "Japan", "Asia",
        "Australia", "Brazil", "Canada", "China", "Finland", "France", "Germany",
        "Greece", "Holland", "Hong Kong", "Italy", "Korea", "The Netherlands",
        "Norway", "Oceania", "Russia", "South America", "Spain", "Sweden",
        "Thailand", "United Kingdom", "United States",
    };

    private static Control BuildRegionPrioritiesPanel(LbSettingsStore s, bool readOnly, out Action apply)
    {
        var panel = new Panel { BackColor = Color.FromArgb(30, 30, 30) };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(150, 150, 152),
            Text = "Regions to prioritize for imports and displayed images. Checked = used, in order.",
            Font = new Font("Segoe UI", 8.25f),
        };

        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(222, 222, 222),
            BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true, IntegralHeight = false, Enabled = !readOnly,
        };

        // Build display order: checked regions first (stored priority order),
        // then the remaining catalog regions in catalog order.
        var stored = s.Get("RegionPriorities")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => _regionCatalog.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var ordered = new List<string>(stored);
        foreach (var r in _regionCatalog)
            if (!ordered.Contains(r, StringComparer.OrdinalIgnoreCase)) ordered.Add(r);
        foreach (var r in ordered)
            list.Items.Add(r, stored.Contains(r, StringComparer.OrdinalIgnoreCase));

        var right = new Panel { Dock = DockStyle.Right, Width = 150, BackColor = Color.FromArgb(30, 30, 30) };
        var up = MoveBtn("Move Selected Up", 4);
        var down = MoveBtn("Move Selected Down", 38);
        up.Enabled = down.Enabled = !readOnly;
        void Move(int delta)
        {
            int i = list.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= list.Items.Count) return;
            var item = list.Items[i];
            bool chk = list.GetItemChecked(i);
            list.Items.RemoveAt(i);
            list.Items.Insert(j, item);
            list.SetItemChecked(j, chk);
            list.SelectedIndex = j;
        }
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);
        right.Controls.Add(up); right.Controls.Add(down);

        panel.Controls.Add(list);
        panel.Controls.Add(right);
        panel.Controls.Add(hint);
        list.BringToFront();

        apply = () =>
        {
            var picked = new List<string>();
            for (int i = 0; i < list.Items.Count; i++)
                if (list.GetItemChecked(i)) picked.Add(list.Items[i].ToString());
            var joined = string.Join(",", picked);
            if (joined != s.Get("RegionPriorities")) s.Set("RegionPriorities", joined);
        };
        return panel;
    }

    // ── Automatic Imports Media (LB parity) ────────────────────────────────
    // LB's hardcoded media catalog: ordered (Media Type, Image Group) — extracted
    // from LB's own Options grid (group empty where LB shows it blank). The per-type
    // Download toggle lives in Settings.xml <ImageTypeSettings>/UseInAutoImports;
    // the limit is AutoImportMediaLimit (0 = No Limit). LiteBox does no metadata
    // importing itself, so this is config we keep for LaunchBox's benefit.
    private static readonly (string type, string group)[] _mediaCatalog =
    {
        ("Box - Front", "Boxes"), ("Box - Front - Reconstructed", "Boxes"),
        ("Box - Back", "Box Back"), ("Box - Back - Reconstructed", "Box Back"),
        ("Box - 3D", "3D Boxes"), ("Box - Spine", ""), ("Box - Full", ""),
        ("Advertisement Flyer - Front", "Boxes"), ("Advertisement Flyer - Back", "Box Back"),
        ("Arcade - Cabinet", ""), ("Arcade - Circuit Board", ""), ("Arcade - Control Panel", ""),
        ("Arcade - Controls Information", ""), ("Arcade - Marquee", "Marquees"), ("Banner", "Marquees"),
        ("Cart - Front", "Carts"), ("Cart - Back", "Cart Back"), ("Cart - 3D", "3D Carts"),
        ("Clear Logo", ""), ("Disc", "Carts"),
        ("Fanart - Box - Front", "Boxes"), ("Fanart - Box - Back", "Box Back"),
        ("Fanart - Cart - Front", "Carts"), ("Fanart - Cart - Back", "Cart Back"),
        ("Fanart - Background", "Backgrounds"), ("Fanart - Disc", "Carts"),
        ("Screenshot - Gameplay", "Screenshots"), ("Screenshot - Game Title", "Screenshots"),
        ("Screenshot - Game Select", "Screenshots"), ("Screenshot - Game Over", "Screenshots"),
        ("Screenshot - High Scores", "Screenshots"),
        ("Steam Banner", "Boxes, Marquees"), ("Steam Poster", "Boxes"), ("Steam Screenshot", "Screenshots"),
        ("GOG Poster", "Boxes"), ("GOG Screenshot", "Screenshots"),
        // Store-specific types (rows 37-50) — same grid order as LaunchBox.
        ("Epic Games Background", "Backgrounds"), ("Epic Games Poster", "Boxes"), ("Epic Games Screenshot", "Screenshots"),
        ("Origin Background", "Backgrounds"), ("Origin Poster", "Boxes"), ("Origin Screenshot", "Screenshots"),
        ("Uplay Background", "Backgrounds"), ("Uplay Thumbnail", "Boxes"),
        ("Amazon Background", "Backgrounds"), ("Amazon Screenshot", "Screenshots"), ("Amazon Poster", "Boxes"),
        ("Icon", ""), ("Square", "Boxes"), ("Poster", "Boxes"),
    };

    private static Control BuildAutoImportMediaPanel(LbSettingsStore s, bool readOnly, out Action apply)
    {
        var panel = new Panel { BackColor = Color.FromArgb(30, 30, 30) };

        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(30, 30, 30) };
        top.Controls.Add(new Label
        {
            Text = "Image downloads limit (per image group) — 0 = No Limit:",
            Location = new Point(4, 6), AutoSize = true, ForeColor = Color.FromArgb(222, 222, 222),
        });
        var limit = new TextBox
        {
            Location = new Point(4, 26), Width = 120, Enabled = !readOnly,
            BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(222, 222, 222), BorderStyle = BorderStyle.FixedSingle,
            Text = s.Get("AutoImportMediaLimit", "0"),
        };
        top.Controls.Add(limit);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(37, 37, 38), BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(222, 222, 222) },
            DefaultCellStyle =
            {
                BackColor = Color.FromArgb(37, 37, 38), ForeColor = Color.FromArgb(222, 222, 222),
                SelectionBackColor = Color.FromArgb(0, 122, 204), SelectionForeColor = Color.White,
            },
        };
        var dl = new DataGridViewCheckBoxColumn { HeaderText = "Download", FillWeight = 12, ReadOnly = readOnly };
        var mt = new DataGridViewTextBoxColumn { HeaderText = "Media Type", FillWeight = 50, ReadOnly = true };
        var ig = new DataGridViewTextBoxColumn { HeaderText = "Image Group", FillWeight = 38, ReadOnly = true };
        grid.Columns.AddRange(dl, mt, ig);
        grid.DataError += (_, e) => e.ThrowException = false;

        // Current per-type Download state from Settings.xml (default false if absent).
        var byType = s.ImageTypes.ToDictionary(i => i.ImageType, i => i.UseInAutoImports, StringComparer.OrdinalIgnoreCase);
        // Display order = hardcoded catalog (the only place that knows order + group),
        // then any LIVE ImageTypeSettings type we don't know about, appended (group blank).
        // So a type a future LB version records still appears instead of vanishing.
        var rows = new List<(string type, string group)>(_mediaCatalog);
        var known = new HashSet<string>(_mediaCatalog.Select(c => c.type), StringComparer.OrdinalIgnoreCase);
        foreach (var it in s.ImageTypes)
            if (it.ImageType.Length > 0 && known.Add(it.ImageType)) rows.Add((it.ImageType, ""));
        foreach (var (type, group) in rows)
        {
            bool on = byType.TryGetValue(type, out var v) && v;
            int ri = grid.Rows.Add(on, type, group);
            grid.Rows[ri].Tag = type;
        }

        panel.Controls.Add(grid);
        panel.Controls.Add(top);
        grid.BringToFront();

        apply = () =>
        {
            // Limit (a plain Settings field).
            var lim = (limit.Text ?? "").Trim();
            if (lim.Length == 0) lim = "0";
            if (lim != s.Get("AutoImportMediaLimit", "0")) s.Set("AutoImportMediaLimit", lim);

            // Merge grid Download states into the FULL ImageTypeSettings collection,
            // preserving entries outside the grid (and each entry's IsDefault + Extra).
            var all = s.ImageTypes;                       // ordered, full (incl. non-grid types)
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Count; i++) idx[all[i].ImageType] = i;
            bool changed = false;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not string type) continue;
                bool on = row.Cells[0].Value is true;
                if (idx.TryGetValue(type, out var i))
                {
                    if (all[i].UseInAutoImports != on) { all[i].UseInAutoImports = on; changed = true; }
                }
                else
                {
                    all.Add(new LbImageTypeSetting { ImageType = type, UseInAutoImports = on });
                    changed = true;
                }
            }
            if (changed) s.SetImageTypes(all);
        };
        return panel;
    }

    private static Button MoveBtn(string text, int top) => new()
    {
        Text = text, Location = new Point(4, top), Size = new Size(142, 28),
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 75), ForeColor = Color.White,
        FlatAppearance = { BorderSize = 0 }, Font = new Font("Segoe UI", 8.5f),
    };

    // ── tiny fluent helpers (keep the table above readable) ─────────────────
    private static OptionItem Tag(this OptionItem it, bool noImpact) { it.NoImpact = noImpact; return it; }
    private static OptionItem Values(this OptionItem it, params string[] values) { it.ChoiceValues = values; return it; }
}
