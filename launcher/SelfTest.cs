using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CoopLauncher
{
    /// <summary>
    /// Headless checks, written to selftest.txt next to the exe (exit code 0 = pass):
    ///   --selftest-loopback  two bridges in one process over a fake Steam channel: hello, compressed state, 2 MB save in 60 KB pieces
    ///   --selftest-steam     real Steam API: init, friends-only lobby, rich presence, leave
    /// </summary>
    public static class SelfTest
    {
        static readonly StringBuilder report = new StringBuilder();
        static bool ok = true;
        static void Check(string what, bool pass) { Note((pass ? "PASS " : "FAIL ") + what); ok &= pass; }
        static void Note(string line)
        {
            report.AppendLine(line);
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt"), report.ToString()); } catch { }
        }

        public static int Run(string mode)
        {
            try
            {
                if (mode == "--selftest-loopback") Loopback();
                else if (mode == "--selftest-steam") Steam();
                else if (mode == "--selftest-dialogs") Dialogs();
                else if (mode == "--selftest-rebind") Rebind();
            }
            catch (Exception e) { Check("exception: " + e, false); }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt"), report.ToString());
            return ok ? 0 : 1;
        }

        static void Loopback()
        {
            var root = Path.Combine(Path.GetTempPath(), "coop_selftest_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string dirA = Path.Combine(root, "host"), dirB = Path.Combine(root, "guest");
            Directory.CreateDirectory(Path.Combine(dirA, "appdata", "savedgames"));
            Directory.CreateDirectory(Path.Combine(dirB, "appdata", "savedgames"));
            var rnd = new Random(7);
            var save = new byte[2 * 1024 * 1024 + 12345];
            rnd.NextBytes(save);
            File.WriteAllBytes(Path.Combine(dirA, "appdata", "savedgames", "coop_session.scop"), save);
            File.WriteAllBytes(Path.Combine(dirA, "appdata", "savedgames", "coop_session.scoc"), new byte[300000]);

            var host = new Bridge(dirA, "host", "HostBot");
            var guest = new Bridge(dirB, "client", "GuestBot");
            SteamLink lh = null, lg = null;
            lh = new SteamLink(2, (peer, m) => { lg.Feed(m); return true; }, _ => { });
            lg = new SteamLink(1, (peer, m) => { lh.Feed(m); return true; }, _ => { });
            var ready = new ManualResetEventSlim();
            guest.SessionReady += () => ready.Set();
            host.RunLink(lh);
            guest.RunLink(lg);

            Wait(() => host.Connected && guest.Connected, 5000);
            Check("hello both ways over the Steam-style link", host.Connected && guest.Connected && guest.PeerName == "HostBot");

            // a big state line (> 512 bytes) goes through the deflate path
            var npcs = string.Join("|", Enumerable.Range(0, 60).Select(i => $"{1000 + i}:sim_default_stalker_1:{i}.0:-7.3:{i * 2}.5:0.95"));
            File.WriteAllText(Path.Combine(dirA, "appdata", "coop_out.txt"), $"g|111|0|1.3\ns|n|12345|{npcs}\nend\n", Bridge.GameEnc);
            var inB = Path.Combine(dirB, "appdata", "coop_in.txt");
            Wait(() => File.Exists(inB) && SafeRead(inB).Contains("s|n|12345|1000:"), 5000);
            var got = SafeRead(inB);
            Check("compressed NPC state reached the guest game file", got.Contains(npcs));

            host.SendSaveFiles("coop_session");
            ready.Wait(15000);
            var recv = Path.Combine(dirB, "appdata", "savedgames", "coop_session.scop");
            Check("2 MB save delivered in pieces and identical", File.Exists(recv) && File.ReadAllBytes(recv).SequenceEqual(save));
            Check("session_ready raised on the guest", ready.IsSet);

            guest.SendChat("привет");
            Wait(() => SafeRead(Path.Combine(dirA, "appdata", "coop_in.txt")).Contains("chat|привет|GuestBot"), 5000);
            Check("chat (cp1251) guest -> host", SafeRead(Path.Combine(dirA, "appdata", "coop_in.txt")).Contains("chat|привет|GuestBot"));
            report.AppendLine($"bytes host sent {host.BytesSent}, guest sent {guest.BytesSent}");

            lg.Close();
            Wait(() => !host.Connected, 25000);
            Check("host notices the guest is gone (silence timeout)", !host.Connected);
            host.Dispose(); guest.Dispose();
            try { Directory.Delete(root, true); } catch { }
        }

        static void Steam()
        {
            Note("starting Steam init...");
            SteamNet.Log += m => Note("steam log: " + m);
            bool init = SteamNet.Init(out var err);
            Check("Steam API init" + (init ? "" : ": " + err), init);
            if (!init) return;
            Note("steam user: " + SteamNet.MyName);
            ulong lobby = 0;
            var created = new ManualResetEventSlim();
            SteamNet.LobbyCreated += l => { lobby = l; created.Set(); };
            Note("creating lobby...");
            SteamNet.HostLobby();
            created.Wait(15000);
            Check("friends-only lobby created", lobby != 0);
            SteamNet.SetPresence("Anomaly Co-op: самотест");
            var friends = SteamNet.FriendLobbies();
            Note("friends hosting right now: " + friends.Count);
            Thread.Sleep(1000);
            Note("leaving...");
            SteamNet.Leave();
            Check("left the lobby and cleared presence", SteamNet.CurrentLobby == 0);
        }

        /// <summary>Render every window of the launcher to PNG files next to the exe (visual check).</summary>
        static void Dialogs()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            void Snap(System.Windows.Forms.Form f, string name)
            {
                f.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(40, 40);
                f.Show();
                for (int i = 0; i < 20; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(25); }
                using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(Path.Combine(dir, "ui_" + name + ".png"));
                }
                f.Close();
                Check("rendered " + name, File.Exists(Path.Combine(dir, "ui_" + name + ".png")));
            }
            var settings = Settings.Load();
            Snap(new MainForm(), "main");
            Snap(new OptionsDialog(settings), "options");
            Snap(new ControlsDialog(settings), "controls");
            foreach (var c in new[] { "Движение", "Оружие", "Снаряжение", "Прочее" })
            {
                var d = new ControlsDialog(settings);
                d.Show(c);
                Snap(d, "controls_" + Array.IndexOf(new[] { "Движение", "Оружие", "Снаряжение", "Прочее" }, c));
            }
            Snap(new FolderDialog(Path.GetDirectoryName((settings.GameDir ?? "C:\\").TrimEnd('\\')) ?? ""), "folder");
            var msg = typeof(ZoneMessage).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
            Snap((System.Windows.Forms.Form)msg.Invoke(new object[] { "Удалить мод", "Удалить файлы мода из папки игры? Сейвы и персонаж гостя останутся.", true }), "message");
        }

        // ControlsDialog: rebinding a game action writes user.ltx, a co-op action the launcher keys
        static void Rebind()
        {
            var dir = Path.Combine(Path.GetTempPath(), "coop_rebind_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(Path.Combine(dir, "appdata"));
            var ltx = Path.Combine(dir, "appdata", "user.ltx");
            File.WriteAllLines(ltx, new[] { "bind jump kSPACE", "bind use kF", "g_always_run on" });
            var s = new Settings { GameDir = dir };
            var d = new ControlsDialog(s);
            var t = typeof(ControlsDialog);
            var notes = (System.Collections.IList)t.GetField("notes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(d);
            object Find(string field, string id)
            {
                foreach (var n in notes) if ((string)n.GetType().GetField(field).GetValue(n) == id) return n;
                return null;
            }
            void Assign(object note, string code)
            {
                t.GetField("editing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(d, note);
                t.GetMethod("Assign", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(d, new object[] { code });
            }
            Assign(Find("GameId", "jump"), "J");
            var lines = File.ReadAllLines(ltx);
            Check("game action rebound in user.ltx", lines.Contains("bind jump kJ") && lines.Contains("bind use kF") && lines.Contains("g_always_run on"));
            Assign(Find("GameId", "use"), "MOUSE3");
            Check("mouse button written as mouse3", File.ReadAllLines(ltx).Contains("bind use mouse3"));
            Assign(Find("CoopId", "give"), "NUMPAD7");
            Check("co-op key saved", s.Key("give") == "NUMPAD7");
            Check("co-op key written for the game", File.ReadAllText(Path.Combine(dir, "appdata", "coop_keys.txt")).Contains("give=DIK_NUMPAD7"));
            Check("dialog reloaded the new binding", Find("GameId", "jump") != null && Find("CoopId", "give") != null);
            Directory.Delete(dir, true);
        }

        static void Wait(Func<bool> cond, int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (!cond() && DateTime.Now < until) Thread.Sleep(50);
        }

        static string SafeRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Bridge.GameEnc) : ""; } catch { return ""; }
        }
    }
}
