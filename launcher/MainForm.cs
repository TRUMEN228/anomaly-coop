using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>
    /// The co-op launcher window: borderless, drawn in the Zone style (see Theme).
    /// Left: who you are and how you connect, the PDA log and chat. Right: status lamps and actions.
    /// </summary>
    public sealed class MainForm : Form
    {
        readonly Settings settings = Settings.Load();

        // fields
        readonly TextBox nick = new TextBox();
        readonly TextBox gameDir = new TextBox();
        readonly TextBox hostIp = new TextBox();
        readonly TextBox chatInput = new TextBox();
        readonly ZoneSelect saves = new ZoneSelect { Empty = "нет сейвов" };
        readonly ZoneSelect friendsBox = new ZoneSelect { Empty = "никто из друзей не хостит — нажмите ↻" };
        FieldFrame nickF, dirF, ipF, chatF, savesF, friendsF;

        // buttons
        readonly ZoneButton modeHost = new ZoneButton("Хост") { Toggle = true };
        readonly ZoneButton modeJoin = new ZoneButton("Гость") { Toggle = true };
        readonly ZoneButton viaSteam = new ZoneButton("Steam") { Toggle = true };
        readonly ZoneButton viaIp = new ZoneButton("Прямой IP") { Toggle = true };
        readonly ZoneButton browse = new ZoneButton("...");
        readonly ZoneButton refresh = new ZoneButton("↻");
        readonly ZoneButton play = new ZoneButton("В Зону", true);
        readonly ZoneButton sendSession = new ZoneButton("Отправить сессию другу");
        readonly ZoneButton teleport = new ZoneButton("Телепорт к другу");
        readonly ZoneButton copyIp = new ZoneButton("Копировать адрес");
        readonly ZoneButton optionsBtn = new ZoneButton("Опции");
        readonly ZoneButton logsBtn = new ZoneButton("Логи");
        readonly ZoneButton uninstall = new ZoneButton("Удалить мод");
        readonly ZoneButton controlsBtn = new ZoneButton("⌨ Управление");
        readonly ZoneButton chatSend = new ZoneButton("Чат");
        readonly ZoneButton minBtn = new ZoneButton("—");
        readonly ZoneButton closeBtn = new ZoneButton("✕");
        readonly ZoneButton update = new ZoneButton("");
        readonly PdaLog log = new PdaLog { Title = "PDA · ЖУРНАЛ СВЯЗИ" };
        readonly Label rowLabel = new Label();
        readonly Label hint = new Label();
        readonly Timer timer = new Timer { Interval = 500 };
        readonly ToolTip tips = new ToolTip();

        Bridge bridge;
        Process game;
        bool sessionSent, upnpMapped;
        DateTime gameSeenSince = DateTime.MinValue, connectedSince = DateTime.MinValue;
        string ipText = "";
        Updater.Release pendingUpdate;
        Bitmap backdrop;

        // status lamps
        enum Lamp { Off, Wait, On, Bad }
        Lamp lampGame, lampNet, lampPeer;
        string textGame = "не запущена", textNet = "не запущена", textPeer = "нет", textAddr = "";

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public MainForm()
        {
            Text = "Anomaly Co-op v" + Updater.CurrentText;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(1000, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Font = Theme.Body(9.5f);
            Icon = MakeIcon();

            BuildLayout();
            LoadSettingsIntoUi();
            WireEvents();
            timer.Start();
            Print("Хост: выберите сейв и нажмите «В Зону». Гость: выберите друга в Steam (или впишите IP) и нажмите «В Зону».", Theme.TextDim);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ cp.Style |= 0x20000; /* WS_MINIMIZEBOX */ return cp; }
        }

        // ------------------------------------------------------------------ layout
        void Place(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); Controls.Add(c); }

        FieldFrame Field(Control inner, int x, int y, int w, int h = 30)
        {
            var f = new FieldFrame(inner);
            Place(f, x, y, w, h);
            return f;
        }

        void BuildLayout()
        {
            // title bar
            Place(minBtn, 900, 6, 42, 26);
            Place(closeBtn, 948, 6, 42, 26);
            Place(update, 700, 6, 190, 26);
            update.Visible = false;

            // left column
            nickF = Field(nick, 28, 172, 190);
            dirF = Field(gameDir, 232, 172, 316);
            Place(browse, 552, 172, 36, 30);
            Place(modeHost, 28, 244, 124, 34);
            Place(modeJoin, 156, 244, 124, 34);
            Place(viaSteam, 308, 244, 136, 34);
            Place(viaIp, 448, 244, 140, 34);
            rowLabel.SetBounds(28, 288, 400, 16);
            savesF = Field(saves, 28, 304, 520);
            friendsF = Field(friendsBox, 28, 304, 520);
            ipF = Field(hostIp, 28, 304, 520);
            Place(refresh, 552, 304, 36, 30);
            Place(log, 28, 352, 560, 182);
            chatF = Field(chatInput, 28, 540, 470);
            Place(chatSend, 504, 540, 84, 30);

            // right column
            Place(play, 618, 290, 354, 72);
            Place(sendSession, 618, 372, 354, 38);
            Place(teleport, 618, 416, 354, 38);
            Place(copyIp, 618, 460, 354, 38);
            Place(optionsBtn, 618, 512, 114, 36);
            Place(logsBtn, 738, 512, 114, 36);
            Place(uninstall, 858, 512, 114, 36);

            foreach (var l in new[] { rowLabel, hint })
            {
                l.BackColor = Color.Transparent;
                l.ForeColor = Theme.TextDim;
                l.Font = Theme.Label(8.5f);
                Controls.Add(l);
            }
            hint.SetBounds(28, 580, 584, 18);
            Place(controlsBtn, 618, 572, 354, 28);
            hint.Font = Theme.Mono(8.5f);

            minBtn.Font = closeBtn.Font = Theme.Label(10);
            update.Font = Theme.Label(9);
            refresh.Font = browse.Font = Theme.Label(11);
            tips.SetToolTip(optionsBtn, "Клавиши в игре, порт и автопроброс (UPnP)");
            tips.SetToolTip(logsBtn, "Собрать логи в архив на рабочий стол");
            tips.SetToolTip(uninstall, "Удалить мод из игры (сейвы не трогаются)");
            tips.SetToolTip(controlsBtn, "Клавиатура с клавишами кооператива и игры");
            tips.SetToolTip(copyIp, "Скопировать ваши адреса, чтобы отправить другу");
            tips.SetToolTip(refresh, "Обновить список");
            tips.SetToolTip(browse, "Выбрать папку игры");
        }

        // ------------------------------------------------------------------ painting
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backdrop == null || backdrop.Size != ClientSize) { backdrop?.Dispose(); backdrop = PaintBackdrop(ClientSize); }
            e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
        }

        float S => DeviceDpi / 96f;   // layout is in 96-dpi units; scale our own drawing to match
        int X(float v) => (int)(v * S);

        Bitmap PaintBackdrop(Size size)
        {
            var bmp = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var all = new Rectangle(Point.Empty, size);
                Theme.Steel(g, all, Theme.BgHigh, Theme.Bg, 1986);

                // radiation sign watermark
                Theme.Trefoil(g, new PointF(X(840), X(150)), X(150), Color.FromArgb(16, Theme.Amber));

                // title bar
                using (var b = new SolidBrush(Color.FromArgb(160, 12, 12, 10))) g.FillRectangle(b, 0, 0, size.Width, X(38));
                Theme.Trefoil(g, new PointF(X(22), X(19)), X(9), Theme.Hazard);
                TextRenderer.DrawText(g, Theme.Spaced("S.T.A.L.K.E.R. Anomaly") + "   ·   КООПЕРАТИВ", Theme.Label(9), new Point(X(40), X(11)), Theme.TextDim);
                Theme.HazardBand(g, new Rectangle(0, X(38), size.Width, X(8)), X(12));

                // headline
                TextRenderer.DrawText(g, "КООПЕРАТИВ", Theme.Display(38), new Point(X(24), X(54)), Color.FromArgb(60, 0, 0, 0));
                TextRenderer.DrawText(g, "КООПЕРАТИВ", Theme.Display(38), new Point(X(22), X(52)), Theme.Text);
                TextRenderer.DrawText(g, Theme.Spaced("двое в зоне") + "   ·   Anomaly 1.5.3   ·   v" + Updater.CurrentText,
                    Theme.Label(9.5f), new Point(X(28), X(116)), Theme.Amber);

                // section labels
                Section(g, "Сталкер", 28, 144, 560);
                SmallLabel(g, "ПОЗЫВНОЙ", 28, 157);
                SmallLabel(g, "ПАПКА ИГРЫ", 232, 157);
                Section(g, "Режим", 28, 226, 252);
                Section(g, "Связь", 308, 226, 280);
                Section(g, "Состояние", 618, 144, 354);

                // status plate frame
                var plate = new Rectangle(X(618), X(176), X(354), X(104));
                Theme.Steel(g, plate, Color.FromArgb(36, 35, 29), Color.FromArgb(26, 25, 21), 7);
                using (var p = new Pen(Theme.Edge)) g.DrawRectangle(p, plate.X, plate.Y, plate.Width - 1, plate.Height - 1);
                Theme.Rivet(g, plate.X + X(7), plate.Y + X(7)); Theme.Rivet(g, plate.Right - X(8), plate.Y + X(7));
                Theme.Rivet(g, plate.X + X(7), plate.Bottom - X(8)); Theme.Rivet(g, plate.Right - X(8), plate.Bottom - X(8));

                // bottom band
                Theme.HazardBand(g, new Rectangle(0, size.Height - X(10), size.Width, X(10)), X(12));
                using (var p = new Pen(Theme.Edge, 2)) g.DrawRectangle(p, 1, 1, size.Width - 2, size.Height - 2);
            }
            return bmp;
        }

        void Section(Graphics g, string text, int x, int y, int w)
        {
            var f = Theme.Label(9);
            var t = Theme.Spaced(text);
            var sz = TextRenderer.MeasureText(g, t, f);
            TextRenderer.DrawText(g, t, f, new Point(X(x), X(y) - sz.Height / 2), Theme.Amber);
            using (var p = new Pen(Color.FromArgb(70, Theme.Amber))) g.DrawLine(p, X(x) + sz.Width + X(6), X(y), X(x + w), X(y));
        }

        void SmallLabel(Graphics g, string text, int x, int y) =>
            TextRenderer.DrawText(g, text, Theme.Label(8), new Point(X(x), X(y)), Theme.TextDim);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawLamp(g, 0, "ИГРА", lampGame, textGame);
            DrawLamp(g, 1, "СВЯЗЬ", lampNet, textNet);
            DrawLamp(g, 2, "НАПАРНИК", lampPeer, textPeer);
            if (textAddr.Length > 0)
                TextRenderer.DrawText(g, textAddr, Theme.Mono(8.5f), new Rectangle(X(640), X(254), X(320), X(18)), Theme.TextDim, TextFormatFlags.EndEllipsis);
        }

        void DrawLamp(Graphics g, int row, string name, Lamp lamp, string value)
        {
            int y = X(190 + row * 22);
            var c = lamp == Lamp.On ? Theme.Ok : lamp == Lamp.Wait ? Theme.Amber : lamp == Lamp.Bad ? Theme.Danger : Color.FromArgb(60, 58, 48);
            var r = new RectangleF(X(640), y + X(3), X(11), X(11));
            if (lamp != Lamp.Off)
                using (var glow = new SolidBrush(Color.FromArgb(50, c))) g.FillEllipse(glow, RectangleF.Inflate(r, X(4), X(4)));
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            using (var p = new Pen(Theme.Edge)) g.DrawEllipse(p, r);
            TextRenderer.DrawText(g, name, Theme.Label(9), new Point(X(662), y), Theme.TextDim);
            TextRenderer.DrawText(g, value, Theme.Body(9.5f), new Rectangle(X(752), y, X(210), X(18)), Theme.Text, TextFormatFlags.EndEllipsis);
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(new SolidBrush(Theme.Hazard), 0, 0, 31, 31);
                    Theme.Trefoil(g, new PointF(16, 16), 13, Color.FromArgb(24, 22, 16));
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // drag the borderless window by its title bar or headline
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < X(140)) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
        }

        // ------------------------------------------------------------------ settings and modes
        bool IsHost => modeHost.Checked;
        bool UseSteam => viaSteam.Checked;

        void LoadSettingsIntoUi()
        {
            gameDir.Text = settings.GameDir ?? DetectGameDir();
            nick.Text = string.IsNullOrWhiteSpace(settings.Nick) ? Environment.UserName : settings.Nick;
            hostIp.Text = settings.HostIp ?? "";
            modeHost.Checked = settings.Mode != "client";
            modeJoin.Checked = !modeHost.Checked;
            viaSteam.Checked = settings.Transport != "ip";
            viaIp.Checked = !viaSteam.Checked;
            FillSaves();
            UpdateMode();
            UpdateHint();
        }

        void UpdateMode()
        {
            bool host = IsHost, steam = UseSteam;
            savesF.Visible = host;
            friendsF.Visible = !host && steam;
            ipF.Visible = !host && !steam;
            refresh.Visible = host || steam;
            rowLabel.Text = host ? "СЕЙВ, С КОТОРОГО НАЧНЁТСЯ СЕССИЯ" : steam ? "ДРУГ В STEAM, КОТОРЫЙ СОЗДАЛ ИГРУ" : "IP ХОСТА";
            copyIp.Visible = host && !steam;
            if (!host && steam) FillFriends();
        }

        void SetMode(bool host) { if (bridge != null) return; modeHost.Checked = host; modeJoin.Checked = !host; UpdateMode(); }
        void SetTransport(bool steam) { if (bridge != null) return; viaSteam.Checked = steam; viaIp.Checked = !steam; UpdateMode(); }

        void FillFriends()
        {
            if (!SteamNet.Init(out var err))
            {
                friendsBox.Items.Clear(); friendsBox.SelectedItem = null;
                Print("Steam недоступен: " + err + " Можно играть по прямому IP.", Theme.Danger);
                return;
            }
            var keep = (friendsBox.SelectedItem as SteamNet.FriendLobby)?.Lobby;
            friendsBox.Items.Clear(); friendsBox.SelectedItem = null;
            friendsBox.Items.AddRange(SteamNet.FriendLobbies());
            if (friendsBox.Items.Count == 0)
            {
                Print("Никто из друзей в Steam сейчас не хостит. Попросите друга нажать «В Зону» в режиме «Хост», затем нажмите ↻.", Theme.TextDim);
                return;
            }
            var again = friendsBox.Items.Cast<SteamNet.FriendLobby>().FirstOrDefault(f => f.Lobby == keep);
            friendsBox.SelectedItem = again ?? friendsBox.Items[0];
            friendsBox.Invalidate();
        }

        void WireEvents()
        {
            modeHost.Click += (s, e) => SetMode(true);
            modeJoin.Click += (s, e) => SetMode(false);
            viaSteam.Click += (s, e) => SetTransport(true);
            viaIp.Click += (s, e) => SetTransport(false);
            browse.Click += (s, e) => BrowseGame();
            refresh.Click += (s, e) => { if (IsHost) FillSaves(); else FillFriends(); };
            gameDir.Leave += (s, e) => FillSaves();
            play.Click += (s, e) => { if (bridge == null) Start(); else StopSession(); };
            sendSession.Click += (s, e) => RequestSessionSave();
            teleport.Click += (s, e) => bridge?.ToGame("cmd|tp");
            copyIp.Click += (s, e) => CopyIp();
            optionsBtn.Click += (s, e) => EditOptions();
            logsBtn.Click += (s, e) => CollectLogs();
            uninstall.Click += (s, e) => UninstallMod();
            controlsBtn.Click += (s, e) => { using (var d = new ControlsDialog(settings)) d.ShowDialog(this); };
            chatSend.Click += (s, e) => SendChat();
            chatInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { SendChat(); e.SuppressKeyPress = true; } };
            minBtn.Click += (s, e) => WindowState = FormWindowState.Minimized;
            closeBtn.Click += (s, e) => Close();
            update.Click += (s, e) => ApplyUpdate();
            timer.Tick += (s, e) => Tick();

            // a friend invited us or we clicked "Join game" in the Steam friends list
            SteamNet.JoinRequested += lobby => Ui(() =>
            {
                if (bridge != null) return;
                SetMode(false); SetTransport(true);
                FillFriends();
                var f = friendsBox.Items.Cast<SteamNet.FriendLobby>().FirstOrDefault(x => x.Lobby == lobby);
                if (f == null) { f = new SteamNet.FriendLobby { Name = "Друг (приглашение)", Lobby = lobby }; friendsBox.Items.Add(f); }
                friendsBox.SelectedItem = f;
                Print("Приглашение из Steam принято", Theme.Amber);
                Start();
            });
            SteamNet.Log += s => Ui(() => Print(s, Theme.Danger));

            Shown += async (s, e) =>
            {
                if (Environment.GetCommandLineArgs().Contains("--auto")) Start();
                pendingUpdate = await Updater.CheckAsync();
                if (pendingUpdate == null) return;
                update.Text = "Обновить до " + pendingUpdate.Tag;
                update.Visible = true;
                Print($"Вышла новая версия {pendingUpdate.Tag}. Нажмите «Обновить» вверху (обновиться должны оба игрока).", Theme.Amber);
            };
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
            var start = IsGameDir(gameDir.Text) ? Path.GetDirectoryName(gameDir.Text.TrimEnd('\\')) : "";
            using (var d = new FolderDialog(start))
                if (d.ShowDialog(this) == DialogResult.OK) { gameDir.Text = d.SelectedPath; FillSaves(); }
        }

        void FillSaves()
        {
            var keep = saves.SelectedItem as string ?? settings.LastSave;
            saves.Items.Clear(); saves.SelectedItem = null;
            var dir = Path.Combine(gameDir.Text, "appdata", "savedgames");
            if (Directory.Exists(dir))
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.scop").OrderByDescending(f => f.LastWriteTime))
                    saves.Items.Add(Path.GetFileNameWithoutExtension(f.Name));
            if (keep != null && saves.Items.Contains(keep)) saves.SelectedItem = keep;
            else if (saves.Items.Count > 0) saves.SelectedItem = saves.Items[0];
            saves.Invalidate();
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
            if (n > 0) Print($"Мод установлен/обновлён в {dir} ({n} файлов)", Theme.TextDim);
        }

        void UninstallMod()
        {
            if (!IsGameDir(gameDir.Text) || bridge != null) return;
            if (!ZoneMessage.Ask(this, "Удалить файлы мода из папки игры? Сейвы и персонаж гостя останутся.", "Удалить мод")) return;
            var dir = Path.Combine(gameDir.Text, "gamedata", "scripts");
            int n = 0;
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "coop_*.script")) { File.Delete(f); n++; }
            Print(n > 0 ? $"Мод удалён ({n} файлов). Ваши сейвы не тронуты." : "Мод не установлен.", Theme.TextDim);
        }

        // ------------------------------------------------------------------ session
        void Start()
        {
            if (!IsGameDir(gameDir.Text)) { Print("Укажите папку с игрой (там должны быть fsgame.ltx и bin).", Theme.Danger); return; }
            if (string.IsNullOrWhiteSpace(nick.Text)) { Print("Введите позывной.", Theme.Danger); return; }
            bool host = IsHost, steam = UseSteam;
            if (host && saves.SelectedItem == null) { Print("Нет сейвов. Начните новую игру в Anomaly и сохранитесь.", Theme.Danger); return; }
            if (!host && !steam && string.IsNullOrWhiteSpace(hostIp.Text)) { Print("Введите IP хоста.", Theme.Danger); return; }
            var friend = friendsBox.SelectedItem as SteamNet.FriendLobby;
            if (!host && steam && friend == null) { Print("Выберите друга в Steam, который создал игру (кнопка ↻ обновляет список).", Theme.Danger); return; }
            if (steam && !SteamNet.Init(out var err)) { Print("Steam недоступен: " + err + " Переключитесь на «Прямой IP».", Theme.Danger); return; }

            settings.GameDir = gameDir.Text; settings.Nick = nick.Text.Trim(); settings.HostIp = hostIp.Text.Trim();
            settings.Mode = host ? "host" : "client"; settings.LastSave = saves.SelectedItem as string;
            settings.Transport = steam ? "steam" : "ip";
            settings.Save();

            try { InstallMod(); settings.WriteGameKeys(gameDir.Text); }
            catch (Exception e) { Print("Не удалось установить мод: " + e.Message, Theme.Danger); return; }

            bridge = new Bridge(gameDir.Text, host ? "host" : "client", settings.Nick);
            bridge.Log += s => Ui(() => Print(s, Theme.Amber));
            bridge.Chat += (name, text) => Ui(() => Print($"[{name}] {text}", Theme.Text));
            bridge.ConnectionChanged += (up, name) => Ui(() =>
            {
                connectedSince = up ? DateTime.Now : DateTime.MinValue;
                if (!up) sessionSent = false;
                if (steam && host) SteamNet.SetPresence(up ? "Anomaly Co-op: в Зоне с " + name : "Anomaly Co-op: ждёт напарника");
            });
            bridge.PeerResumed += () => Ui(() =>
            {
                sessionSent = true;
                Print($"{bridge.PeerName} вернулся в сессию, мир не перезагружается", Theme.Amber);
            });
            bridge.GameSaved += name => { if (bridge.Connected) bridge.SendSaveFiles(name); };
            bridge.SessionReady += () => Ui(OnSessionReady);

            sessionSent = false;
            try
            {
                if (host && steam) bridge.HostSteam();
                else if (host) bridge.Host(settings.Port);
                else if (steam) bridge.JoinSteam(friend.Lobby);
                else bridge.Join(settings.HostIp, settings.Port);
            }
            catch (Exception e)
            {
                Print("Ошибка сети: " + e.Message, Theme.Danger);
                bridge.Dispose(); bridge = null;
                return;
            }

            if (host && !steam) StartIpHosting();
            if (host && steam) Print("Друг выбирает вас в списке «Друг в Steam» в своём лаунчере или жмёт «Присоединиться» в списке друзей Steam.", Theme.TextDim);
            if (host && !GameRunning()) LaunchGame(settings.LastSave);
            if (!host) Print("Игра запустится сама, когда хост пришлёт свой мир.", Theme.TextDim);

            play.Text = "Покинуть Зону";
            foreach (Control c in new Control[] { nickF, dirF, browse, modeHost, modeJoin, viaSteam, viaIp, savesF, friendsF, ipF, refresh, uninstall, optionsBtn }) c.Enabled = false;
        }

        void StartIpHosting()
        {
            ShowHostAddresses();
            var port = settings.Port;
            if (!settings.Upnp || Environment.GetCommandLineArgs().Contains("--no-upnp")) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                var lan = NetInfo.PrimaryLanIp();
                string why = "нет локальной сети";
                var ext = lan == null ? null : NetInfo.UpnpMap(port, lan, out why);
                Ui(() =>
                {
                    if (ext != null)
                    {
                        upnpMapped = true;
                        Print($"Порт {port} открыт на роутере через UPnP. IP для друга через интернет: {ext}", Theme.Amber);
                        ipText = ipText.Length > 0 ? ipText + ", " + ext : ext;
                    }
                    else Print($"Автопроброс порта не удался ({why}). Для игры через интернет используйте Steam или Radmin VPN.", Theme.TextDim);
                });
            });
        }

        void StopSession()
        {
            if (upnpMapped) { NetInfo.UpnpUnmap(settings.Port); upnpMapped = false; }
            bridge?.Dispose();
            bridge = null;
            ipText = "";
            play.Text = "В Зону";
            foreach (Control c in new Control[] { nickF, dirF, browse, modeHost, modeJoin, viaSteam, viaIp, savesF, friendsF, ipF, refresh, uninstall, optionsBtn }) c.Enabled = true;
            UpdateMode();
            Print("Сессия остановлена", Theme.TextDim);
        }

        void RequestSessionSave()
        {
            if (bridge == null) return;
            bridge.ToGame("cmd|sync");
            sessionSent = true;
            Print("Сохраняю сессию и отправляю другу...", Theme.TextDim);
        }

        void OnSessionReady()
        {
            Print("Мир хоста получен", Theme.Amber);
            if (!GameRunning()) LaunchGame("coop_session");
            else Print($"Игра уже запущена: нажмите {KeysDialog.Pretty(settings.Key("load"))} в игре, чтобы войти в мир хоста.", Theme.Amber);
        }

        void SendChat()
        {
            var text = chatInput.Text.Trim();
            if (text.Length == 0 || bridge == null) return;
            bridge.SendChat(text);
            Print($"[{settings.Nick}] {text}", Theme.Text);
            chatInput.Clear();
        }

        bool GameRunning()
        {
            if (game != null && !game.HasExited) return true;
            if (!IsGameDir(gameDir.Text)) return false;
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
                Print($"Запуск игры: {Path.GetFileName(exe)}, сейв «{save}»", Theme.TextDim);
            }
            catch (Exception e) { Print("Не удалось запустить игру: " + e.Message, Theme.Danger); }
        }

        void Tick()
        {
            var before = (lampGame, lampNet, lampPeer, textGame, textNet, textPeer, textAddr);
            bool running = GameRunning();
            if (bridge == null)
            {
                lampGame = running ? Lamp.Wait : Lamp.Off; textGame = running ? "запущена" : "не запущена";
                lampNet = Lamp.Off; textNet = "не запущена";
                lampPeer = Lamp.Off; textPeer = "нет";
                textAddr = "";
                sendSession.Enabled = teleport.Enabled = copyIp.Enabled = false;
            }
            else
            {
                bool host = settings.Mode == "host", steam = settings.Transport == "steam";
                bool seen = bridge.GameSeen;
                if (!seen) gameSeenSince = DateTime.MinValue;
                else if (gameSeenSince == DateTime.MinValue) gameSeenSince = DateTime.Now;

                // host: send the world once both are ready (a returning guest says "resume" first)
                if (host && bridge.Connected && !sessionSent && seen && (DateTime.Now - gameSeenSince).TotalSeconds > 5
                    && (DateTime.Now - connectedSince).TotalSeconds > 3)
                    RequestSessionSave();

                lampGame = seen ? Lamp.On : running ? Lamp.Wait : Lamp.Off;
                textGame = seen ? "в игре" : running ? "загрузка / меню" : "не запущена";
                lampNet = bridge.Connected ? Lamp.On : Lamp.Wait;
                textNet = (steam ? "Steam" : "IP") + (bridge.Connected ? " · связь есть" : host ? " · ждём друга" : " · подключаемся");
                lampPeer = bridge.Connected ? Lamp.On : Lamp.Off;
                textPeer = bridge.Connected ? bridge.PeerName : "нет";
                textAddr = host && !steam && ipText.Length > 0 ? "АДРЕС: " + ipText
                    : steam && SteamNet.Ready ? "STEAM: " + SteamNet.MyName : "";
                sendSession.Enabled = host && bridge.Connected && seen;
                teleport.Enabled = bridge.Connected && seen;
                copyIp.Enabled = ipText.Length > 0;
            }
            if (!before.Equals((lampGame, lampNet, lampPeer, textGame, textNet, textPeer, textAddr))) Invalidate(new Rectangle(X(620), X(178), X(350), X(100)));
        }

        // ------------------------------------------------------------------ helpers
        void ShowHostAddresses()
        {
            var list = NetInfo.LocalAddresses();
            if (list.Count == 0) { Print("Не найдено ни одного сетевого адреса.", Theme.Danger); return; }
            Print("Ваши адреса для друга: " + string.Join(", ", list.Select(a => a.ToString())), Theme.Amber);
            Print("Друг в той же сети вписывает LAN-адрес, через Radmin VPN/ZeroTier/Tailscale — адрес этой сети.", Theme.TextDim);
            ipText = string.Join(", ", list.Select(a => a.Ip));
        }

        void CopyIp()
        {
            if (ipText.Length == 0) return;
            try { Clipboard.SetText(ipText); Print("Скопировано: " + ipText, Theme.TextDim); }
            catch (Exception e) { Print("Не удалось скопировать: " + e.Message, Theme.Danger); }
        }

        void UpdateHint()
        {
            string K(string a) => KeysDialog.Pretty(settings.Key(a));
            hint.Text = $"В ИГРЕ:  {K("tp")} телепорт · {K("sync")}/{K("load")} сессия · {K("status")} статус · {K("give")} передать вещи";
        }

        void EditOptions()
        {
            using (var d = new OptionsDialog(settings))
                if (d.ShowDialog(this) != DialogResult.OK) return;
            settings.Save();
            UpdateHint();
            if (IsGameDir(gameDir.Text)) { try { settings.WriteGameKeys(gameDir.Text); } catch { } }
            Print("Настройки сохранены. Новые клавиши заработают после следующей загрузки сейва.", Theme.TextDim);
        }

        void CollectLogs()
        {
            try
            {
                var zip = LogCollector.Collect(gameDir.Text);
                Print("Логи собраны: " + zip, Theme.Amber);
                Process.Start("explorer.exe", $"/select,\"{zip}\"");
            }
            catch (Exception e) { Print("Не удалось собрать логи: " + e.Message, Theme.Danger); }
        }

        async void ApplyUpdate()
        {
            if (pendingUpdate == null) return;
            update.Enabled = false;
            update.Text = "Скачиваю...";
            try
            {
                await System.Threading.Tasks.Task.Run(() => Updater.Apply(pendingUpdate));
                Print("Обновление скачано, лаунчер перезапускается...", Theme.Amber);
                Close();
            }
            catch (Exception e)
            {
                Print("Не удалось обновиться: " + e.Message, Theme.Danger);
                update.Text = "Обновить до " + pendingUpdate.Tag;
                update.Enabled = true;
            }
        }

        void Print(string s, Color color)
        {
            log.Add($"{DateTime.Now:HH:mm:ss}  {s}", color);
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
            if (upnpMapped) NetInfo.UpnpUnmap(settings.Port);
            bridge?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
