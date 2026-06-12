// LB's Add-Emulator presets. They are NOT baked into LaunchBox.exe: they live in
// LB\Metadata\LaunchBox.Metadata.db (SQLite), tables "Emulators" (35 rows: name,
// default command line, binary file name, flags) and "EmulatorPlatforms" (98 rows:
// per-platform command line — e.g. RetroArch's `-L "cores\xxx_libretro.dll" -f` —
// recommended flag, required BIOS hint). We read them read-only so the dump works
// even while LaunchBox owns the file; missing DB → empty list (Custom-only Add).

#nullable enable

using System.IO;
using System.Runtime.CompilerServices;

namespace LbApiHost.Host.Emulators;

internal sealed class EmuPreset
{
    public string Name = "";
    public string CommandLine = "";
    public string BinaryFileName = "";
    public string Url = "";
    public bool NoQuotes, NoSpace, HideConsole, FileNameOnly, AutoExtract;
    public readonly List<EmuPresetPlatform> Platforms = new();
}

internal sealed class EmuPresetPlatform
{
    public string Platform = "";
    public string CommandLine = "";
    public bool Recommended;
    public string RequiredBiosFile = "";
}

internal static class EmuPresets
{
    private static List<EmuPreset>? _cache;

    /// <summary>All presets, name-sorted. Cached for the process lifetime; empty on any failure.</summary>
    public static List<EmuPreset> Load(string lbRoot)
    {
        if (_cache != null) return _cache;
        try { _cache = LoadCore(lbRoot); }
        catch (Exception ex) { Console.WriteLine("[empresets] load failed: " + ex.Message); _cache = new(); }
        return _cache;
    }

    // Typed Microsoft.Data.Sqlite refs isolated behind NoInlining so Load() itself
    // can JIT (and fail soft) even if the sqlite assembly can't be resolved.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<EmuPreset> LoadCore(string lbRoot)
    {
        var result = new List<EmuPreset>();
        var dbPath = Path.Combine(lbRoot ?? "", "Metadata", "LaunchBox.Metadata.db");
        if (!File.Exists(dbPath)) { Console.WriteLine("[empresets] no metadata db: " + dbPath); return result; }

        using var con = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());
        con.Open();

        var byName = new Dictionary<string, EmuPreset>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT Name, CommandLine, URL, BinaryFileName, NoQuotes, NoSpace, HideConsole, FileNameOnly, AutoExtract FROM Emulators";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = new EmuPreset
                {
                    Name = r.GetString(0),
                    CommandLine = r.IsDBNull(1) ? "" : r.GetString(1),
                    Url = r.IsDBNull(2) ? "" : r.GetString(2),
                    BinaryFileName = r.GetString(3),
                    NoQuotes = r.GetInt64(4) != 0,
                    NoSpace = r.GetInt64(5) != 0,
                    HideConsole = r.GetInt64(6) != 0,
                    FileNameOnly = r.GetInt64(7) != 0,
                    AutoExtract = r.GetInt64(8) != 0,
                };
                byName[p.Name] = p;
                result.Add(p);
            }
        }
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT Emulator, Platform, CommandLine, Recommended, RequiredBiosFile FROM EmulatorPlatforms ORDER BY Platform";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!byName.TryGetValue(r.GetString(0), out var owner)) continue;
                owner.Platforms.Add(new EmuPresetPlatform
                {
                    Platform = r.GetString(1),
                    CommandLine = r.IsDBNull(2) ? "" : r.GetString(2),
                    Recommended = r.GetInt64(3) != 0,
                    RequiredBiosFile = r.IsDBNull(4) ? "" : r.GetString(4),
                });
            }
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"[empresets] loaded {result.Count} presets, {result.Sum(p => p.Platforms.Count)} platform rows");
        return result;
    }
}
