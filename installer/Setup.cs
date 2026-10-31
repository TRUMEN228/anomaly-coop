using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoopLauncher
{
    static class Program
    {
        // AnomalyCoopSetup.exe [--install|--restore <game folder> [--shortcuts]]: the same without the window
        // (for scripts and tests); what it would show goes to the console and the exit code is 0 on success
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && (args[0] == "--install" || args[0] == "--restore"))
            {
                using (var f = new SetupForm(args[1], args.Contains("--shortcuts")))
                    return f.RunQuiet(args[0] == "--install") ? 0 : 1;
            }
            Application.Run(new SetupForm());
            return 0;
        }
    }

    /// <summary>
    /// Anomaly Co-op setup. The archive (the co-op engine as the DX11 exes, steam_api64.dll, the engine's gamedata
    /// and the mod) goes into the game folder; every file it replaces is copied first to coop_backup\, once - a later
    /// setup keeps the vanilla originals there. "Back to vanilla" restores them and removes what the archive added.
    /// </summary>
    public sealed class SetupForm : ZoneDialog
    {
        const string BackupDir = "coop_backup";
        const string Installed = "installed.txt";   // every path the archive put in, relative to the game folder
        const string BackedUp = "backed_up.txt";    // those of them that were there before (their originals are in coop_backup)
        const string ShortcutName = "Anomaly Co-op.lnk";

        readonly TextBox gameDir = new TextBox();
        readonly ZoneCheck desktop = new ZoneCheck("Ярлык на рабочем столе") { Checked = true };
        readonly ZoneCheck startMenu = new ZoneCheck("Ярлык в меню «Пуск»") { Checked = true };
        readonly Label state = new Label();
        readonly PdaLog log = new PdaLog { Title = "УСТАНОВКА" };
        readonly ZoneButton install = new ZoneButton("Установить", true);
        readonly ZoneButton restore = new ZoneButton("Вернуть ванильную игру");
        readonly string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        readonly bool quiet;

        public SetupForm(string game, bool shortcuts) : this()
        {
            quiet = true;
            gameDir.Text = game;
            desktop.Checked = startMenu.Checked = shortcuts;
        }

        public bool RunQuiet(bool installing)
        {
            if (!IsGame(Game)) { Console.WriteLine("not an Anomaly folder: " + Game); return false; }
            failed = false;
            Run(installing ? (Action)Install : Restore);
            return !failed;
        }

        bool failed;

        void Say(string text, Color color)
        {
            log.Add(text, color);
            if (quiet) Console.WriteLine(text);
            if (color == Theme.Danger) failed = true;
        }

        public SetupForm() : base("Anomaly Co-op " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " — установка", 640, 560)
        {
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Caption("Папка Anomaly 1.5.3 (где AnomalyLauncher.exe)", 24, 58, 592);
            Add(new FieldFrame(gameDir), 24, 82, 488, 30);
            var browse = Add(new ZoneButton("Обзор..."), 520, 82, 96, 30);
            state.BackColor = Color.Transparent;
            state.Font = Theme.Label(9);
            Add(state, 24, 118, 592, 40);
            Add(desktop, 24, 160, 280, 24);
            Add(startMenu, 320, 160, 280, 24);
            Add(log, 24, 196, 592, 290);
            Add(install, 24, 500, 200, 40);
            Add(restore, 232, 500, 240, 40);
            var quit = Add(new ZoneButton("Выход"), 480, 500, 136, 40);

            browse.Click += (s, e) =>
            {
                using (var d = new FolderDialog(Directory.Exists(gameDir.Text) ? gameDir.Text : ""))
                    if (d.ShowDialog(this) == DialogResult.OK) gameDir.Text = d.SelectedPath;
            };
            gameDir.TextChanged += (s, e) => ShowState();
            install.Click += (s, e) => Run(Install);
            restore.Click += (s, e) => Run(Restore);
            quit.Click += (s, e) => Close();

            gameDir.Text = FindGame() ?? "";
            ShowState();
            Say("Архив внутри установщика: игра ставится без интернета. Кооп работает на DX11.", Theme.TextDim);
        }

        // ------------------------------------------------------------------ the game folder
        static bool IsGame(string dir) => FolderDialog.IsGame(dir);

        static string FindGame()
        {
            // the setup started from inside the game folder, or next to it; then the usual places
            var here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            for (var d = new DirectoryInfo(here); d != null; d = d.Parent)
                if (IsGame(d.FullName)) return d.FullName;
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                foreach (var rel in new[] { "Anomaly", @"Games\Anomaly", @"Games\Anomaly 1.5.3", "Anomaly-1.5.3-full", @"Games\Anomaly-1.5.3-full" })
                {
                    var p = Path.Combine(drive.RootDirectory.FullName, rel);
                    if (IsGame(p)) return p;
                }
            return null;
        }

        string Game => gameDir.Text.Trim().TrimEnd('\\');
        string Backup => Path.Combine(Game, BackupDir);

        void ShowState()
        {
            if (!IsGame(Game))
            {
                state.ForeColor = Theme.Danger;
                state.Text = "Это не папка Anomaly: нужны fsgame.ltx и папка bin.";
                install.Enabled = restore.Enabled = false;
                return;
            }
            bool has = File.Exists(Path.Combine(Backup, Installed));
            var ver = has && File.Exists(Path.Combine(Backup, "version.txt")) ? File.ReadAllText(Path.Combine(Backup, "version.txt")).Trim() : "";
            state.ForeColor = has ? Theme.Amber : Theme.Ok;
            state.Text = has
                ? $"Кооператив {ver} уже установлен. Оригиналы игры сохранены в {Backup}"
                : "Ванильная игра. Файлы, которые заменит кооператив, будут сохранены в " + Backup;
            install.Text = has ? "Обновить" : "Установить";
            install.Enabled = true;
            restore.Enabled = has;
        }

        void Run(Action work)
        {
            if (GameRunning())
            {
                if (quiet) Say("Игра запущена из этой папки: закройте её", Theme.Danger);
                else ZoneMessage.Show(this, "Закройте игру и лаунчер Anomaly, затем повторите.");
                return;
            }
            install.Enabled = restore.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try { work(); }
            catch (UnauthorizedAccessException)
            {
                Say("Нет прав на запись в папку игры. Запустите установщик от имени администратора.", Theme.Danger);
            }
            catch (Exception e) { Say("Ошибка: " + e.Message, Theme.Danger); }
            finally
            {
                Cursor = Cursors.Default;
                ShowState();
            }
        }

        bool GameRunning()
        {
            var bin = Path.Combine(Game, "bin");
            foreach (var p in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("Anomaly", StringComparison.OrdinalIgnoreCase)))
            {
                try { if (p.MainModule.FileName.StartsWith(Game, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { }
            }
            return false;
        }

        // ------------------------------------------------------------------ install
        static HashSet<string> ReadList(string path) =>
            new HashSet<string>(File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Trim() != "") : new string[0], StringComparer.OrdinalIgnoreCase);

        void Install()
        {
            Directory.CreateDirectory(Backup);
            var installed = ReadList(Path.Combine(Backup, Installed));
            var backedUp = ReadList(Path.Combine(Backup, BackedUp));
            bool first = installed.Count == 0;
            int saved = 0, put = 0;
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("AnomalyCoop.zip"))
            using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries.Where(e => e.Name != ""))
                {
                    var rel = entry.FullName.Replace('/', '\\');
                    var target = Path.Combine(Game, rel);
                    // a file of the game this setup has not put there yet: its original goes to the backup, once
                    if (File.Exists(target) && !installed.Contains(rel) && !backedUp.Contains(rel))
                    {
                        var copy = Path.Combine(Backup, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(copy));
                        File.Copy(target, copy, true);
                        backedUp.Add(rel);
                        saved++;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    installed.Add(rel);
                    put++;
                }
            }
            // the lists go first to disk: a restore needs them even if what follows fails
            File.WriteAllLines(Path.Combine(Backup, Installed), installed.OrderBy(x => x));
            File.WriteAllLines(Path.Combine(Backup, BackedUp), backedUp.OrderBy(x => x));
            File.WriteAllText(Path.Combine(Backup, "version.txt"), version);

            Say($"Установлено файлов: {put}.", Theme.Text);
            if (first || saved > 0)
                Say($"Оригиналы игры ({saved} файлов) сохранены в {Backup}", Theme.Amber);
            else
                Say($"Резервная копия уже была: {Backup}. Оригиналы в ней не тронуты.", Theme.Amber);

            var exe = Path.Combine(Game, "bin", HasAvx() ? "AnomalyDX11AVX.exe" : "AnomalyDX11.exe");
            if (desktop.Checked) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName), exe);
            if (startMenu.Checked) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName), exe);
            if (desktop.Checked || startMenu.Checked) Say("Ярлыки «Anomaly Co-op» созданы: " + Path.GetFileName(exe), Theme.Text);
            Say("Готово. Запустите игру (DX11) и откройте «Сетевая игра» в главном меню.", Theme.Ok);
        }

        // ------------------------------------------------------------------ back to vanilla
        void Restore()
        {
            if (!quiet && !ZoneMessage.Ask(this, "Вернуть ванильную игру? Файлы кооператива будут удалены, оригиналы восстановлены. Сохранения останутся."))
                return;
            var installed = ReadList(Path.Combine(Backup, Installed));
            var backedUp = ReadList(Path.Combine(Backup, BackedUp));
            int back = 0, gone = 0;
            foreach (var rel in installed)
            {
                var target = Path.Combine(Game, rel);
                var copy = Path.Combine(Backup, rel);
                if (backedUp.Contains(rel) && File.Exists(copy))
                {
                    File.Copy(copy, target, true);
                    back++;
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                    gone++;
                }
            }
            foreach (var dir in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
            {
                var lnk = Path.Combine(Environment.GetFolderPath(dir), ShortcutName);
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            Directory.Delete(Backup, true);
            // folders the archive made and that are empty now (deepest first; the game's own keep their files)
            foreach (var dir in installed.Select(r => Path.GetDirectoryName(Path.Combine(Game, r))).Distinct()
                .OrderByDescending(d => d.Length))
                for (var d = dir; d != null && d.Length > Game.Length; d = Path.GetDirectoryName(d))
                {
                    if (!Directory.Exists(d) || Directory.EnumerateFileSystemEntries(d).Any()) break;
                    Directory.Delete(d);
                }
            Say($"Восстановлено оригиналов: {back}, удалено файлов кооператива: {gone}. Ярлыки убраны.", Theme.Text);
            Say("Игра снова ванильная.", Theme.Ok);
        }

        // ------------------------------------------------------------------ helpers
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(int feature);
        static bool HasAvx() { try { return IsProcessorFeaturePresent(39); } catch { return false; } } // PF_AVX_INSTRUCTIONS_AVAILABLE

        void Shortcut(string path, string exe)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            var lnk = shell.CreateShortcut(path);
            lnk.TargetPath = exe;
            lnk.WorkingDirectory = Game;
            lnk.IconLocation = exe + ",0";
            lnk.Description = "S.T.A.L.K.E.R. Anomaly — кооператив";
            lnk.Save();
            Marshal.FinalReleaseComObject(lnk);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
