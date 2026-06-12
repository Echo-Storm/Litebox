// Calls ExtendDB's BigBoxWeb/LaunchBoxWeb kiosk toggles by reflection (the host can't reference
// ExtendDB). ExtendDB normally fires these off its own WPF KeyDown class handler (F11=BigBox,
// F10=LaunchBox, F12=DevTools) — which never fires under LiteBox because LiteBox is WinForms, not
// WPF. The host replicates the keys (see Host/HostHotKeys.cs) and routes them here.
//
// Reflected surface (ExtendDB.Forms.BigBoxWebKioskFormsWindow, all public static void):
//   Toggle()           — open/close the BigBoxWeb kiosk (/bigbox)
//   ToggleLaunchBox()  — open/close the LaunchBoxWeb kiosk (/launchbox)   [may be absent on older builds]
//   ShowDevTools()     — DevTools on the live kiosk
//
// Each call is a no-op if ExtendDB isn't loaded or the method is missing (e.g. ToggleLaunchBox on a
// plugin build that predates it) — resolved lazily and cached, failures swallowed.

using System;
using System.Linq;
using System.Reflection;

namespace LbApiHost.Host.Media;

internal static class KioskBridge
{
    private static bool _probed;
    private static MethodInfo _toggle, _toggleLb, _devTools;

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ExtendDB");
            var t = asm?.GetType("ExtendDB.Forms.BigBoxWebKioskFormsWindow");
            if (t == null) return;
            _toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            _toggleLb = t.GetMethod("ToggleLaunchBox", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            _devTools = t.GetMethod("ShowDevTools", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        }
        catch { }
    }

    /// <summary>True iff ExtendDB exposes the kiosk window (the plugin is loaded + new enough).</summary>
    public static bool Available { get { Probe(); return _toggle != null || _toggleLb != null; } }

    public static void ToggleBigBox() { Probe(); _toggle?.Invoke(null, null); }
    public static void ToggleLaunchBox() { Probe(); _toggleLb?.Invoke(null, null); }
    public static void ShowDevTools() { Probe(); _devTools?.Invoke(null, null); }
}
