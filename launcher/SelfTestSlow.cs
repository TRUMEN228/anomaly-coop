using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoopLauncher
{
    public static partial class SelfTest
    {
        // A slow link (a VPN relay on a weak uplink) must delay only the network, never the game's file exchange:
        // otherwise the game takes the launcher for closed after 3 s and drops the partner.
        static void SlowLink()
        {
            var root = Path.Combine(Path.GetTempPath(), "coop_slow_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string dirA = Path.Combine(root, "host"), dirB = Path.Combine(root, "guest");
            Directory.CreateDirectory(Path.Combine(dirA, "appdata", "savedgames"));
            Directory.CreateDirectory(Path.Combine(dirB, "appdata", "savedgames"));
            var save = new byte[3 * 1024 * 1024];
            new Random(3).NextBytes(save);
            File.WriteAllBytes(Path.Combine(dirA, "appdata", "savedgames", "coop_session.scop"), save);

            var host = new Bridge(dirA, "host", "HostBot");
            var guest = new Bridge(dirB, "client", "GuestBot");
            SteamLink lh = null, lg = null;
            // ~200 KB/s with 150 ms per message: the 3 MB save takes ~15 s to cross
            lh = new SteamLink(2, (peer, m) => { Thread.Sleep(150 + m.Length / 200); lg.Feed(m); return true; }, _ => { });
            lg = new SteamLink(1, (peer, m) => { Thread.Sleep(150); lh.Feed(m); return true; }, _ => { });
            host.RunLink(lh);
            guest.RunLink(lg);
            Wait(() => host.Connected && guest.Connected, 8000);
            Check("connected over the slow link", host.Connected && guest.Connected);

            // the game keeps writing states 10 times a second while the save is on its way
            var outA = Path.Combine(dirA, "appdata", "coop_out.txt");
            var inA = Path.Combine(dirA, "appdata", "coop_in.txt");
            int tick = 0;
            var gaps = new List<double>();
            var stop = DateTime.Now.AddSeconds(10);
            new Thread(() => host.SendSaveFiles("coop_session")) { IsBackground = true }.Start();
            string lastIn = SafeRead(inA);
            var lastChange = DateTime.Now;
            while (DateTime.Now < stop)
            {
                tick++;
                File.WriteAllText(outA, $"g|111|0|2.3\ns|p|zaton|{tick}.0|0|0\nend\n", Bridge.GameEnc);
                Thread.Sleep(100);
                var now = SafeRead(inA);
                if (now != lastIn) { gaps.Add((DateTime.Now - lastChange).TotalMilliseconds); lastIn = now; lastChange = DateTime.Now; }
            }
            gaps.Add((DateTime.Now - lastChange).TotalMilliseconds);
            var worst = gaps.Max();
            Check($"game file kept fresh while sending (worst gap {worst:0} ms)", worst < 1500);
            var inB = Path.Combine(dirB, "appdata", "coop_in.txt");
            Wait(() => SafeRead(inB).Contains("s|p|zaton|"), 20000);
            Check("partner still gets states (only the newest)", SafeRead(inB).Contains("s|p|zaton|"));
            Wait(() => File.Exists(Path.Combine(dirB, "appdata", "savedgames", "coop_session.scop")), 30000);
            var recv = Path.Combine(dirB, "appdata", "savedgames", "coop_session.scop");
            Check("save still delivered intact", File.Exists(recv) && File.ReadAllBytes(recv).SequenceEqual(save));
            Wait(() => host.RttMs > 0, 8000);
            Check($"round trip measured ({host.RttMs} ms)", host.RttMs >= 250);
            host.Dispose(); guest.Dispose();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
