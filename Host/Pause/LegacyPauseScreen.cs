// "LaunchBox legacy" pause mode: a borderless, top-most, full-screen dark overlay with
// a vertical action menu (Resume / Save State / Load State / Reset / Swap Discs /
// Exit Game). Keyboard-driven (Up/Down + Enter, Esc = Resume) and clickable.
//
// Presentation only — every action is reported through PauseContext.OnAction and the
// PauseManager runs the mechanics (resume the process, fire the AHK scripts, …).
//
// Forceful activation: the emulator may fight for the foreground right after being
// suspended; when ctx.ForcefulActivation is set we re-assert TopMost + foreground a few
// times (LB's ForcefulPauseScreenActivation equivalent).

#nullable enable

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LbApiHost.Host.Pause;

internal sealed class LegacyPauseScreen : IPauseScreen
{
    private PauseForm? _form;

    public bool IsOpen => _form is { IsDisposed: false, Visible: true };

    public void Show(PauseContext ctx)
    {
        Close();
        _form = new PauseForm(ctx);
        _form.Show();
        _form.ForceToFront(ctx.ForcefulActivation ? 8 : 2);
    }

    public void Close()
    {
        try { if (_form is { IsDisposed: false }) _form.Close(); } catch { }
        try { _form?.Dispose(); } catch { }
        _form = null;
    }

    // ── The overlay form ──────────────────────────────────────────────
    private sealed class PauseForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static readonly Color Bg = Color.FromArgb(18, 18, 24);
        private static readonly Color Fg = Color.FromArgb(235, 235, 235);
        private static readonly Color Dim = Color.FromArgb(150, 150, 160);
        private static readonly Color Hi = Color.FromArgb(45, 110, 200);

        private readonly PauseContext _ctx;
        private readonly List<(PauseAction action, Button btn)> _items = new();
        private int _sel;

        public PauseForm(PauseContext ctx)
        {
            _ctx = ctx;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Bg;
            KeyPreview = true;
            Cursor = Cursors.Default;

            var title = new Label
            {
                Text = _ctx.GameTitle,
                Font = new Font("Segoe UI", 26f, FontStyle.Bold),
                ForeColor = Fg, BackColor = Color.Transparent,
                AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 110, Padding = new Padding(0, 40, 0, 0),
            };
            Controls.Add(title);
            var sub = new Label
            {
                Text = string.IsNullOrEmpty(_ctx.Platform) ? "PAUSED" : _ctx.Platform + "  —  PAUSED",
                Font = new Font("Segoe UI", 11f),
                ForeColor = Dim, BackColor = Color.Transparent,
                AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 30,
            };
            Controls.Add(sub);
            sub.BringToFront();

            // Action menu, centred vertically below the title.
            var menu = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            Controls.Add(menu);
            menu.BringToFront();

            void Add(PauseAction a, string label, bool enabled = true)
            {
                if (!enabled) return;
                var b = new Button
                {
                    Text = label,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Bg, ForeColor = Fg,
                    FlatAppearance = { BorderSize = 0, MouseOverBackColor = Color.FromArgb(35, 35, 45) },
                    Font = new Font("Segoe UI", 15f),
                    Size = new Size(420, 52),
                    TabStop = false,
                };
                b.Click += (_, _) => Fire(a);
                _items.Add((a, b));
                menu.Controls.Add(b);
            }

            Add(PauseAction.Resume, "Resume");
            Add(PauseAction.SaveState, "Save State", _ctx.CanSaveState);
            Add(PauseAction.LoadState, "Load State", _ctx.CanLoadState);
            Add(PauseAction.Reset, "Reset", _ctx.CanReset);
            Add(PauseAction.SwapDiscs, "Swap Discs", _ctx.CanSwapDiscs);
            Add(PauseAction.ExitGame, "Exit Game");

            menu.Resize += (_, _) => LayoutMenu(menu);
            LayoutMenu(menu);
            Select(0);

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Fire(PauseAction.Resume); e.Handled = true; }
                else if (e.KeyCode == Keys.Up) { Select((_sel - 1 + _items.Count) % _items.Count); e.Handled = true; }
                else if (e.KeyCode == Keys.Down) { Select((_sel + 1) % _items.Count); e.Handled = true; }
                else if (e.KeyCode is Keys.Enter or Keys.Space) { Fire(_items[_sel].action); e.Handled = true; }
            };
        }

        private void LayoutMenu(Panel menu)
        {
            int totalH = _items.Count * 58;
            int y = Math.Max(10, (menu.Height - totalH) / 2 - 30);
            foreach (var (_, b) in _items)
            {
                b.Location = new Point((menu.Width - b.Width) / 2, y);
                y += 58;
            }
        }

        private void Select(int i)
        {
            _sel = Math.Max(0, Math.Min(_items.Count - 1, i));
            for (int k = 0; k < _items.Count; k++)
            {
                var b = _items[k].btn;
                b.BackColor = k == _sel ? Hi : Bg;
                b.ForeColor = k == _sel ? Color.White : Fg;
            }
        }

        private void Fire(PauseAction a)
        {
            try { _ctx.OnAction?.Invoke(a); } catch { }
        }

        /// <summary>Re-assert top-most + foreground a few times — emulators (especially
        /// exclusive-fullscreen ones) can win the first foreground fight.</summary>
        public void ForceToFront(int attempts)
        {
            int n = 0;
            var t = new System.Windows.Forms.Timer { Interval = 250 };
            t.Tick += (_, _) =>
            {
                if (IsDisposed || !Visible || n++ >= attempts) { t.Stop(); t.Dispose(); return; }
                try { TopMost = true; Activate(); SetForegroundWindow(Handle); } catch { }
            };
            try { Activate(); SetForegroundWindow(Handle); } catch { }
            t.Start();
        }
    }
}
