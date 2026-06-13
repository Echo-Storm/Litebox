// LB "Startup Applications" honouring: LiteBox plays the LaunchBox role, so at
// boot it starts every StartupAppSettings row flagged StartWithLaunchBox —
// exactly what LaunchBox does (verified live: LB spawns the app right after its
// own boot). AllowMultipleInstances=false → skipped when a process with the
// same executable name is already running (also covers "LB launched it first").
// Like LB, nothing is killed at exit.

#nullable enable

using System.Diagnostics;
using System.IO;
using LbApiHost.Host.Data;

namespace LbApiHost.Host;

internal static class StartupApps
{
    public static void LaunchAll(LbSettingsStore settings, string lbRoot)
    {
        foreach (var a in settings.StartupApps)
        {
            try
            {
                if (!a.StartWithLaunchBox) continue;
                string path = a.ApplicationPath ?? "";
                if (path.Length == 0) continue;
                if (!Path.IsPathRooted(path)) path = Path.GetFullPath(Path.Combine(lbRoot ?? "", path));
                if (!File.Exists(path)) { Console.WriteLine("[startupapps] missing: " + path); continue; }

                if (!a.AllowMultipleInstances)
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (Process.GetProcessesByName(name).Length > 0)
                    { Console.WriteLine($"[startupapps] already running, skipped: {name}"); continue; }
                }

                var psi = new ProcessStartInfo(path, a.CommandLine ?? "")
                { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" };
                Process.Start(psi);
                Console.WriteLine($"[startupapps] started: {path} {a.CommandLine}");
            }
            catch (Exception ex) { Console.WriteLine("[startupapps] " + ex.Message); }
        }
    }
}
