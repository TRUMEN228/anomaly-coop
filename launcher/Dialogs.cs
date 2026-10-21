using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>Base for the launcher's child windows: borderless steel plate with a hazard-striped title bar.</summary>
    public class ZoneDialog : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        readonly ZoneButton closeX = new ZoneButton("✕");
        Bitmap backdrop;
        protected const int TitleH = 46;

        public ZoneDialog(string title, int width, int height)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(width, height);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body(9.5f);
            DoubleBuffered = true;
            KeyPreview = true;
            closeX.Font = Theme.Label(10);
            closeX.SetBounds(width - 46, 6, 38, 24);
            closeX.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(closeX);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // drop shadow
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }

        protected float S => DeviceDpi / 96f;
        protected int X(float v) => (int)(v * S);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backdrop == null || backdrop.Size != ClientSize)
            {
                backdrop?.Dispose();
                backdrop = new Bitmap(ClientSize.Width, ClientSize.Height);
                using (var g = Graphics.FromImage(backdrop))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    var all = new Rectangle(Point.Empty, ClientSize);
                    Theme.Steel(g, all, Theme.BgHigh, Theme.Bg, Text.GetHashCode());
                    using (var b = new SolidBrush(Color.FromArgb(160, 12, 12, 10))) g.FillRectangle(b, 0, 0, all.Width, X(36));
                    Theme.Trefoil(g, new PointF(X(20), X(18)), X(8), Theme.Hazard);
                    TextRenderer.DrawText(g, Theme.Spaced(Text), Theme.Label(9.5f), new Point(X(36), X(10)), Theme.Amber);
                    Theme.HazardBand(g, new Rectangle(0, X(36), all.Width, X(6)), X(10));
                    PaintExtra(g);
                    using (var p = new Pen(Theme.Edge, 2)) g.DrawRectangle(p, 1, 1, all.Width - 2, all.Height - 2);
                    using (var p = new Pen(Theme.Bevel)) g.DrawRectangle(p, 3, X(42), all.Width - 7, all.Height - X(42) - 4);
                }
            }
            e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
        }

        /// <summary>Static decoration drawn once into the backdrop (section labels etc.).</summary>
        protected virtual void PaintExtra(Graphics g) { }

        protected void Section(Graphics g, string text, int x, int y, int w)
        {
            var f = Theme.Label(8.5f);
            var t = Theme.Spaced(text);
            var sz = TextRenderer.MeasureText(g, t, f);
            TextRenderer.DrawText(g, t, f, new Point(X(x), X(y) - sz.Height / 2), Theme.Amber);
            using (var p = new Pen(Color.FromArgb(70, Theme.Amber))) g.DrawLine(p, X(x) + sz.Width + X(6), X(y), X(x + w), X(y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < X(42)) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
        }

        protected Label Caption(string text, int x, int y, int w)
        {
            var l = new Label { Text = text, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Body(9.5f), AutoEllipsis = true };
            l.SetBounds(x, y, w, 22);
            Controls.Add(l);
            return l;
        }

        protected T Add<T>(T c, int x, int y, int w, int h) where T : Control { c.SetBounds(x, y, w, h); Controls.Add(c); return c; }
    }

    /// <summary>A themed checkbox: square plate with an amber mark.</summary>
    public class ZoneCheck : Control
    {
        bool isChecked, hover;
        public bool Checked { get => isChecked; set { isChecked = value; Invalidate(); } }

        public ZoneCheck(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = Theme.Body(9.5f);
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; Focus(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = Height - 6;
            var r = new Rectangle(1, 3, box, box);
            using (var b = new SolidBrush(Theme.FieldBack)) g.FillRectangle(b, r);
            using (var p = new Pen(hover || Focused ? Theme.Amber : Color.FromArgb(80, 78, 64))) g.DrawRectangle(p, r);
            if (Checked)
                using (var p = new Pen(Theme.Amber, Math.Max(2, box / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(p, new[] { new PointF(r.X + box * 0.22f, r.Y + box * 0.52f), new PointF(r.X + box * 0.42f, r.Y + box * 0.74f), new PointF(r.X + box * 0.8f, r.Y + box * 0.28f) });
            TextRenderer.DrawText(g, Text, Font, new Rectangle(box + 10, 0, Width - box - 10, Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Themed replacement for MessageBox.</summary>
    public sealed class ZoneMessage : ZoneDialog
    {
        ZoneMessage(string title, string text, bool ask) : base(title, 440, 200)
        {
            var body = new Label { Text = text, BackColor = Color.Transparent, ForeColor = Theme.Text, Font = Theme.Body(10) };
            Add(body, 24, 60, 392, 80);
            var ok = Add(new ZoneButton(ask ? "Да" : "Понятно"), ask ? 196 : 296, 148, 120, 36);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = null;
            if (ask)
            {
                var no = Add(new ZoneButton("Нет"), 324, 148, 92, 36);
                no.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            }
        }

        public static void Show(IWin32Window owner, string text, string title = "Внимание")
        {
            using (var d = new ZoneMessage(title, text, false)) d.ShowDialog(owner);
        }

        public static bool Ask(IWin32Window owner, string text, string title = "Подтверждение")
        {
            using (var d = new ZoneMessage(title, text, true)) return d.ShowDialog(owner) == DialogResult.OK;
        }
    }

    /// <summary>Options: in-game keys, port, UPnP. Replaces the old system-styled dialog.</summary>
    public sealed class OptionsDialog : ZoneDialog
    {
        public static readonly string[][] Actions = KeysDialog.Actions;
        readonly ZoneSelect[] boxes = new ZoneSelect[Actions.Length];
        readonly ZoneCheck upnp = new ZoneCheck("Хост: открывать порт на роутере (UPnP)");
        readonly ZoneCheck rewards = new ZoneCheck("Хост: награда за задание обоим игрокам");
        readonly ZoneCheck revive = new ZoneCheck("Хост: нокдаун вместо смерти");
        readonly ZoneCheck ff = new ZoneCheck("Хост: огонь по своим");
        readonly ZoneCheck money = new ZoneCheck("Хост: общие деньги");
        readonly ZoneCheck follow = new ZoneCheck("Переходить на локацию вслед за напарником");
        readonly TextBox port = new TextBox();
        readonly Settings settings;

        sealed class Key { public string Dik; public override string ToString() => KeysDialog.Pretty(Dik); }

        public OptionsDialog(Settings s) : base("Опции", 440, 642)
        {
            settings = s;
            for (int i = 0; i < Actions.Length; i++)
            {
                int y = 82 + i * 40;
                Caption(Actions[i][1], 24, y + 5, 210);
                var sel = new ZoneSelect();
                foreach (var k in KeysDialog.Choices) sel.Items.Add(new Key { Dik = k });
                sel.SelectedItem = sel.Items.Cast<Key>().FirstOrDefault(k => k.Dik == s.Key(Actions[i][0])) ?? sel.Items[0];
                Add(new FieldFrame(sel), 250, y, 166, 30);
                boxes[i] = sel;
            }
            Caption("Порт для прямого IP", 24, 303, 210);
            port.Text = s.Port.ToString();
            port.KeyPress += (o, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            Add(new FieldFrame(port), 250, 298, 166, 30);
            upnp.Checked = s.Upnp;
            Add(upnp, 24, 340, 392, 26);
            rewards.Checked = s.ShareRewards;
            Add(rewards, 24, 410, 392, 26);
            revive.Checked = s.Revive; Add(revive, 24, 440, 392, 26);
            ff.Checked = s.FriendlyFire; Add(ff, 24, 470, 392, 26);
            money.Checked = s.SharedMoney; Add(money, 24, 500, 392, 26);
            follow.Checked = s.Follow; Add(follow, 24, 530, 392, 26);

            var save = Add(new ZoneButton("Сохранить"), 196, 582, 120, 38);
            var cancel = Add(new ZoneButton("Отмена"), 324, 582, 92, 38);
            save.Click += (o, e) => Save();
            cancel.Click += (o, e) => { DialogResult = DialogResult.Cancel; Close(); };
        }

        protected override void PaintExtra(Graphics g)
        {
            Section(g, "Клавиши в игре", 24, 64, 392);
            Section(g, "Сеть", 24, 284, 392);
            Section(g, "Игра вдвоём", 24, 390, 392);
        }

        void Save()
        {
            var chosen = boxes.Select(b => ((Key)b.SelectedItem).Dik).ToArray();
            if (chosen.Distinct().Count() != chosen.Length)
            {
                ZoneMessage.Show(this, "Одна клавиша назначена на два действия. Выберите разные клавиши.", "Клавиши");
                return;
            }
            if (!int.TryParse(port.Text, out var p) || p < 1024 || p > 65535)
            {
                ZoneMessage.Show(this, "Порт должен быть числом от 1024 до 65535.", "Порт");
                return;
            }
            for (int i = 0; i < Actions.Length; i++) settings.SetKey(Actions[i][0], chosen[i]);
            settings.Port = p;
            settings.Upnp = upnp.Checked;
            settings.ShareRewards = rewards.Checked;
            settings.Revive = revive.Checked;
            settings.FriendlyFire = ff.Checked;
            settings.SharedMoney = money.Checked;
            settings.Follow = follow.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// Folder picker in the launcher's style. Drives on top, folders below; folders that hold an
    /// Anomaly install (fsgame.ltx + bin) are marked so the player finds the game quickly.
    /// </summary>
    public sealed class FolderDialog : ZoneDialog
    {
        readonly TextBox path = new TextBox();
        readonly FolderList list = new FolderList();
        readonly Label verdict = new Label();
        readonly ZoneButton choose;
        public string SelectedPath => path.Text.Trim();

        public FolderDialog(string start) : base("Папка игры", 560, 470)
        {
            var up = Add(new ZoneButton("↑ Вверх"), 24, 58, 96, 30);
            Add(new FieldFrame(path), 128, 58, 408, 30);
            Add(list, 24, 98, 512, 280);
            verdict.BackColor = Color.Transparent;
            verdict.Font = Theme.Label(9);
            Add(verdict, 24, 386, 512, 20);
            choose = Add(new ZoneButton("Выбрать эту папку"), 248, 416, 180, 38);
            var cancel = Add(new ZoneButton("Отмена"), 436, 416, 100, 38);

            up.Click += (s, e) => { var p = Directory.GetParent(SelectedPath.TrimEnd('\\')); Open(p?.FullName ?? ""); };
            path.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Open(SelectedPath); e.SuppressKeyPress = true; } };
            list.Activated += full => Open(full);
            choose.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Open(Directory.Exists(start ?? "") ? start : "");
        }

        public static bool IsGame(string dir)
        {
            try { return File.Exists(Path.Combine(dir, "fsgame.ltx")) && Directory.Exists(Path.Combine(dir, "bin")); }
            catch { return false; }
        }

        void Open(string dir)
        {
            var items = new List<FolderList.Item>();
            if (string.IsNullOrEmpty(dir))
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                    items.Add(new FolderList.Item { Name = $"{d.Name.TrimEnd('\\')}  {SafeLabel(d)}", Full = d.RootDirectory.FullName, Drive = true });
                path.Text = "";
            }
            else
            {
                try
                {
                    foreach (var d in new DirectoryInfo(dir).GetDirectories().Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).OrderBy(d => d.Name))
                        items.Add(new FolderList.Item { Name = d.Name, Full = d.FullName, Game = IsGame(d.FullName) });
                }
                catch (Exception e) { items.Add(new FolderList.Item { Name = "нет доступа: " + e.Message, Full = null }); }
                path.Text = dir;
            }
            list.SetItems(items);
            bool game = SelectedPath.Length > 0 && IsGame(SelectedPath);
            verdict.ForeColor = game ? Theme.Ok : Theme.TextDim;
            verdict.Text = game ? "✓ ЗДЕСЬ УСТАНОВЛЕНА ANOMALY" : SelectedPath.Length == 0 ? "ВЫБЕРИТЕ ДИСК" : "ЗДЕСЬ НЕТ ИГРЫ: ИЩИТЕ ПАПКУ С fsgame.ltx И bin (ОТМЕЧЕНЫ ☢)";
            choose.Enabled = game;
        }

        static string SafeLabel(DriveInfo d) { try { return d.VolumeLabel; } catch { return ""; } }
    }

    /// <summary>The folder list inside FolderDialog: dark rows, amber hover, trefoil next to game folders.</summary>
    public sealed class FolderList : Control
    {
        public sealed class Item { public string Name, Full; public bool Game, Drive; }
        readonly List<Item> items = new List<Item>();
        int top, hover = -1;
        public event Action<string> Activated;
        int RowH => (int)(28 * DeviceDpi / 96f);

        public FolderList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.Body(10);
            Cursor = Cursors.Hand;
        }

        public void SetItems(IEnumerable<Item> it) { items.Clear(); items.AddRange(it); top = 0; hover = -1; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int visible = Math.Max(1, Height / RowH);
            top = Math.Max(0, Math.Min(Math.Max(0, items.Count - visible), top + (e.Delta > 0 ? -3 : 3)));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = top + e.Y / RowH;
            if (h != hover) { hover = h < items.Count ? h : -1; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = top + e.Y / RowH;
            if (i >= 0 && i < items.Count && items[i].Full != null) Activated?.Invoke(items[i].Full);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Screen)) g.FillRectangle(b, ClientRectangle);
            int y = 0;
            for (int i = top; i < items.Count && y < Height; i++, y += RowH)
            {
                var it = items[i];
                var row = new Rectangle(0, y, Width, RowH);
                if (i == hover) using (var b = new SolidBrush(Color.FromArgb(70, 56, 26))) g.FillRectangle(b, row);
                if (it.Game) Theme.Trefoil(g, new PointF(16, y + RowH / 2f), RowH * 0.3f, Theme.Hazard);
                else TextRenderer.DrawText(g, it.Drive ? "▪" : "›", Font, new Rectangle(4, y, 24, RowH), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                var color = it.Full == null ? Theme.Danger : it.Game ? Theme.Amber : i == hover ? Theme.Amber : Theme.Text;
                TextRenderer.DrawText(g, it.Name + (it.Game ? "   · ANOMALY" : ""), Font, new Rectangle(32, y, Width - 40, RowH), color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            if (items.Count == 0) TextRenderer.DrawText(g, "пусто", Font, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Color.FromArgb(28, 0, 0, 0))) for (int sy = 0; sy < Height; sy += 3) g.DrawLine(p, 0, sy, Width, sy);
            using (var p = new Pen(Color.FromArgb(70, 68, 56))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
