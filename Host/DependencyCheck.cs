// Launch-time dependency pre-check, modelled on LaunchBox's "Missing Dependency
// Files" dialog. The data comes from the emulator's INTEGRATION plugin
// (EmulatorPlugin.GetBiosFilesForPlatform → EmulatorBiosFile{Location, FileName,
// Required, Md5}); emulators without a plugin are never blocked.
//
// Flow (HostLaunch, right before the main spawn, on the launch worker):
//   • required bios files for (emulator, game.platform) that are MISSING on disk
//     → a modal dark dialog (on the UI thread): "Play Anyway" continues the
//     launch, "Cancel Launch" aborts; a "Don't show this again for this
//     platform/emulator" checkbox persists the skip in LiteBox.ini
//     (SkipDepCheck.<emulatorId>.<platform>=true).
//   • LB's "Verify Dependency Files" (auto-download) is NOT replicated yet —
//     that ties into the InstallEmulator flow (Edit Emulator V3+).
//
// Master switch: LiteBox.ini CheckDependencies (default true).

#nullable enable

using System.IO;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbApiHost.Host;

internal static class DependencyCheck
{
    private static LiteBoxConfig? _cfg;
    private static string _lbRoot = "";

    public static void Configure(LiteBoxConfig cfg, string lbRoot) { _cfg = cfg; _lbRoot = lbRoot; }

    /// <summary>True → continue the launch; false → user cancelled. Never throws,
    /// never blocks emulators without an integration plugin.</summary>
    public static bool PreLaunchCheck(IEmulator emulator, IGame game)
    {
        try
        {
            if (_cfg != null && !_cfg.GetBool("CheckDependencies", true)) return true;
            string platform = "";
            try { platform = game?.Platform ?? ""; } catch { }
            if (platform.Length == 0) return true;

            string emuId = "";
            try { emuId = emulator?.Id ?? ""; } catch { }
            string skipKey = $"SkipDepCheck.{emuId}.{platform}";
            if (_cfg != null && _cfg.GetBool(skipKey, false)) return true;

            var missing = MissingRequiredFiles(emulator!, platform);
            if (missing.Count == 0) return true;

            Console.WriteLine($"[deps] {missing.Count} required dependency file(s) missing for \"{platform}\":");
            foreach (var m in missing) Console.WriteLine("  - " + m);

            bool play = true, dontShowAgain = false;
            UiThread.Invoke(() =>
            {
                using var dlg = new MissingDepsDialog(missing);
                play = dlg.ShowDialog() == DialogResult.OK;
                dontShowAgain = dlg.DontShowAgain;
            });
            if (dontShowAgain && _cfg != null) { _cfg.SetBool(skipKey, true); _cfg.Save(); }
            if (!play) Console.WriteLine("[deps] launch cancelled by the user.");
            return play;
        }
        catch (Exception ex) { Console.WriteLine("[deps] check failed (launch continues): " + ex.Message); return true; }
    }

    /// <summary>Missing dependency lines, with LB's GROUP semantics (probed on the
    /// RetroArch plugin):
    ///   • a file in NO group (or in a group with AllItemsRequired): its own
    ///     Required flag decides — missing → one line per file;
    ///   • a file in a group with IsGroupRequired + !AllItemsRequired: AT LEAST
    ///     ONE file of the group must exist (e.g. PS1 "Regional BIOS" —
    ///     scph5500/5501/5502, any region suffices) — the per-file Required flag
    ///     is superseded by the group rule.
    /// Paths resolve against the emulator's folder (Location is emulator-relative,
    /// e.g. "system").</summary>
    private static List<string> MissingRequiredFiles(IEmulator emulator, string platform)
    {
        var result = new List<string>();
        var files = EmuPlugins.BiosFiles(emulator, platform).ToList();
        if (files.Count == 0) return result;

        string emuDir = "";
        try
        {
            var p = emulator.ApplicationPath ?? "";
            if (!Path.IsPathRooted(p)) p = Path.GetFullPath(Path.Combine(_lbRoot, p));
            emuDir = Path.GetDirectoryName(p) ?? "";
        }
        catch { }
        if (emuDir.Length == 0) return result;

        string Rel(EmulatorBiosFile f)
        {
            string loc = "", name = "";
            try { loc = f.Location ?? ""; name = f.FileName ?? ""; } catch { }
            return Path.Combine(loc.Trim('\\', '/'), name).Replace('/', '\\');
        }
        bool Exists(EmulatorBiosFile f)
        {
            try { return File.Exists(Path.Combine(emuDir, Rel(f))); } catch { return false; }
        }

        // At-least-one groups (IsGroupRequired, not AllItemsRequired).
        var atLeastOneGroups = files
            .Where(f => Safe(() => f.ApplicableGroup) is { IsGroupRequired: true, AllItemsRequired: false })
            .GroupBy(f => Safe(() => f.ApplicableGroup!.Id) ?? "");
        var grouped = new HashSet<EmulatorBiosFile>();
        foreach (var g in atLeastOneGroups)
        {
            var members = g.ToList();
            foreach (var m in members) grouped.Add(m);
            if (!members.Any(Exists))
            {
                string desc = Safe(() => members[0].ApplicableGroup!.Description) ?? "";
                result.Add($"one of:  {string.Join("  ·  ", members.Select(m => "\\" + Rel(m)))}"
                           + (desc.Length > 0 ? $"   ({desc})" : ""));
            }
        }

        // Ungrouped (or all-items groups): per-file Required.
        foreach (var f in files)
        {
            if (grouped.Contains(f)) continue;
            bool required = Safe(() => f.Required);
            var grp = Safe(() => f.ApplicableGroup);
            if (grp is { IsGroupRequired: true, AllItemsRequired: true }) required = true;
            if (!required) continue;
            if (!Exists(f)) result.Add("\\" + Rel(f));
        }
        return result;
    }

    private static T? Safe<T>(Func<T?> f) { try { return f(); } catch { return default; } }

    // ── The dialog (dark, LB-style wording) ──────────────────────────────
    private sealed class MissingDepsDialog : Form
    {
        public bool DontShowAgain { get; private set; }

        public MissingDepsDialog(List<string> missing)
        {
            Text = "Missing Dependency Files";
            Size = new Size(560, 260 + Math.Min(5, missing.Count) * 20);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30); ForeColor = Color.FromArgb(222, 222, 222);
            Font = new Font("Segoe UI", 9.5f);

            var head = new Label
            {
                Text = "Heads Up!  There are missing dependency files that may be required to play this game.",
                Location = new Point(16, 14), Size = new Size(515, 36),
            };
            Controls.Add(head);

            var list = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Location = new Point(16, 54), Size = new Size(515, Math.Min(5, missing.Count) * 20 + 24),
                BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(235, 150, 150),
                BorderStyle = BorderStyle.FixedSingle,
                Text = string.Join("\r\n", missing),
            };
            Controls.Add(list);

            var chk = new CheckBox
            {
                Text = "Don't show this again for this platform/emulator",
                Location = new Point(16, list.Bottom + 12), AutoSize = true,
            };
            chk.CheckedChanged += (_, _) => DontShowAgain = chk.Checked;
            Controls.Add(chk);

            var play = Btn("Play Anyway", Color.FromArgb(50, 110, 65));
            play.Location = new Point(290, chk.Bottom + 16);
            play.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(play);
            var cancel = Btn("Cancel Launch", Color.FromArgb(60, 60, 75));
            cancel.Location = new Point(412, chk.Bottom + 16);
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            AcceptButton = play; CancelButton = cancel;
        }

        private static Button Btn(string text, Color back) => new()
        {
            Text = text, Size = new Size(116, 30),
            FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White,
            FlatAppearance = { BorderSize = 0 }, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
    }
}
