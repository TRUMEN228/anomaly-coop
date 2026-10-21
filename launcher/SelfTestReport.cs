using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoopLauncher
{
    public static partial class SelfTest
    {
        // ErrorReport: personal data is scrubbed from the issue and the archive, the link stays short enough
        static void Report()
        {
            var dir = Path.Combine(Path.GetTempPath(), "coop_report_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(Path.Combine(dir, "appdata", "logs"));
            var user = Environment.UserName;
            var noise = string.Join("\n", Enumerable.Range(0, 40000).Select(i => "* loading texture " + i));
            File.WriteAllText(Path.Combine(dir, "appdata", "logs", "xray_" + user + ".log"),
                noise + "\nLoad C:\\Users\\" + user + "\\Games\\Anomaly\\appdata\\savedgames\\x.scop\nconnect 192.168.1.5 steam 76561198000000001 Tester\n" +
                "FATAL ERROR\n[error]Expression    : <no expression>\n[error]Function      : CScriptEngine::lua_error\nstack trace:\n", Bridge.GameEnc);
            File.WriteAllText(Path.Combine(dir, "appdata", "coop_log.txt"),
                "[1] quests update failed: attempt to index nil\n[2] Tester joined from 10.0.0.7\n", Bridge.GameEnc);
            var s = new Settings { GameDir = dir, Nick = "Tester" };
            var url = ErrorReport.IssueUrl(s, dir, "Вылетело при входе на Затон", "logs.zip", out var body);
            Check("crash found", ErrorReport.CrashExcerpt(dir, fatalOnly: true) != null && body.Contains("FATAL ERROR"));
            Check("mod errors listed", body.Contains("attempt to index nil"));
            Check("windows user removed", user.Length < 3 || body.IndexOf(user, StringComparison.OrdinalIgnoreCase) < 0);
            Check("nick, IPs and steam id removed", !body.Contains("Tester") && !body.Contains("192.168.1.5") && !body.Contains("10.0.0.7") && !body.Contains("76561198000000001"));
            Check("link short enough for GitHub (" + url.Length + ")", url.Length <= 7600 && url.StartsWith("https://github.com/" + ErrorReport.Repo + "/issues/new?"));
            var zip = LogCollector.Collect(dir, s);
            string zipped;
            using (var z = System.IO.Compression.ZipFile.OpenRead(zip))
            using (var r = new StreamReader(z.Entries.First(e => e.FullName.StartsWith("logs/")).Open(), Bridge.GameEnc)) zipped = r.ReadToEnd();
            Check("archive scrubbed too", !zipped.Contains("192.168.1.5") && !zipped.Contains("Tester") && zipped.Contains("FATAL ERROR"));
            File.Delete(zip);
            var d = new ReportDialog(s, dir, true);
            d.Show();
            for (int i = 0; i < 20; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(25); }
            using (var bmp = new System.Drawing.Bitmap(d.Width, d.Height))
            {
                d.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, d.Width, d.Height));
                bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_report.png"));
            }
            d.Close();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report_body.md"), body);
            Directory.Delete(dir, true);
        }
    }
}
