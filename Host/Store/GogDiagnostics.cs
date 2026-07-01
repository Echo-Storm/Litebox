// GOG integration diagnostics for the Options → LB · Integrations → GOG tab. Unlike Steam, GOG is fully
// LOCAL: achievements come from galaxy-2.0.db and owned/installed counts read from that same DB — no web,
// no public profile, and the Galaxy client need NOT be running. So every check here is offline + instant.

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LbApiHost.Host;
using Unbroken.LaunchBox.Plugins;

namespace LbApiHost.Host.Store;

internal static class GogDiagnostics
{
    internal enum GalaxyState { NotInstalled, Installed, Running }

    /// <summary>Three-level Galaxy client status: running / installed-but-not-running / not installed.
    /// Informational only — GOG achievements don't need the client running (they're read from the DB).</summary>
    public static GalaxyState ClientStatus()
    {
        try { if (Process.GetProcessesByName("GalaxyClient").Length > 0) return GalaxyState.Running; } catch { }
        return InstallDir() != null ? GalaxyState.Installed : GalaxyState.NotInstalled;
    }

    private static string? InstallDir()
    {
        foreach (var root in new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\GalaxyClient\paths",
        })
        {
            try
            {
                var p = Microsoft.Win32.Registry.GetValue(root, "client", null)?.ToString();
                if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "GalaxyClient.exe"))) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Is galaxy-2.0.db present? That file IS the achievements source (Galaxy can be closed).</summary>
    public static bool DbPresent() => GalaxyDb.SourceDbPath() != null;

    /// <summary>Public GOG community URL for the profile name (null when empty).</summary>
    public static string? ProfileUrl(string profile)
    {
        profile = (profile ?? "").Trim();
        return profile.Length == 0 ? null : "https://www.gog.com/u/" + Uri.EscapeDataString(profile);
    }

    /// <summary>GOG-source games currently in the LaunchBox library LiteBox loaded.</summary>
    public static int LbGogGameCount()
    {
        try { return PluginHelper.DataManager?.GetAllGames()?.Count(g => StoreSupport.KindOf(g) == StoreKind.Gog) ?? 0; }
        catch { return 0; }
    }

    /// <summary>Owned + installed GOG games straight from galaxy-2.0.db (Products / InstalledBaseProducts).
    /// Each is -1 when GOG isn't installed or the query fails.</summary>
    public static (int owned, int installed) LibraryCounts()
    {
        int owned = -1, installed = -1;
        GalaxyDb.Read(con =>
        {
            try { using var c = con.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM Products"; owned = Convert.ToInt32(c.ExecuteScalar()); } catch { }
            try { using var c = con.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM InstalledBaseProducts"; installed = Convert.ToInt32(c.ExecuteScalar()); } catch { }
        });
        return (owned, installed);
    }
}
