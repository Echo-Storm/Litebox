// Dev tool (--dump-emupresets): read LB's Metadata DB (LB\Metadata\LaunchBox.Metadata.db)
// and dump the schema + content of the "Emulators" / "EmulatorPlatforms" tables — the
// presets behind LaunchBox's "Add Emulator" dialog (NOT baked into the executable).
// Read-only (Mode=ReadOnly) so we can run while LB owns the file.
//
// JIT gotcha (same as --dump-oplog): the Microsoft.Data.Sqlite resolver must be
// registered BEFORE the typed method is JIT-ed → wrapper + NoInlining core.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;

namespace LbApiHost.Tools;

internal static class EmuPresetDump
{
    public static int Run(string[] args)
    {
        // Sqlite managed + native bits live in LB\Core (the deploy), not in bin.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var p = Path.Combine(@"C:\Users\mehdi\source\repos\scrapper-project\LB\Core", name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
        return Core(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Core(string[] args)
    {
        var dbPath = @"C:\Users\mehdi\source\repos\scrapper-project\LB\Metadata\LaunchBox.Metadata.db";
        if (!File.Exists(dbPath)) { Console.WriteLine("Metadata DB not found: " + dbPath); return 1; }

        // Optional filter: --dump-emupresets <substring>  → only emulators whose
        // Name contains the substring (case-insensitive). No filter = full dump.
        string? filter = args.SkipWhile(a => a != "--dump-emupresets").Skip(1).FirstOrDefault(a => !a.StartsWith("--"));

        using var con = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());
        con.Open();

        // 1) Schemas.
        Console.WriteLine("=== sqlite_master ===");
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                if (name is "Emulators" or "EmulatorPlatforms")
                    Console.WriteLine(r.IsDBNull(1) ? name : r.GetString(1));
                else
                    Console.WriteLine($"-- (other table: {name})");
            }
        }

        // 2) Row counts.
        foreach (var t in new[] { "Emulators", "EmulatorPlatforms" })
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{t}\"";
            Console.WriteLine($"\n{t}: {cmd.ExecuteScalar()} rows");
        }

        // 3) Emulators rows (all columns, generic dump).
        Console.WriteLine("\n=== Emulators ===");
        DumpTable(con, "Emulators", filter == null ? null : ("Name", filter));

        // 4) EmulatorPlatforms — joined per emulator when filtered, otherwise a sample.
        Console.WriteLine("\n=== EmulatorPlatforms ===");
        if (filter != null)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT * FROM EmulatorPlatforms WHERE Emulator LIKE '%' || $f || '%' ORDER BY Emulator, Platform";
            cmd.Parameters.AddWithValue("$f", filter);
            DumpReader(cmd);
        }
        else
        {
            DumpTable(con, "EmulatorPlatforms", null, limit: 40);
        }
        return 0;
    }

    private static void DumpTable(Microsoft.Data.Sqlite.SqliteConnection con, string table, (string col, string val)? like, int limit = 0)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{table}\"";
        if (like is { } l)
        {
            cmd.CommandText += $" WHERE \"{l.col}\" LIKE '%' || $f || '%'";
            cmd.Parameters.AddWithValue("$f", l.val);
        }
        if (limit > 0) cmd.CommandText += $" LIMIT {limit}";
        DumpReader(cmd);
    }

    private static void DumpReader(Microsoft.Data.Sqlite.SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
        int n = 0;
        while (r.Read())
        {
            var sb = new StringBuilder();
            sb.Append("--- row ").Append(++n).Append(" ---\n");
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (r.IsDBNull(i)) continue;   // skip nulls to keep output readable
                var v = r.GetValue(i)?.ToString() ?? "";
                if (v.Length > 300) v = v.Substring(0, 300) + "…";
                sb.Append("  ").Append(cols[i]).Append(" = ").Append(v.Replace("\r\n", "\\n").Replace("\n", "\\n")).Append('\n');
            }
            Console.Write(sb.ToString());
        }
        Console.WriteLine($"({n} rows)");
    }
}
