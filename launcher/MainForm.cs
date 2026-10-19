using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CoopLauncher
{
    public sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();

        static readonly Color Fore = Color.FromArgb(225, 225, 225);
        static readonly Color FieldBack = Color.FromArgb(28, 28, 28);
        static readonly Color ButtonBack = Color.FromArgb(38, 38, 38);

        readonly TextBox gameDir = Field(110, 50, 360);
        readonly TextBox nick = Field(110, 80, 160);
        readonly RadioButton modeHost = Radio("Создать игру (хост)", 110, 112);
        readonly RadioButton modeJoin = Radio("Подключиться к другу", 290, 112);
        readonly ComboBox saves = new ComboBox { Left = 110, Top = 142, Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = FieldBack, ForeColor = Fore };
        readonly TextBox hostIp = Field(110, 142, 360);
        readonly NumericUpDown port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Left = 400, Top = 80, Width = 70, BackColor = FieldBack, ForeColor = Fore, BorderStyle = BorderStyle.FixedSingle };
        readonly Button start = Btn("Играть в кооператив", 575, 215, 358, 62, 11);
        readonly Button stop = Btn("Остановить сессию", 575, 288, 358, 36);
        readonly Button sendSession = Btn("Отправить сессию другу", 575, 330, 358, 36);
        readonly Button teleport = Btn("Телепорт к другу", 575, 372, 358, 36);
        readonly Button uninstall = Btn("Удалить мод из игры", 575, 414, 175, 36);
        readonly Button quit = Btn("Выход", 758, 414, 175, 36);
        readonly Button browse = Btn("...", 475, 49, 50, 25);
        readonly Button refresh = Btn("↻", 475, 141, 50, 25);
        readonly Label status = Lbl("", 14, 176, true);
        readonly TextBox log = new TextBox { Left = 14, Top = 200, Width = 511, Height = 158, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = FieldBack, ForeColor = Fore, BorderStyle = BorderStyle.FixedSingle };
        readonly TextBox chatInput = Field(14, 366, 430);
        readonly Button chatSend = Btn("Чат", 450, 365, 75, 25);
        readonly Label saveLabel = Lbl("Сейв:", 14, 145);
        readonly Label ipLabel = Lbl("IP хоста:", 14, 145);
        readonly Timer timer = new Timer { Interval = 500 };
        readonly Panel panel = new Panel { Left = 8, Top = 40, Width = 541, Height = 413, BackColor = Color.FromArgb(30, 30, 30) };

        Bridge bridge;
        Process game;
        bool sessionSent;
        DateTime gameSeenSince = DateTime.MinValue;

        static TextBox Field(int x, int y, int w) =>
            new TextBox { Left = x, Top = y, Width = w, BackColor = FieldBack, ForeColor = Fore, BorderStyle = BorderStyle.FixedSingle };
        static RadioButton Radio(string t, int x, int y) =>
            new RadioButton { Text = t, Left = x, Top = y, AutoSize = true, ForeColor = Fore, BackColor = Color.Transparent };
        static Label Lbl(string t, int x, int y, bool bold = false)
        {
            var l = new Label { Text = t, Left = x, Top = y + 3, AutoSize = true, ForeColor = Fore, BackColor = Color.Transparent };
            if (bold) l.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            return l;
        }
        static Button Btn(string t, int x, int y, int w, int h, float size = 9)
        {
            var b = new Button { Text = t, Left = x, Top = y, Width = w, Height = h, FlatStyle = FlatStyle.Flat, BackColor = ButtonBack, ForeColor = Fore, Font = new Font("Segoe UI", size), UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(85, 85, 85);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(80, 80, 80);
            return b;
        }

        public MainForm()
        {
            Text = "Anomaly Co-op Launcher v1.0";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(962, 493);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(18, 18, 18);
            BackgroundImageLayout = ImageLayout.Stretch;
            panel.BackgroundImageLayout = ImageLayout.Stretch;
            DoubleBuffered = true;

            panel.Controls.AddRange(new Control[] {
                Lbl("Папка игры:", 14, 50), gameDir, browse,
                Lbl("Ваш ник:", 14, 80), nick, Lbl("Порт:", 355, 80), port,
                Lbl("Режим:", 14, 112), modeHost, modeJoin,
                saveLabel, saves, ipLabel, hostIp, refresh,
                status, log, chatInput, chatSend });
            Controls.Add(panel);
            Controls.AddRange(new Control[] { start, stop, sendSession, teleport, uninstall, quit });
            var hint = Lbl("Num1 — телепорт к другу · Num2 — отправить сессию · Num3 — загрузить сессию · Num0 — статус", 8, 462);
            hint.ForeColor = Color.FromArgb(170, 170, 170);
            Controls.Add(hint);

            gameDir.Text = settings.GameDir ?? DetectGameDir();
            nick.Text = string.IsNullOrWhiteSpace(settings.Nick) ? Environment.UserName : settings.Nick;
            hostIp.Text = settings.HostIp ?? "";
            port.Value = settings.Port;
            (settings.Mode == "client" ? modeJoin : modeHost).Checked = true;
            LoadSkin();
            FillSaves();
            UpdateMode();
            stop.Enabled = false;

            modeHost.CheckedChanged += (s, e) => UpdateMode();
            gameDir.Leave += (s, e) => { FillSaves(); LoadSkin(); };
            browse.Click += (s, e) => BrowseGame();
            refresh.Click += (s, e) => FillSaves();
            start.Click += (s, e) => Start();
            stop.Click += (s, e) => StopSession();
            sendSession.Click += (s, e) => RequestSessionSave();
            teleport.Click += (s, e) => bridge?.ToGame("cmd|tp");
            uninstall.Click += (s, e) => UninstallMod();
            quit.Click += (s, e) => Close();
            chatSend.Click += (s, e) => SendChat();
            chatInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { SendChat(); e.SuppressKeyPress = true; } };
            timer.Tick += (s, e) => Tick();
            timer.Start();

            if (Environment.GetCommandLineArgs().Contains("--auto")) Shown += (s, e) => Start();
            Print("Хост: выберите сейв и нажмите «Играть». Друг: впишите IP хоста и нажмите «Играть».");
        }

        /// <summary>Borrow the look (background, panel, icon) from the player's own AnomalyLauncher.exe.</summary>
        void LoadSkin()
        {
            var exe = Path.Combine(gameDir.Text ?? "", "AnomalyLauncher.exe");
            if (BackgroundImage != null || !File.Exists(exe)) return;
            try
            {
                var asm = Assembly.LoadFile(exe);
                using (var s = asm.GetManifestResourceStream("Launcher.Properties.Resources.resources"))
                using (var r = new System.Resources.ResourceReader(s))
                    foreach (System.Collections.DictionaryEntry e in r)
                    {
                        if ((string)e.Key == "bg" && e.Value is Image bg) BackgroundImage = bg;
                        else if ((string)e.Key == "options2" && e.Value is Image p) { panel.BackgroundImage = p; panel.BackColor = Color.Transparent; }
                        else if ((string)e.Key == "Stalker" && e.Value is Icon i) Icon = i;
                    }
            }
            catch { /* plain dark theme */ }
        }

        void UpdateMode()
        {
            bool host = modeHost.Checked;
            saves.Visible = saveLabel.Visible = refresh.Visible = host;
            hostIp.Visible = ipLabel.Visible = !host;
        }

        // ------------------------------------------------------------------ game folder & saves
        static string DetectGameDir()
        {
            var candidates = new List<string> { AppDomain.CurrentDomain.BaseDirectory };
            var parent = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            if (parent != null) candidates.Add(parent.FullName);
            foreach (var c in candidates)
                if (IsGameDir(c)) return c.TrimEnd('\\');
            return "";
        }

        static bool IsGameDir(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "fsgame.ltx")) && Directory.Exists(Path.Combine(dir, "bin"));

        void BrowseGame()
        {
            using (var d = new FolderBrowserDialog { Description = "Папка с игрой (где лежат fsgame.ltx и bin)" })
            {
                if (IsGameDir(gameDir.Text)) d.SelectedPath = gameDir.Text;
                if (d.ShowDialog(this) == DialogResult.OK) { gameDir.Text = d.SelectedPath; FillSaves(); LoadSkin(); }
            }
        }

        void FillSaves()
        {
            var keep = saves.SelectedItem as string ?? settings.LastSave;
            saves.Items.Clear();
            var dir = Path.Combine(gameDir.Text, "appdata", "savedgames");
            if (Directory.Exists(dir))
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.scop").OrderByDescending(f => f.LastWriteTime))
                    saves.Items.Add(Path.GetFileNameWithoutExtension(f.Name));
            if (keep != null && saves.Items.Contains(keep)) saves.SelectedItem = keep;
            else if (saves.Items.Count > 0) saves.SelectedIndex = 0;
        }

        // ------------------------------------------------------------------ mod install
        void InstallMod()
        {
            var dir = Path.Combine(gameDir.Text, "gamedata", "scripts");
            Directory.CreateDirectory(dir);
            var asm = Assembly.GetExecutingAssembly();
            int n = 0;
            foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith("mod\\")))
            {
                var path = Path.Combine(dir, res.Substring(4));
                using (var src = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream())
                {
                    src.CopyTo(ms);
                    var data = ms.ToArray();
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(data))
                    {
                        File.WriteAllBytes(path, data);
                        n++;
                    }
                }
            }
            if (n > 0) Print($"Мод установлен/обновлён в {dir} ({n} файлов)");
        }

        void UninstallMod()
        {
            if (!IsGameDir(gameDir.Text)) return;
            var dir = Path.Combine(gameDir.Text, "gamedata", "scripts");
            int n = 0;
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "coop_*.script")) { File.Delete(f); n++; }
            Print(n > 0 ? $"Мод удалён ({n} файлов). Ваши сейвы не тронуты." : "Мод не установлен.");
        }

        // ------------------------------------------------------------------ session
        void Start()
        {
            if (!IsGameDir(gameDir.Text)) { MessageBox.Show(this, "Укажите папку с игрой (там должны быть fsgame.ltx и bin).", Text); return; }
            if (string.IsNullOrWhiteSpace(nick.Text)) { MessageBox.Show(this, "Введите ник.", Text); return; }
            bool host = modeHost.Checked;
            if (host && saves.SelectedItem == null) { MessageBox.Show(this, "Нет сейвов. Начните новую игру в Anomaly и сохранитесь.", Text); return; }
            if (!host && string.IsNullOrWhiteSpace(hostIp.Text)) { MessageBox.Show(this, "Введите IP хоста.", Text); return; }

            settings.GameDir = gameDir.Text; settings.Nick = nick.Text.Trim(); settings.HostIp = hostIp.Text.Trim();
            settings.Port = (int)port.Value; settings.Mode = host ? "host" : "client"; settings.LastSave = saves.SelectedItem as string;
            settings.Save();

            try { InstallMod(); }
            catch (Exception e) { Print("Не удалось установить мод: " + e.Message); return; }

            bridge = new Bridge(gameDir.Text, host ? "host" : "client", settings.Nick);
            bridge.Log += s => Ui(() => Print(s));
            bridge.Chat += (name, text) => Ui(() => Print($"[{name}] {text}"));
            bridge.ConnectionChanged += (up, name) => Ui(() => { if (!up) sessionSent = false; });
            bridge.GameSaved += name => { if (bridge.Connected) bridge.SendSaveFiles(name); };
            bridge.SessionReady += () => Ui(OnSessionReady);

            sessionSent = false;
            try
            {
                if (host) bridge.Host(settings.Port);
                else bridge.Join(settings.HostIp, settings.Port);
            }
            catch (Exception e)
            {
                Print("Ошибка сети: " + e.Message);
                bridge.Dispose(); bridge = null;
                return;
            }

            if (host && !GameRunning()) LaunchGame(settings.LastSave);
            if (!host) Print("Гость: игра запустится автоматически, когда хост пришлёт сессию.");

            start.Enabled = false; stop.Enabled = true;
            foreach (Control c in new Control[] { gameDir, browse, nick, modeHost, modeJoin, saves, refresh, hostIp, port, uninstall }) c.Enabled = false;
        }

        void StopSession()
        {
            bridge?.Dispose();
            bridge = null;
            start.Enabled = true; stop.Enabled = false;
            foreach (Control c in new Control[] { gameDir, browse, nick, modeHost, modeJoin, saves, refresh, hostIp, port, uninstall }) c.Enabled = true;
            UpdateMode();
            Print("Сессия остановлена");
        }

        void RequestSessionSave()
        {
            if (bridge == null) return;
            bridge.ToGame("cmd|sync");
            sessionSent = true;
            Print("Сохраняю сессию и отправляю другу...");
        }

        void OnSessionReady()
        {
            Print("Сессия хоста получена");
            if (!GameRunning()) LaunchGame("coop_session");
            else Print("Игра уже запущена: нажмите Num3 в игре, чтобы загрузить мир хоста.");
        }

        void SendChat()
        {
            var text = chatInput.Text.Trim();
            if (text.Length == 0 || bridge == null) return;
            bridge.SendChat(text);
            Print($"[{settings.Nick}] {text}");
            chatInput.Clear();
        }

        bool GameRunning()
        {
            if (game != null && !game.HasExited) return true;
            var exeDir = Path.GetFullPath(Path.Combine(gameDir.Text, "bin")).TrimEnd('\\');
            foreach (var p in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("Anomaly", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), exeDir, StringComparison.OrdinalIgnoreCase)) { game = p; return true; }
                }
                catch { }
            }
            return false;
        }

        void LaunchGame(string save)
        {
            var dir = gameDir.Text;
            string renderer = "DX11", avx = "AVX";
            var cfg = Path.Combine(dir, "AnomalyLauncher.cfg");
            if (File.Exists(cfg))
            {
                var lines = File.ReadAllLines(cfg);
                if (lines.Length > 0 && lines[0].StartsWith("DX")) renderer = lines[0].Trim();
                if (lines.Length > 1) avx = lines[1].Trim() == "AVX" ? "AVX" : "";
            }
            var exe = Path.Combine(dir, "bin", $"Anomaly{renderer}{avx}.exe");
            if (!File.Exists(exe)) exe = Path.Combine(dir, "bin", "AnomalyDX11.exe");
            // the engine appends commandline.txt by itself
            var args = $"-start server({save}/single/alife/load) client(localhost)";
            try
            {
                game = Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = dir, UseShellExecute = false });
                Print($"Запуск игры: {Path.GetFileName(exe)}, сейв «{save}»");
            }
            catch (Exception e) { Print("Не удалось запустить игру: " + e.Message); }
        }

        void Tick()
        {
            if (bridge == null)
            {
                status.Text = "Не запущено";
                sendSession.Enabled = teleport.Enabled = false;
                return;
            }
            bool host = settings.Mode == "host";
            bool seen = bridge.GameSeen;
            if (!seen) gameSeenSince = DateTime.MinValue;
            else if (gameSeenSince == DateTime.MinValue) gameSeenSince = DateTime.Now;

            // host: send the world to the guest once both are ready
            if (host && bridge.Connected && !sessionSent && seen && (DateTime.Now - gameSeenSince).TotalSeconds > 5)
                RequestSessionSave();

            sendSession.Enabled = host && bridge.Connected && seen;
            teleport.Enabled = bridge.Connected && seen;
            var peer = bridge.Connected ? $"друг: {bridge.PeerName}" : (host ? "ждём друга" : "нет связи с хостом");
            var g = seen ? "игра: в игре" : (GameRunning() ? "игра: загрузка/меню" : "игра: не запущена");
            status.Text = $"{(host ? "Хост" : "Гость")} · {peer} · {g}";
        }

        void Print(string s)
        {
            log.AppendText($"{DateTime.Now:HH:mm:ss}  {s}\r\n");
            FileLog(s);
        }

        public static void FileLog(string s)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coop_launcher.log"), $"{DateTime.Now:HH:mm:ss}  {s}\r\n"); } catch { }
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            FileLog("closing: " + e.CloseReason);
            bridge?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
