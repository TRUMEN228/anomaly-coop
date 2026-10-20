using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CoopLauncher
{
    /// <summary>Packs the launcher, mod and engine logs into one zip on the desktop for bug reports.</summary>
    public static class LogCollector
    {
        public static string Collect(string gameDir)
        {
            var zip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"anomaly-coop-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            var appdata = Path.Combine(gameDir ?? "", "appdata");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                void Add(string path, string name)
                {
                    if (!File.Exists(path)) return;
                    // the game may still be writing its log: copy through a shared read
                    using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var dst = archive.CreateEntry(name).Open())
                        src.CopyTo(dst);
                }
                Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coop_launcher.log"), "coop_launcher.log");
                foreach (var f in new[] { "coop_log.txt", "coop_out.txt", "coop_in.txt", "coop_keys.txt" })
                    Add(Path.Combine(appdata, f), f);
                var logs = Path.Combine(appdata, "logs");
                if (Directory.Exists(logs))
                    foreach (var f in new DirectoryInfo(logs).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime).Take(2))
                        Add(f.FullName, "logs/" + f.Name);
                var info = $"launcher {Updater.CurrentText}\r\nwindows {Environment.OSVersion}\r\ntime {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n";
                using (var w = new StreamWriter(archive.CreateEntry("info.txt").Open())) w.Write(info);
            }
            return zip;
        }
    }
}
