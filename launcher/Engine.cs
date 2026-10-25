using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CoopLauncher
{
    /// <summary>
    /// The co-op engine: exes built from TRUMEN228/xray-monolith (branch coop) that place the partner's puppet
    /// smoothly. They are installed next to the stock exes under their own names and start with their own
    /// fsgame_coop.ltx, which adds the modded exes' gamedata. Stock files are never touched, so turning the
    /// option off is just starting the stock exe again.
    /// </summary>
    public static class Engine
    {
        const string Repo = "TRUMEN228/xray-monolith";
        const string Folder = "coop_engine";      // in the game dir: gamedata archive and version
        const string FsLtx = "fsgame_coop.ltx";
        static readonly string[] Exes = { "AnomalyDX11AVX.exe", "AnomalyDX11.exe" };
        const string Archive = "coop_engine_gamedata.db0";

        public sealed class Release { public int Version; public string Tag; public Dictionary<string, string> Assets = new Dictionary<string, string>(); }

        static string CoopExeName(string stock) => Path.GetFileNameWithoutExtension(stock) + "_coop.exe";

        /// <summary>Installed revision in this game, 0 when there is none (or it is incomplete).</summary>
        public static int Installed(string gameDir)
        {
            try
            {
                var v = Path.Combine(gameDir, Folder, "version.txt");
                if (!File.Exists(v) || !File.Exists(Path.Combine(gameDir, Folder, Archive))) return 0;
                if (!Exes.All(e => File.Exists(Path.Combine(gameDir, "bin", CoopExeName(e))))) return 0;
                return int.TryParse(File.ReadAllText(v).Trim(), out var n) ? n : 0;
            }
            catch { return 0; }
        }

        /// <summary>The newest coop-engine-N release, or null (offline, rate limited, none published).</summary>
        public static Task<Release> CheckAsync() => Task.Run(() =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "CoopLauncher/" + Updater.CurrentText;
                    var json = wc.DownloadString($"https://api.github.com/repos/{Repo}/releases?per_page=20");
                    return Parse(json);
                }
            }
            catch { return null; }
        });

        /// <summary>Pick the newest complete coop-engine release from the GitHub releases JSON.</summary>
        public static Release Parse(string json)
        {
            Release best = null;
            foreach (Match t in Regex.Matches(json, "\"tag_name\"\\s*:\\s*\"coop-engine-(\\d+)\""))
            {
                var r = new Release { Version = int.Parse(t.Groups[1].Value), Tag = "coop-engine-" + t.Groups[1].Value };
                // asset links carry the tag: .../releases/download/coop-engine-N/<file>
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+/download/" + Regex.Escape(r.Tag) + "/([^\"/]+))\""))
                    r.Assets[m.Groups[2].Value] = m.Groups[1].Value;
                if (!Exes.All(r.Assets.ContainsKey) || !r.Assets.ContainsKey(Archive)) continue;
                if (best == null || r.Version > best.Version) best = r;
            }
            return best;
        }

        /// <summary>Download the release and put it in place; files are swapped only once all have arrived.</summary>
        public static void Install(string gameDir, Release r)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var tmp = Path.Combine(gameDir, Folder, "download");
            Directory.CreateDirectory(tmp);
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "CoopLauncher/" + Updater.CurrentText;
                    foreach (var name in Exes.Concat(new[] { Archive }))
                    {
                        var path = Path.Combine(tmp, name);
                        wc.DownloadFile(r.Assets[name], path);
                        Verify(path, name.EndsWith(".exe"));
                    }
                }
                foreach (var e in Exes)
                    File.Copy(Path.Combine(tmp, e), Path.Combine(gameDir, "bin", CoopExeName(e)), true);
                File.Copy(Path.Combine(tmp, Archive), Path.Combine(gameDir, Folder, Archive), true);
                File.WriteAllText(Path.Combine(gameDir, Folder, "version.txt"), r.Version.ToString());
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        static void Verify(string path, bool exe)
        {
            var len = new FileInfo(path).Length;
            var head = new byte[2];
            using (var f = File.OpenRead(path)) f.Read(head, 0, 2);
            bool ok = exe ? head[0] == 'M' && head[1] == 'Z' && len > 1024 * 1024 : len > 1024;
            if (!ok) throw new InvalidDataException(Path.GetFileName(path) + ": скачанный файл повреждён");
        }

        /// <summary>Remove everything the co-op engine added to the game.</summary>
        public static void Uninstall(string gameDir)
        {
            foreach (var e in Exes)
            {
                var p = Path.Combine(gameDir, "bin", CoopExeName(e));
                if (File.Exists(p)) File.Delete(p);
            }
            var dir = Path.Combine(gameDir, Folder);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var ltx = Path.Combine(gameDir, FsLtx);
            if (File.Exists(ltx)) File.Delete(ltx);
        }

        /// <summary>
        /// The exe to start and its extra arguments. Only DX11 is built, so the co-op engine always runs DX11.
        /// fsgame_coop.ltx is written fresh from the game's fsgame.ltx each time, plus the engine's gamedata.
        /// </summary>
        public static (string exe, string args) Prepare(string gameDir, bool avx)
        {
            var src = File.ReadAllLines(Path.Combine(gameDir, "fsgame.ltx")).ToList();
            var line = "$arch_dir_coop$ = false| false| $fs_root$| " + Folder + "\\";
            // after the stock archives, so its files override theirs the same way db\mods does
            int at = src.FindLastIndex(l => l.TrimStart().StartsWith("$arch_dir"));
            src.Insert(at < 0 ? 0 : at + 1, line);
            File.WriteAllLines(Path.Combine(gameDir, FsLtx), src);
            var exe = Path.Combine(gameDir, "bin", CoopExeName(avx ? "AnomalyDX11AVX.exe" : "AnomalyDX11.exe"));
            return (exe, "-fsltx " + FsLtx);
        }
    }
}
