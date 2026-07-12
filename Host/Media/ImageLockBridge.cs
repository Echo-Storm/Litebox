// Per-file image LOCK, reflected onto ExtendDB's ExtendDB.Utility.ImageLockAds (NTFS ":lock" ADS or a
// .ads/<name>.json sidecar). The lock blocks LaunchBox/ExtendDB from deleting an image the user chose to
// keep — it is an ExtendDB feature, so it is only AVAILABLE when the plugin is loaded; the LiteBox Images
// editor shows the lock UI only then (Available == false → the buttons are omitted). No-ops when absent.

#nullable enable

using System;
using System.Linq;
using System.Reflection;

namespace LbApiHost.Host.Media;

internal static class ImageLockBridge
{
    private static bool _probed;
    private static MethodInfo? _isLocked, _lock, _unlock, _toggle;

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ExtendDB");
            var t = asm?.GetType("ExtendDB.Utility.ImageLockAds");
            if (t == null) return;
            _isLocked = t.GetMethod("IsLocked", new[] { typeof(string) });
            _lock = t.GetMethod("Lock", new[] { typeof(string) });
            _unlock = t.GetMethod("Unlock", new[] { typeof(string) });
            _toggle = t.GetMethod("Toggle", new[] { typeof(string) });
        }
        catch { }
    }

    /// <summary>True only when ExtendDB's ImageLockAds is reflectable — gate the whole lock UI on this.</summary>
    public static bool Available { get { Probe(); return _isLocked != null; } }

    public static bool IsLocked(string path)
    { Probe(); try { return path != null && _isLocked?.Invoke(null, new object[] { path }) is true; } catch { return false; } }

    public static void Lock(string path)
    { Probe(); try { if (path != null) _lock?.Invoke(null, new object[] { path }); } catch { } }

    public static void Unlock(string path)
    { Probe(); try { if (path != null) _unlock?.Invoke(null, new object[] { path }); } catch { } }

    /// <summary>Flip the lock and return the NEW state (false on any failure / when unavailable).</summary>
    public static bool Toggle(string path)
    { Probe(); try { return path != null && _toggle?.Invoke(null, new object[] { path }) is true; } catch { return false; } }
}
