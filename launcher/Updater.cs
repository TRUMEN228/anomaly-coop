using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CoopLauncher
{
    /// <summary>Checks GitHub Releases for a newer launcher and replaces this exe.</summary>
    public static class Updater
    {
        const string Repo = "TRUMEN228/anomaly-coop";
        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        public static string CurrentText => Current.Build > 0 ? $"{Current.Major}.{Current.Minor}.{Current.Build}" : $"{Current.Major}.{Current.Minor}";

        public sealed class Release { public Version Version; public string Tag, ExeUrl; }

        public static Task<Release> CheckAsync() => Task.Run(() =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "CoopLauncher/" + CurrentText;
                    var json = wc.DownloadString($"https://api.github.com/repos/{Repo}/releases/latest");
                    var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"").Groups[1].Value;
                    var url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*CoopLauncher\\.exe)\"").Groups[1].Value;
                    if (tag == "" || url == "" || !Version.TryParse(tag.Contains(".") ? tag : tag + ".0", out var v)) return null;
                    var cur = new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
                    var rel = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
                    return rel > cur ? new Release { Version = v, Tag = "v" + tag, ExeUrl = url } : null;
                }
            }
            catch { return null; } // offline or rate limited: just skip
        });

        /// <summary>Download the new exe, then swap it in after this process exits and restart.</summary>
        public static void Apply(Release r)
        {
            var exe = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(exe);
            var fresh = Path.Combine(dir, "CoopLauncher.new.exe");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "CoopLauncher/" + CurrentText;
                wc.DownloadFile(r.ExeUrl, fresh);
            }
            var head = new byte[2];
            using (var f = File.OpenRead(fresh)) f.Read(head, 0, 2);
            if (head[0] != 'M' || head[1] != 'Z' || new FileInfo(fresh).Length < 16 * 1024)
            {
                File.Delete(fresh);
                throw new InvalidDataException("скачанный файл повреждён");
            }
            var script = Path.Combine(Path.GetTempPath(), "coop_launcher_update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                $"set PID={Process.GetCurrentProcess().Id}\r\n" +
                ":wait\r\n" +
                "tasklist /FI \"PID eq %PID%\" | find \"%PID%\" >nul && (timeout /t 1 >nul & goto wait)\r\n" +
                $"move /y \"{fresh}\" \"{exe}\" >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
