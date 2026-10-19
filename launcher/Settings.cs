using System;
using System.IO;

namespace CoopLauncher
{
    /// <summary>Launcher settings in coop_launcher.ini next to the exe (key=value).</summary>
    public sealed class Settings
    {
        public string GameDir, Nick, HostIp, Mode = "host", LastSave;
        public int Port = 27015;

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
                File.WriteAllLines(FilePath, new[] {
                    "game_dir=" + GameDir, "nick=" + Nick, "host_ip=" + HostIp,
                    "mode=" + Mode, "last_save=" + LastSave, "port=" + Port });
            }
            catch { }
        }
    }
}
