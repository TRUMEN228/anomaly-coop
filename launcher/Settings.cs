using System;
using System.Collections.Generic;
using System.IO;

namespace CoopLauncher
{
    /// <summary>Launcher settings in coop_launcher.ini next to the exe (key=value).</summary>
    public sealed class Settings
    {
        public string GameDir, Nick, HostIp, Mode = "host", LastSave;
        public int Port = 27015;
        public bool Upnp = true;
        public bool ShareRewards = true, Revive = true, FriendlyFire = false, SharedMoney = false, Follow = true;
        public string Transport = "steam"; // "steam" | "ip"
        readonly Dictionary<string, string> keys = new Dictionary<string, string>
        {
            ["tp"] = "NUMPAD1", ["sync"] = "NUMPAD2", ["load"] = "NUMPAD3", ["status"] = "NUMPAD0",
        };

        public string Key(string action) => keys.TryGetValue(action, out var k) ? k : "NUMPAD0";
        public void SetKey(string action, string key) => keys[action] = key;

        /// <summary>Tell the mod which keys to use (appdata\coop_keys.txt, read when a game loads).</summary>
        public void WriteGameKeys(string gameDir)
        {
            var lines = new List<string>();
            foreach (var kv in keys) lines.Add(kv.Key + "=DIK_" + kv.Value);
            lines.Add("rewards=" + (ShareRewards ? 1 : 0));
            lines.Add("revive=" + (Revive ? 1 : 0));
            lines.Add("ff=" + (FriendlyFire ? 1 : 0));
            lines.Add("money=" + (SharedMoney ? 1 : 0));
            lines.Add("follow=" + (Follow ? 1 : 0));
            var dir = Path.Combine(gameDir, "appdata");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "coop_keys.txt"), lines);
        }

        static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coop_launcher.ini");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    var k = line.Substring(0, i).Trim();
                    var v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "game_dir": s.GameDir = v; break;
                        case "nick": s.Nick = v; break;
                        case "host_ip": s.HostIp = v; break;
                        case "mode": s.Mode = v; break;
                        case "last_save": s.LastSave = v; break;
                        case "port": int.TryParse(v, out s.Port); break;
                        case "upnp": s.Upnp = v != "0"; break;
                        case "share_rewards": s.ShareRewards = v != "0"; break;
                        case "revive": s.Revive = v != "0"; break;
                        case "friendly_fire": s.FriendlyFire = v == "1"; break;
                        case "shared_money": s.SharedMoney = v == "1"; break;
                        case "follow": s.Follow = v != "0"; break;
                        case "transport": s.Transport = v == "ip" ? "ip" : "steam"; break;
                        default:
                            if (k.StartsWith("key_") && v.Length > 0) s.keys[k.Substring(4)] = v;
                            break;
                    }
                }
                if (s.Port < 1024 || s.Port > 65535) s.Port = 27015;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var lines = new List<string> {
                    "game_dir=" + GameDir, "nick=" + Nick, "host_ip=" + HostIp,
                    "mode=" + Mode, "last_save=" + LastSave, "port=" + Port, "upnp=" + (Upnp ? 1 : 0), "share_rewards=" + (ShareRewards ? 1 : 0),
                    "revive=" + (Revive ? 1 : 0), "friendly_fire=" + (FriendlyFire ? 1 : 0), "shared_money=" + (SharedMoney ? 1 : 0),
                    "follow=" + (Follow ? 1 : 0), "transport=" + Transport };
                foreach (var kv in keys) lines.Add("key_" + kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
