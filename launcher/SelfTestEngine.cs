using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoopLauncher
{
    public static partial class SelfTest
    {
        // The co-op engine goes in next to the stock files and comes out without a trace; partners on
        // different engines are told so when they connect.
        static void EngineSetup()
        {
            var root = Path.Combine(Path.GetTempPath(), "coop_engine_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var game = Path.Combine(root, "game");
            var src = Path.Combine(root, "release");
            Directory.CreateDirectory(Path.Combine(game, "bin"));
            Directory.CreateDirectory(src);
            File.WriteAllLines(Path.Combine(game, "fsgame.ltx"), new[] {
                "$fs_root$ = false| false| .\\",
                "$arch_dir$ = false| false| $fs_root$ | db\\",
                "$arch_dir_resource$ = false| false| $arch_dir$ | mods\\",
                "$game_data$ = true| true| $fs_root$ | gamedata\\" });
            var stock = new byte[2 * 1024 * 1024]; stock[0] = (byte)'M'; stock[1] = (byte)'Z'; stock[2] = 1;
            File.WriteAllBytes(Path.Combine(game, "bin", "AnomalyDX11AVX.exe"), stock);

            var coop = new byte[2 * 1024 * 1024]; coop[0] = (byte)'M'; coop[1] = (byte)'Z'; coop[2] = 2;
            File.WriteAllBytes(Path.Combine(src, "AnomalyDX11AVX.exe"), coop);
            File.WriteAllBytes(Path.Combine(src, "AnomalyDX11.exe"), coop);
            File.WriteAllBytes(Path.Combine(src, "coop_engine_gamedata.db0"), new byte[4096]);

            // the release is found in the API listing by its tag and asset links
            string Url(string tag, string f) => $"https://github.com/TRUMEN228/xray-monolith/releases/download/{tag}/{f}";
            string Rel(string tag, params string[] files) => "{\"tag_name\":\"" + tag + "\",\"assets\":[" +
                string.Join(",", files.Select(f => "{\"browser_download_url\":\"" + Url(tag, f) + "\"}")) + "]}";
            var json = "[" + Rel("coop-engine-3", "AnomalyDX11AVX.exe") + "," +
                Rel("coop-engine-2", "AnomalyDX11AVX.exe", "AnomalyDX11.exe", "coop_engine_gamedata.db0") + "," +
                Rel("2026.10.24", "x.zip") + "]";
            var r = Engine.Parse(json);
            Check("newest complete release picked (r3 lacks files, r2 has all)", r != null && r.Version == 2 && r.Assets.Count == 3);

            // install from local copies of the assets
            foreach (var k in r.Assets.Keys.ToList()) r.Assets[k] = new Uri(Path.Combine(src, k)).AbsoluteUri;
            Check("not installed at first", Engine.Installed(game) == 0);
            Engine.Install(game, r);
            Check("installed r2", Engine.Installed(game) == 2);
            Check("stock exe untouched", File.ReadAllBytes(Path.Combine(game, "bin", "AnomalyDX11AVX.exe"))[2] == 1);
            Check("co-op exe next to it", File.ReadAllBytes(Path.Combine(game, "bin", "AnomalyDX11AVX_coop.exe"))[2] == 2);
            Check("download folder cleaned up", !Directory.Exists(Path.Combine(game, "coop_engine", "download")));

            var (exe, args) = Engine.Prepare(game, true);
            var ltx = File.ReadAllLines(Path.Combine(game, "fsgame_coop.ltx"));
            int coopLine = Array.FindIndex(ltx, l => l.StartsWith("$arch_dir_coop$"));
            Check("starts the co-op exe with its own fsgame", exe.EndsWith("AnomalyDX11AVX_coop.exe") && args == "-fsltx fsgame_coop.ltx");
            Check("fsgame_coop adds the engine's gamedata after the stock archives",
                coopLine == 3 && ltx[coopLine].Contains("coop_engine\\") && ltx.Length == 5);
            Check("stock fsgame.ltx untouched", File.ReadAllLines(Path.Combine(game, "fsgame.ltx")).Length == 4);

            // a broken download leaves the installed engine as it was
            File.WriteAllBytes(Path.Combine(src, "AnomalyDX11.exe"), new byte[100]);
            r.Version = 3;
            bool threw = false;
            try { Engine.Install(game, r); } catch (InvalidDataException) { threw = true; }
            Check("broken download refused, r2 still installed", threw && Engine.Installed(game) == 2);

            Engine.Uninstall(game);
            Check("uninstall removes everything it added", Engine.Installed(game) == 0 &&
                !File.Exists(Path.Combine(game, "bin", "AnomalyDX11AVX_coop.exe")) && !Directory.Exists(Path.Combine(game, "coop_engine")) &&
                !File.Exists(Path.Combine(game, "fsgame_coop.ltx")) && File.Exists(Path.Combine(game, "bin", "AnomalyDX11AVX.exe")));

            // partners on different engines
            string dirA = Path.Combine(root, "host"), dirB = Path.Combine(root, "guest");
            Directory.CreateDirectory(Path.Combine(dirA, "appdata", "savedgames"));
            Directory.CreateDirectory(Path.Combine(dirB, "appdata", "savedgames"));
            var host = new Bridge(dirA, "host", "HostBot") { EngineVersion = 1 };
            var guest = new Bridge(dirB, "client", "GuestBot") { EngineVersion = 0 };
            var hostLog = new List<string>(); var guestLog = new List<string>();
            host.Log += s => { lock (hostLog) hostLog.Add(s); };
            guest.Log += s => { lock (guestLog) guestLog.Add(s); };
            SteamLink lh = null, lg = null;
            lh = new SteamLink(2, (peer, m) => { lg.Feed(m); return true; }, _ => { });
            lg = new SteamLink(1, (peer, m) => { lh.Feed(m); return true; }, _ => { });
            host.RunLink(lh); guest.RunLink(lg);
            for (int i = 0; i < 50 && !(host.Connected && guest.Connected); i++) Thread.Sleep(100);
            Thread.Sleep(300);
            bool H(string w) { lock (hostLog) return hostLog.Any(l => l.Contains(w)); }
            bool G(string w) { lock (guestLog) return guestLog.Any(l => l.Contains(w)); }
            Check("host told the guest runs the stock engine", H("обычный движок, у вас улучшенный"));
            Check("guest told to turn the engine on", G("улучшенный движок, у вас обычный"));
            host.Dispose(); guest.Dispose();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
