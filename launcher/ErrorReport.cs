using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>
    /// Bug reports without secrets in the exe: the launcher builds the report (versions, the engine's crash,
    /// tails of the mod and launcher logs), scrubs personal data, shows it, and on the player's consent opens a
    /// pre-filled GitHub issue in the browser. The player submits it from their own account and can drop the
    /// log archive in.
    /// </summary>
    public static class ErrorReport
    {
        public const string Repo = "TRUMEN228/anomaly-coop";
        const int MaxUrl = 7600;                 // GitHub refuses longer "new issue" links

        static string ReadShared(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Bridge.GameEnc)) return r.ReadToEnd();
            }
            catch { return ""; }
        }

        static string[] Lines(string text) => text.Replace("\r", "").Split('\n');

        static string NewestEngineLog(string gameDir)
        {
            var dir = Path.Combine(gameDir ?? "", "appdata", "logs");
            if (!Directory.Exists(dir)) return "";
            var f = new DirectoryInfo(dir).GetFiles("*.log").OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
            return f == null ? "" : ReadShared(f.FullName);
        }

        static readonly string[] FatalMarks = { "FATAL ERROR", "stack trace", "[error]Expression" };
        static readonly string[] ErrorMarks = { "FATAL ERROR", "stack trace", "[error]Expression", "SCRIPT RUNTIME ERROR", "! [LUA]" };

        /// <summary>The engine's own words about a crash (or, unless fatalOnly, a script error), null when there is none.</summary>
        public static string CrashExcerpt(string gameDir, bool fatalOnly = false)
        {
            var lines = Lines(NewestEngineLog(gameDir));
            var marks = fatalOnly ? FatalMarks : ErrorMarks;
            int at = -1;
            for (int i = lines.Length - 1; i >= 0 && at < 0; i--)
                if (marks.Any(m => lines[i].IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0)) at = i;
            if (at < 0) return null;
            int from = Math.Max(0, at - 6);
            return string.Join("\n", lines.Skip(from).Take(36)).Trim();
        }

        static string Tail(string text, int n) => string.Join("\n", Lines(text).Where(l => l.Trim().Length > 0).Reverse().Take(n).Reverse());

        /// <summary>Removes what identifies the player: Windows user name, nick, IP addresses, Steam ids.</summary>
        public static string Scrub(string s, Settings settings)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var user = Environment.UserName;
            if (!string.IsNullOrEmpty(user))
                s = Regex.Replace(s, @"(\\|/)(Users|Пользователи)(\\|/)" + Regex.Escape(user), "$1$2$3<user>", RegexOptions.IgnoreCase);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.Length > 3) s = Regex.Replace(s, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
            if (!string.IsNullOrWhiteSpace(settings?.Nick) && settings.Nick.Trim().Length >= 3)
                s = Regex.Replace(s, Regex.Escape(settings.Nick.Trim()), "<nick>", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(user) && user.Length >= 3) s = Regex.Replace(s, @"\b" + Regex.Escape(user) + @"\b", "<user>", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b7656119\d{10}\b", "<steamid>");
            s = Regex.Replace(s, @"\b(?!127\.0\.0\.1\b)(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b", "x.x.x.x");
            s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.]+", "<email>");
            return s;
        }

        // Environment.OSVersion reports 6.2 to apps without an OS manifest entry: ask the registry
        static string WindowsName()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var build = k?.GetValue("CurrentBuild") as string;
                    var name = (k?.GetValue("ProductName") as string) ?? "Windows";
                    // Windows 11 still calls itself "Windows 10" there; builds 22000+ are 11
                    if (int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    return $"{name} (build {build})";
                }
            }
            catch { return "Windows " + Environment.OSVersion.Version; }
        }

        static string Fence(string s) => "```\n" + s.Replace("```", "'''") + "\n```";

        /// <summary>Markdown body of the issue; tails shrink until the link fits.</summary>
        public static string Body(Settings settings, string gameDir, string what, string zipName, int tail = 25)
        {
            var appdata = Path.Combine(gameDir ?? "", "appdata");
            var crash = CrashExcerpt(gameDir);
            var mod = ReadShared(Path.Combine(appdata, "coop_log.txt"));
            var launcher = ReadShared(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coop_launcher.log"));
            var modErrors = string.Join("\n", Lines(mod).Where(l => l.Contains("failed") || l.Contains("error")).Reverse().Take(8).Reverse());
            var sb = new StringBuilder();
            sb.AppendLine("### Что случилось");
            sb.AppendLine(string.IsNullOrWhiteSpace(what) ? "_(не указано)_" : what.Trim());
            sb.AppendLine();
            sb.AppendLine("### Окружение");
            sb.AppendLine($"- лаунчер {Updater.CurrentText}, режим {(settings.Mode == "client" ? "гость" : "хост")}, связь {(settings.Transport == "ip" ? "прямой IP" : "Steam")}");
            sb.AppendLine($"- {WindowsName()}, {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
            sb.AppendLine();
            if (crash != null) { sb.AppendLine("### Ошибка игры"); sb.AppendLine(Fence(crash)); sb.AppendLine(); }
            if (modErrors.Length > 0) { sb.AppendLine("### Ошибки мода"); sb.AppendLine(Fence(modErrors)); sb.AppendLine(); }
            if (tail > 0)
            {
                sb.AppendLine("<details><summary>Конец лога мода</summary>\n"); sb.AppendLine(Fence(Tail(mod, tail))); sb.AppendLine("</details>\n");
                sb.AppendLine("<details><summary>Конец лога лаунчера</summary>\n"); sb.AppendLine(Fence(Tail(launcher, tail))); sb.AppendLine("</details>\n");
            }
            if (zipName != null) sb.AppendLine($"Архив логов `{zipName}` лежит на рабочем столе — перетащите его в это окно.");
            return Scrub(sb.ToString(), settings);
        }

        public static string Title(string gameDir, string what)
        {
            var w = (what ?? "").Trim().Split('\n')[0].Trim();
            if (w.Length > 70) w = w.Substring(0, 70) + "…";
            if (w.Length > 0) return w;
            return CrashExcerpt(gameDir, fatalOnly: true) != null ? "Вылет игры" : "Ошибка в кооперативе";
        }

        /// <summary>The "new issue" link with title and body filled in, short enough for GitHub.</summary>
        public static string IssueUrl(Settings settings, string gameDir, string what, string zipName, out string body)
        {
            string title = Scrub(Title(gameDir, what), settings), url = null;
            body = "";
            foreach (var tail in new[] { 25, 15, 8, 0 })
            {
                body = Body(settings, gameDir, what, zipName, tail);
                url = $"https://github.com/{Repo}/issues/new?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
                if (url.Length <= MaxUrl) return url;
            }
            // still too long (a huge crash dump): cut the body itself
            while (url.Length > MaxUrl && body.Length > 200)
            {
                body = body.Substring(0, body.Length * 3 / 4) + "\n\n_(обрезано, полный текст — в архиве логов)_";
                url = $"https://github.com/{Repo}/issues/new?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
            }
            return url;
        }
    }

    /// <summary>"Сообщить об ошибке": what will be published, the player's consent, then the browser.</summary>
    public sealed class ReportDialog : ZoneDialog
    {
        readonly TextBox what = new TextBox { Multiline = true, AcceptsReturn = true };
        readonly TextBox preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true };
        readonly ZoneCheck consent = new ZoneCheck("Я понимаю, что отчёт будет опубликован открыто на GitHub");
        readonly ZoneButton send = new ZoneButton("Открыть на GitHub", true);
        readonly Settings settings;
        readonly string gameDir;

        public ReportDialog(Settings s, string gameDir, bool crashed) : base("Сообщить об ошибке", 760, 640)
        {
            settings = s;
            this.gameDir = gameDir;
            var intro = Caption(crashed
                ? "Игра завершилась с ошибкой. Опишите, что делали перед вылетом, — это сильно помогает."
                : "Опишите, что пошло не так и что вы делали перед этим.", 24, 54, 712);
            intro.Height = 22;
            Add(new FieldFrame(what), 24, 82, 712, 70);
            Caption("Будет отправлено (имя пользователя Windows, ник, IP и Steam ID уже убраны):", 24, 164, 712);
            Add(new FieldFrame(preview), 24, 190, 712, 300);
            preview.Font = Theme.Mono(8.5f); // after FieldFrame, which sets its own font
            Add(consent, 24, 502, 712, 26);
            Caption("Понадобится аккаунт GitHub: откроется готовый отчёт, останется нажать «Submit».", 24, 532, 712).ForeColor = Theme.TextDim;
            var zipOnly = Add(new ZoneButton("Только архив логов"), 24, 580, 200, 40);
            Add(send, 412, 580, 200, 40);
            var cancel = Add(new ZoneButton("Отмена"), 620, 580, 116, 40);
            send.Enabled = false;
            consent.Click += (o, e) => send.Enabled = consent.Checked;
            what.TextChanged += (o, e) => Refresh();
            send.Click += (o, e) => Send();
            zipOnly.Click += (o, e) => { var z = Zip(); if (z != null) Process.Start("explorer.exe", $"/select,\"{z}\""); };
            cancel.Click += (o, e) => Close();
            Refresh();
        }

        public new void Refresh()
        {
            ErrorReport.IssueUrl(settings, gameDir, what.Text, "anomaly-coop-logs-….zip", out var body);
            preview.Text = body.Replace("\n", "\r\n");
        }

        string Zip()
        {
            try { return LogCollector.Collect(gameDir, settings); }
            catch (Exception e) { ZoneMessage.Show(this, "Не удалось собрать логи: " + e.Message, "Логи"); return null; }
        }

        void Send()
        {
            var zip = Zip();
            var url = ErrorReport.IssueUrl(settings, gameDir, what.Text, zip == null ? null : Path.GetFileName(zip), out _);
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { ZoneMessage.Show(this, "Не удалось открыть браузер: " + e.Message, "GitHub"); return; }
            if (zip != null) Process.Start("explorer.exe", $"/select,\"{zip}\"");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
