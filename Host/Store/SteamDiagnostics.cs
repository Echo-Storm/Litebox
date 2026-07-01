// Steam integration diagnostics for the Options → LB · Integrations → Steam tab: validate the API key,
// resolve the profile, detect whether "Game details" is public (web achievements) vs client-only, and
// gather library stats. All network calls are bounded by the caller's CancellationToken.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using LbApiHost.Host;
using Unbroken.LaunchBox.Plugins;

namespace LbApiHost.Host.Store;

internal static class SteamDiagnostics
{
    /// <summary>Outcome of the online probe (key validity, profile, public-achievements, owned count).</summary>
    internal sealed class Probe
    {
        public bool KeyValid;
        public string? SteamId;
        public int OwnedCount = -1;         // -1 = unknown
        public bool? GameDetailsPublic;     // null = couldn't determine
        public string? Note;                // short reason when a check couldn't run
    }

    private static readonly HttpClient Http = BuildClient();
    private static HttpClient BuildClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try { c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)"); } catch { }
        return c;
    }

    private static (int status, string? body) Get(string url, CancellationToken ct)
    {
        try
        {
            using var resp = Http.GetAsync(url, ct).GetAwaiter().GetResult();
            return ((int)resp.StatusCode, resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
        }
        catch { return (-1, null); }
    }

    // ── instant (local) checks ───────────────────────────────────────────────────────────────
    internal enum ClientState { NotInstalled, Installed, Running }

    public static bool ClientRunning()
    { try { return Process.GetProcessesByName("steam").Length > 0; } catch { return false; } }

    /// <summary>Three-level client status: running / installed-but-not-running / not installed.</summary>
    public static ClientState ClientStatus()
    {
        try { if (Process.GetProcessesByName("steam").Length > 0) return ClientState.Running; } catch { }
        return SteamInstallPath() != null ? ClientState.Installed : ClientState.NotInstalled;
    }

    /// <summary>Steam's install directory (steam.exe present), from the registry — null when not installed.</summary>
    private static string? SteamInstallPath()
    {
        foreach (var (root, val) in new[]
        {
            (@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"),
        })
        {
            try
            {
                var p = Microsoft.Win32.Registry.GetValue(root, val, null)?.ToString();
                if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p.Replace('/', '\\'), "steam.exe")))
                    return p.Replace('/', '\\');
            }
            catch { }
        }
        return null;
    }

    private static bool IsSteamId64(string s) => s.Length == 17 && s.All(char.IsDigit);

    /// <summary>Public community URL for a vanity name or steamid64 (null when empty).</summary>
    public static string? ProfileUrl(string user)
    {
        user = (user ?? "").Trim();
        if (user.Length == 0) return null;
        return IsSteamId64(user) ? $"https://steamcommunity.com/profiles/{user}" : $"https://steamcommunity.com/id/{user}";
    }

    /// <summary>The privacy-settings page for the profile (where "Game details" is toggled).</summary>
    public static string? PrivacyUrl(string user)
        => ProfileUrl(user) is string u ? u.TrimEnd('/') + "/edit/settings" : null;

    /// <summary>Steam-source games currently in the LaunchBox library LiteBox loaded.</summary>
    public static int LbSteamGameCount()
    {
        try { return PluginHelper.DataManager?.GetAllGames()?.Count(g => StoreSupport.KindOf(g) == StoreKind.Steam) ?? 0; }
        catch { return 0; }
    }

    /// <summary>Installed Steam games = appmanifest_*.acf across every Steam library (libraryfolders.vdf),
    /// minus the redistributables entry. -1 when Steam's install path can't be found.</summary>
    public static int InstalledCount()
    {
        try
        {
            string? steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null)?.ToString();
            if (string.IsNullOrEmpty(steamPath)) return -1;
            steamPath = steamPath.Replace('/', '\\');
            var libs = new List<string> { Path.Combine(steamPath, "steamapps") };
            foreach (var vdf in new[] { Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"), Path.Combine(steamPath, "config", "libraryfolders.vdf") })
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    {
                        var lib = Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps");
                        if (!libs.Contains(lib, StringComparer.OrdinalIgnoreCase)) libs.Add(lib);
                    }
                    break;
                }
            int n = 0;
            foreach (var lib in libs)
                try { if (Directory.Exists(lib)) n += Directory.EnumerateFiles(lib, "appmanifest_*.acf").Count(f => !f.EndsWith("appmanifest_228980.acf", StringComparison.OrdinalIgnoreCase)); }
                catch { }
            return n;
        }
        catch { return -1; }
    }

    // ── online probe (bounded by ct) ───────────────────────────────────────────────────────────
    public static Probe Run(string key, string user, CancellationToken ct)
    {
        var r = new Probe();
        key = (key ?? "").Trim(); user = (user ?? "").Trim();
        if (key.Length == 0) { r.Note = "no API key set"; return r; }

        // 1) resolve steamid + validate the key (a 200 from a key-gated endpoint ⇒ valid key)
        if (IsSteamId64(user))
        {
            r.SteamId = user;
            var (st, _) = Get($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={user}", ct);
            r.KeyValid = st == 200;
            if (st == 401 || st == 403) return r;   // bad key
        }
        else if (user.Length > 0)
        {
            var (st, body) = Get($"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key={key}&vanityurl={Uri.EscapeDataString(user)}", ct);
            if (st == 401 || st == 403) { r.KeyValid = false; return r; }
            if (st == 200 && body != null)
            {
                r.KeyValid = true;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("response", out var resp)
                        && resp.TryGetProperty("success", out var s) && s.GetInt32() == 1
                        && resp.TryGetProperty("steamid", out var sid))
                        r.SteamId = sid.GetString();
                    else r.Note = "profile id not found";
                }
                catch { }
            }
        }
        else { r.Note = "enter your profile id to validate the key"; return r; }

        if (!r.KeyValid || string.IsNullOrEmpty(r.SteamId)) return r;

        // 2) owned games (count + appids by playtime, to probe "Game details")
        var appIds = new List<string>();
        var (ost, obody) = Get($"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={key}&steamid={r.SteamId}&include_played_free_games=1&format=json", ct);
        if (ost == 200 && obody != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(obody);
                if (doc.RootElement.TryGetProperty("response", out var resp))
                {
                    if (resp.TryGetProperty("game_count", out var gc)) r.OwnedCount = gc.GetInt32();
                    if (resp.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array)
                        appIds = games.EnumerateArray()
                            .Select(g => (id: g.TryGetProperty("appid", out var a) ? a.GetInt64() : 0,
                                          pt: g.TryGetProperty("playtime_forever", out var p) ? p.GetInt64() : 0))
                            .Where(x => x.id > 0).OrderByDescending(x => x.pt).Select(x => x.id.ToString()).ToList();
                }
            }
            catch { }
        }

        // 3) "Game details" public? GetPlayerAchievements: 403 = private, 200+success = public. Probe the
        //    most-played games until one is definitive (skip games with no achievements → 400/no stats).
        r.GameDetailsPublic = CheckPublic(key, r.SteamId!, appIds, ct);
        return r;
    }

    private static bool? CheckPublic(string key, string steamId, IEnumerable<string> appIds, CancellationToken ct)
    {
        int tried = 0;
        foreach (var appId in appIds)
        {
            if (ct.IsCancellationRequested || tried >= 6) break;
            tried++;
            var (st, body) = Get($"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?appid={appId}&key={key}&steamid={steamId}", ct);
            if (st == 403) return false;                 // profile "Game details" is private
            if (st == 200 && body != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("playerstats", out var ps)
                        && ps.TryGetProperty("success", out var ok) && ok.GetBoolean())
                        return true;                     // achievements returned → public
                }
                catch { }
                // success=false (game has no stats) → try the next game
            }
        }
        return null;   // no game gave a definitive answer
    }
}
