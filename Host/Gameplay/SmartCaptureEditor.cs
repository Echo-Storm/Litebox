// Reusable "Smart Capture override" editor block, embedded per-emulator (Edit Emulator →
// LiteBox) and per-game (Edit Game → Smart Capture). One checkbox toggles the whole block:
// checked = this entity overrides Smart Capture (its values are stored under scope=entity in
// litebox-options.db); unchecked = inherit global (all rows cleared). The controls are seeded
// from the current override, else from the resolved GLOBAL config so the inherited values show.

#nullable enable

using LbApiHost.Host.Data;

namespace LbApiHost.Host.Gameplay;

internal static class SmartCaptureEditor
{
    /// <summary>Builds the override block for (scope, entityId). Returns the panel to embed and a
    /// save action (call on OK). The panel is fixed-height (~250px scaled).</summary>
    public static (Panel panel, Action save) Build(string scope, string entityId, float s,
        Color bg, Color fg, Color subFg, Color panel2, bool readOnly)
    {
        int S(int px) => (int)Math.Round(px * s);
        var p = new Panel { BackColor = bg, AutoScroll = true };

        // Current override (SmartCaptureEnabled row = the block is overridden), else global effective.
        bool overridden = !string.IsNullOrEmpty(LiteBoxOption.GetOverride(scope, entityId, "SmartCaptureEnabled"));
        var g = GameplaySettings.ResolveSmartCapture(null, null);   // global defaults for display
        string Cur(string key, string glob) { var v = LiteBoxOption.GetOverride(scope, entityId, key); return string.IsNullOrEmpty(v) ? glob : v; }

        Label Lab(string t, int y, Color? c = null) => new() { Text = t, Location = new Point(S(8), S(y)), AutoSize = true, ForeColor = c ?? fg, BackColor = bg };
        CheckBox Chk(string t, bool v, int y) => new() { Text = t, Location = new Point(S(8), S(y)), AutoSize = true, ForeColor = fg, BackColor = bg, Checked = v, Enabled = !readOnly };
        TextBox Txt(string v, int x, int y, int w) => new() { Text = v, Location = new Point(S(x), S(y)), Width = S(w), BackColor = panel2, ForeColor = fg, BorderStyle = BorderStyle.FixedSingle, Enabled = !readOnly };

        var ovr = Chk($"Override Smart Capture for this {(scope == LiteBoxOption.ScopeGame ? "game" : "emulator")}", overridden, 8);
        p.Controls.Add(ovr);
        p.Controls.Add(Lab("Unchecked = use the global Smart Capture settings.", 30, subFg));

        var en = Chk("Enable Smart Capture", Cur("SmartCaptureEnabled", g.Enabled ? "true" : "false").Equals("true", StringComparison.OrdinalIgnoreCase), 60);
        p.Controls.Add(en);

        p.Controls.Add(Lab("Detection mode:", 92));
        var mode = new ComboBox { Location = new Point(S(150), S(89)), Width = S(200), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = panel2, ForeColor = fg, FlatStyle = FlatStyle.Flat, Enabled = !readOnly };
        mode.Items.AddRange(new object[] { "fps", "size", "any" });
        mode.SelectedItem = Cur("SmartCaptureMode", g.Mode); if (mode.SelectedIndex < 0) mode.SelectedIndex = 0;
        p.Controls.Add(mode);

        p.Controls.Add(Lab("Minimum FPS:", 124));
        var fps = Txt(Cur("SmartCaptureMinFps", g.MinFps.ToString()), 150, 121, 70); p.Controls.Add(fps);
        p.Controls.Add(new Label { Text = "sustained (ms):", Location = new Point(S(250), S(124)), AutoSize = true, ForeColor = fg, BackColor = bg });
        var sus = Txt(Cur("SmartCaptureSustainMs", g.SustainMs.ToString()), 370, 121, 70); p.Controls.Add(sus);

        p.Controls.Add(Lab("Min window size (% of screen):", 156));
        var sz = Txt(Cur("SmartCaptureMinSizePct", g.MinSizePct.ToString()), 250, 153, 70); p.Controls.Add(sz);

        p.Controls.Add(Lab("Window title filter (wildcard):", 188));
        var title = Txt(Cur("SmartCaptureTitle", g.Title), 250, 185, 200); p.Controls.Add(title);

        var stopWin = Chk("End session when the game window closes", Cur("SmartCaptureStopOnWindowClose", g.StopOnWindowClose ? "true" : "false").Equals("true", StringComparison.OrdinalIgnoreCase), 218);
        p.Controls.Add(stopWin);

        var sub = new Control[] { en, mode, fps, sus, sz, title, stopWin };
        void Sync() { foreach (var c in sub) c.Enabled = !readOnly && ovr.Checked; }
        ovr.CheckedChanged += (_, _) => Sync();
        Sync();

        void Save()
        {
            if (!ovr.Checked)
            {
                foreach (var k in SmartCaptureConfig.Keys) LiteBoxOption.SetOverride(scope, entityId, k, null);
                return;
            }
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureEnabled", en.Checked ? "true" : "false");
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureMode", mode.SelectedItem as string ?? "fps");
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureMinFps", fps.Text.Trim());
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureSustainMs", sus.Text.Trim());
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureMinSizePct", sz.Text.Trim());
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureTitle", title.Text.Trim());
            LiteBoxOption.SetOverride(scope, entityId, "SmartCaptureStopOnWindowClose", stopWin.Checked ? "true" : "false");
        }

        return (p, Save);
    }
}
