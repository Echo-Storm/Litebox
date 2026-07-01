// Steamworks interop for reading the CURRENT user's achievement unlock state for one appid — the
// private, local source the user picked over a public profile. Validated 2026-07-01: Init succeeds for
// owned games (installed or not) and GetAchievement* returns localized names + unlock state/time.
//
// Steamworks binds ONE appid per process (steam_appid.txt / SteamAppId read once at SteamAPI_Init),
// so this is meant to run in a SHORT-LIVED helper process — LiteBox re-launches itself as
// "LiteBox.exe --steam-ach <appid>" (see Program.cs), one process per query, exactly like SAM.
//
// We load a bundled genuine Valve steam_api64.dll by explicit path (NativeLibrary) and call the flat
// C API (SteamAPI_* / SteamAPI_ISteamUserStats_*) via delegates, scanning the versioned UserStats
// accessor so the same code works across steam_api64.dll versions.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LbApiHost.Host.Store;

internal static class SteamWorksNative
{
    /// <summary>One achievement as Steam reports it (icon + rarity are filled later from the web schema).</summary>
    internal sealed class RawAch
    {
        public string id { get; set; } = "";    // API name (stable)
        public string? name { get; set; }        // localized display name
        public string? desc { get; set; }        // localized description
        public bool hidden { get; set; }
        public bool unlocked { get; set; }
        public long unlockTime { get; set; }     // unix seconds (0 = locked/unknown)
    }

    // ── flat-API delegates ──────────────────────────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool dInitBool();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int dGetPipe();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr dAccessor();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void dShutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool dReqStats(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint dNumAch(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr dAchName(IntPtr self, uint i);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool dAchTime(IntPtr self, IntPtr name, ref bool achieved, ref uint unlockTime);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr dDispAttr(IntPtr self, IntPtr name, IntPtr key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void dMdInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void dMdRunFrame(int pipe);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool dMdGetNext(int pipe, ref CallbackMsg msg);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void dMdFreeLast(int pipe);

    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackMsg { public int hSteamUser; public int iCallback; public IntPtr pubParam; public int cubParam; }

    private const int k_UserStatsReceived = 1101;   // UserStatsReceived_t

    /// <summary>Reads every achievement + the current user's unlock state for <paramref name="appId"/>.
    /// Returns null with <paramref name="error"/> set on failure (Steam off, app not owned, …). BLOCKING.
    /// MUST run in a process dedicated to this appid (steam_appid.txt already written + CWD set by caller).</summary>
    public static List<RawAch>? Read(string appId, string dllPath, out string error)
    {
        error = "";
        IntPtr lib;
        try { lib = NativeLibrary.Load(dllPath); }
        catch (Exception ex) { error = "load steam_api64.dll: " + ex.Message; return null; }

        T Fn<T>(string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));

        try
        {
            var init = Fn<dInitBool>("SteamAPI_Init");
            if (!init()) { error = "SteamAPI_Init failed (Steam not running / not logged in / app not owned)"; return null; }

            var getPipe = Fn<dGetPipe>("SteamAPI_GetHSteamPipe");
            int pipe = getPipe();

            // ISteamUserStats accessor is versioned in the export name; scan high→low.
            IntPtr stats = IntPtr.Zero;
            for (int v = 16; v >= 9 && stats == IntPtr.Zero; v--)
            {
                string nm = $"SteamAPI_SteamUserStats_v{v:D3}";
                try { stats = Fn<dAccessor>(nm)(); } catch { /* export absent for this version */ }
            }
            var shutdown = Fn<dShutdown>("SteamAPI_Shutdown");
            if (stats == IntPtr.Zero) { error = "no ISteamUserStats accessor"; shutdown(); return null; }

            var reqStats = Fn<dReqStats>("SteamAPI_ISteamUserStats_RequestCurrentStats");
            var numAch = Fn<dNumAch>("SteamAPI_ISteamUserStats_GetNumAchievements");
            var achName = Fn<dAchName>("SteamAPI_ISteamUserStats_GetAchievementName");
            var achTime = Fn<dAchTime>("SteamAPI_ISteamUserStats_GetAchievementAndUnlockTime");
            var dispAttr = Fn<dDispAttr>("SteamAPI_ISteamUserStats_GetAchievementDisplayAttribute");

            reqStats(stats);

            // Pump manual callback dispatch until UserStatsReceived_t (stats loaded) or a short timeout.
            try
            {
                Fn<dMdInit>("SteamAPI_ManualDispatch_Init")();
                var runFrame = Fn<dMdRunFrame>("SteamAPI_ManualDispatch_RunFrame");
                var getNext = Fn<dMdGetNext>("SteamAPI_ManualDispatch_GetNextCallback");
                var freeLast = Fn<dMdFreeLast>("SteamAPI_ManualDispatch_FreeLastCallback");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool got = false;
                while (sw.ElapsedMilliseconds < 4000 && !got)
                {
                    runFrame(pipe);
                    var msg = new CallbackMsg();
                    while (getNext(pipe, ref msg))
                    {
                        if (msg.iCallback == k_UserStatsReceived) got = true;
                        freeLast(pipe);
                    }
                    System.Threading.Thread.Sleep(40);
                }
            }
            catch { /* no manual dispatch on a very old DLL → read cached state anyway */ }

            var list = new List<RawAch>();
            uint n = numAch(stats);
            for (uint i = 0; i < n; i++)
            {
                string id = Utf8(achName(stats, i));
                if (string.IsNullOrEmpty(id)) continue;
                var idPtr = Marshal.StringToCoTaskMemUTF8(id);
                try
                {
                    bool achieved = false; uint t = 0;
                    achTime(stats, idPtr, ref achieved, ref t);
                    list.Add(new RawAch
                    {
                        id = id,
                        name = Attr(dispAttr, stats, idPtr, "name"),
                        desc = Attr(dispAttr, stats, idPtr, "desc"),
                        hidden = Attr(dispAttr, stats, idPtr, "hidden") == "1",
                        unlocked = achieved,
                        unlockTime = achieved ? t : 0,
                    });
                }
                finally { Marshal.FreeCoTaskMem(idPtr); }
            }
            shutdown();
            return list;
        }
        catch (Exception ex) { error = "steamworks read: " + ex.Message; return null; }
    }

    private static string? Attr(dDispAttr fn, IntPtr stats, IntPtr namePtr, string key)
    {
        var keyPtr = Marshal.StringToCoTaskMemUTF8(key);
        try { return Utf8(fn(stats, namePtr, keyPtr)); }
        finally { Marshal.FreeCoTaskMem(keyPtr); }
    }

    private static string Utf8(IntPtr p) => p == IntPtr.Zero ? "" : (Marshal.PtrToStringUTF8(p) ?? "");
}
