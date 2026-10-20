using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>
    /// The launcher's own look: weathered steel plates, hazard stripes, an amber PDA screen.
    /// Everything is drawn in code; no game files or downloaded art.
    /// </summary>
    public static class Theme
    {
        // palette
        public static readonly Color Bg = Color.FromArgb(21, 20, 16);
        public static readonly Color BgHigh = Color.FromArgb(31, 30, 23);
        public static readonly Color Plate = Color.FromArgb(46, 45, 38);
        public static readonly Color PlateLow = Color.FromArgb(30, 30, 25);
        public static readonly Color Edge = Color.FromArgb(10, 10, 8);
        public static readonly Color Bevel = Color.FromArgb(78, 76, 64);
        public static readonly Color Text = Color.FromArgb(214, 208, 188);
        public static readonly Color TextDim = Color.FromArgb(132, 128, 110);
        public static readonly Color Amber = Color.FromArgb(222, 164, 58);
        public static readonly Color AmberDim = Color.FromArgb(120, 90, 36);
        public static readonly Color Hazard = Color.FromArgb(201, 160, 38);
        public static readonly Color Rust = Color.FromArgb(110, 56, 24);
        public static readonly Color Danger = Color.FromArgb(196, 72, 48);
        public static readonly Color Ok = Color.FromArgb(150, 186, 96);
        public static readonly Color Screen = Color.FromArgb(14, 16, 11);
        public static readonly Color FieldBack = Color.FromArgb(15, 15, 12);

        // type: Bahnschrift ships with Windows 10/11; Consolas for the PDA
        public static Font Display(float size) => Make("Bahnschrift SemiBold Condensed", size, FontStyle.Regular, "Bahnschrift", FontStyle.Bold);
        public static Font Label(float size) => Make("Bahnschrift SemiCondensed", size, FontStyle.Regular, "Segoe UI", FontStyle.Bold);
        public static Font Body(float size) => Make("Bahnschrift", size, FontStyle.Regular, "Segoe UI", FontStyle.Regular);
        public static Font Mono(float size) => Make("Consolas", size, FontStyle.Regular, "Courier New", FontStyle.Regular);

        static readonly HashSet<string> installed = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        static Font Make(string family, float size, FontStyle style, string fallback, FontStyle fallbackStyle) =>
            installed.Contains(family) ? new Font(family, size, style) : new Font(fallback, size, fallbackStyle);

        /// <summary>Uppercase with wide tracking, the way stencilled labels read.</summary>
        public static string Spaced(string s) => string.Join(" ", s.ToUpperInvariant().ToCharArray());

        public static void Rivet(Graphics g, float x, float y, float r = 2.6f)
        {
            using (var b = new SolidBrush(Color.FromArgb(18, 18, 14))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            using (var b = new SolidBrush(Color.FromArgb(96, 94, 80))) g.FillEllipse(b, x - r + 0.6f, y - r + 0.6f, r * 1.3f, r * 1.3f);
        }

        public static void HazardBand(Graphics g, Rectangle r, int step = 14)
        {
            var state = g.Save();
            g.SetClip(r);
            using (var y = new SolidBrush(Hazard)) g.FillRectangle(y, r);
            using (var k = new SolidBrush(Color.FromArgb(24, 22, 16)))
                for (int x = r.Left - r.Height * 2; x < r.Right + r.Height; x += step * 2)
                    g.FillPolygon(k, new[] { new Point(x, r.Bottom), new Point(x + step, r.Bottom), new Point(x + step + r.Height, r.Top), new Point(x + r.Height, r.Top) });
            // wear
            var rnd = new Random(r.X * 31 + r.Y);
            using (var w = new SolidBrush(Color.FromArgb(70, 20, 18, 14)))
                for (int i = 0; i < r.Width / 6; i++) g.FillRectangle(w, r.Left + rnd.Next(r.Width), r.Top + rnd.Next(r.Height), 1 + rnd.Next(4), 1 + rnd.Next(2));
            g.Restore(state);
        }

        /// <summary>The radiation trefoil: three 60-degree blades around a hollow ring and a centre dot.</summary>
        public static void Trefoil(Graphics g, PointF c, float r, Color color)
        {
            using (var b = new SolidBrush(color))
            using (var blades = new GraphicsPath())
            {
                for (int i = 0; i < 3; i++)
                    blades.AddPie(c.X - r, c.Y - r, r * 2, r * 2, -120 + i * 120, 60);
                using (var region = new Region(blades))
                using (var hole = new GraphicsPath())
                {
                    hole.AddEllipse(c.X - r * 0.32f, c.Y - r * 0.32f, r * 0.64f, r * 0.64f);
                    region.Exclude(hole);
                    g.FillRegion(b, region);
                }
                g.FillEllipse(b, c.X - r * 0.2f, c.Y - r * 0.2f, r * 0.4f, r * 0.4f);
            }
        }

        /// <summary>Weathered steel: gradient, grain and a few rust stains.</summary>
        public static void Steel(Graphics g, Rectangle r, Color top, Color bottom, int seed)
        {
            using (var lg = new LinearGradientBrush(r, top, bottom, 90f)) g.FillRectangle(lg, r);
            var rnd = new Random(seed);
            using (var grain = new SolidBrush(Color.FromArgb(14, 255, 255, 230)))
            using (var dark = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                for (int i = 0; i < r.Width * r.Height / 90; i++)
                    g.FillRectangle(i % 2 == 0 ? grain : dark, r.Left + rnd.Next(r.Width), r.Top + rnd.Next(r.Height), 1, 1);
            for (int i = 0; i < 3; i++)
            {
                int w = 30 + rnd.Next(Math.Max(1, r.Width / 3)), h = 10 + rnd.Next(Math.Max(1, r.Height / 2));
                var e = new Rectangle(r.Left + rnd.Next(Math.Max(1, r.Width - w)), r.Top + rnd.Next(Math.Max(1, r.Height - h)), w, h);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(e);
                    using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(34, Rust), SurroundColors = new[] { Color.FromArgb(0, Rust) } })
                        g.FillEllipse(pg, e);
                }
            }
        }
    }

    /// <summary>A riveted steel plate button. Primary buttons get a hazard-stripe frame.</summary>
    public class ZoneButton : Control
    {
        bool hover, down;
        public bool Primary { get; set; }
        bool isChecked;
        public bool Toggle { get; set; }
        public bool Checked { get => isChecked; set { isChecked = value; Invalidate(); } }

        public ZoneButton(string text, bool primary = false)
        {
            Text = text;
            Primary = primary;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = primary ? Theme.Display(17) : Theme.Label(9.5f);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Parent?.BackColor ?? Theme.Bg)) g.FillRectangle(b, r);

            var body = r;
            if (Primary)
            {
                Theme.HazardBand(g, r, 9);
                body = Rectangle.Inflate(r, -5, -5);
            }
            bool on = Enabled && (Checked || down);
            Color top = !Enabled ? Theme.PlateLow : on ? Color.FromArgb(70, 56, 26) : hover ? Color.FromArgb(62, 60, 50) : Theme.Plate;
            Color bottom = !Enabled ? Theme.PlateLow : on ? Color.FromArgb(48, 38, 18) : Color.FromArgb(32, 31, 26);
            Theme.Steel(g, body, top, bottom, Text.GetHashCode());
            using (var p = new Pen(Theme.Edge)) g.DrawRectangle(p, body.X, body.Y, body.Width - 1, body.Height - 1);
            using (var p = new Pen(Enabled ? Theme.Bevel : Color.FromArgb(40, 40, 34))) g.DrawLine(p, body.X + 1, body.Y + 1, body.Right - 2, body.Y + 1);
            if (body.Height >= 26)
            {
                Theme.Rivet(g, body.X + 6, body.Y + 6); Theme.Rivet(g, body.Right - 7, body.Y + 6);
                Theme.Rivet(g, body.X + 6, body.Bottom - 7); Theme.Rivet(g, body.Right - 7, body.Bottom - 7);
            }
            if (Checked && Toggle)
                using (var b = new SolidBrush(Theme.Amber)) g.FillRectangle(b, body.X + 1, body.Bottom - 4, body.Width - 2, 3);

            Color fg = !Enabled ? Color.FromArgb(88, 86, 74) : (hover || Checked || Primary) ? Theme.Amber : Theme.Text;
            TextRenderer.DrawText(g, Primary ? Theme.Spaced(Text) : Text.ToUpperInvariant(), Font, body, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.AmberDim) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, body.X + 3, body.Y + 3, body.Width - 7, body.Height - 7);
        }
    }

    /// <summary>A recessed field frame that hosts a borderless TextBox or ComboBox.</summary>
    public class FieldFrame : Panel
    {
        public readonly Control Inner;

        public FieldFrame(Control inner)
        {
            Inner = inner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FieldBack;
            Padding = new Padding(7, 5, 7, 4);
            inner.BackColor = Theme.FieldBack;
            inner.ForeColor = Theme.Text;
            inner.Font = Theme.Body(10);
            inner.Dock = DockStyle.Fill;
            if (inner is TextBox tb) tb.BorderStyle = BorderStyle.None;
            Controls.Add(inner);
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();
            inner.EnabledChanged += (s, e) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Theme.FieldBack)) g.FillRectangle(b, r);
            using (var p = new Pen(Theme.Edge)) g.DrawLine(p, 0, 0, r.Width, 0);
            using (var p = new Pen(Inner.Focused ? Theme.Amber : Color.FromArgb(62, 60, 50)))
                g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            if (!Enabled || !Inner.Enabled)
                using (var b = new SolidBrush(Color.FromArgb(110, Theme.Bg))) g.FillRectangle(b, r);
        }
    }

    /// <summary>A themed drop-down: shows the selected item, opens a dark menu on click.</summary>
    public class ZoneSelect : Control
    {
        public readonly List<object> Items = new List<object>();
        object selected;
        bool hover;
        public event EventHandler SelectedChanged;
        public string Empty = "—";

        public object SelectedItem
        {
            get => selected;
            set { selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public ZoneSelect()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Theme.FieldBack;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down) Open(); base.OnKeyDown(e); }
        protected override void OnClick(EventArgs e) { Focus(); Open(); base.OnClick(e); }

        void Open()
        {
            if (!Enabled || Items.Count == 0) return;
            var menu = new ContextMenuStrip { Renderer = new ZoneMenuRenderer(), ShowImageMargin = false, BackColor = Theme.FieldBack, ForeColor = Theme.Text, Font = Font };
            foreach (var it in Items)
            {
                var item = new ToolStripMenuItem(it.ToString()) { Tag = it, ForeColor = it == selected ? Theme.Amber : Theme.Text, AutoSize = false, Width = Width, Height = 26 };
                item.Click += (s, e) => SelectedItem = ((ToolStripItem)s).Tag;
                menu.Items.Add(item);
            }
            menu.MaximumSize = new Size(Width, 400);
            menu.Show(this, new Point(0, Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Theme.FieldBack)) g.FillRectangle(b, r);
            var text = selected?.ToString() ?? Empty;
            TextRenderer.DrawText(g, text, Font, new Rectangle(0, 0, r.Width - r.Height, r.Height), Enabled ? (selected == null ? Theme.TextDim : Theme.Text) : Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "▼", Font, new Rectangle(r.Width - r.Height, 0, r.Height, r.Height), hover ? Theme.Amber : Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }

    class ZoneMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(Theme.FieldBack)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var p = new Pen(Theme.AmberDim)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var c = e.Item.Selected ? Color.FromArgb(70, 56, 26) : Theme.FieldBack;
            using (var b = new SolidBrush(c)) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Selected ? Theme.Amber : e.Item.ForeColor; base.OnRenderItemText(e); }
    }

    /// <summary>The PDA screen: amber text on a dark screen with scanlines; newest line at the bottom.</summary>
    public class PdaLog : Control
    {
        readonly List<KeyValuePair<string, Color>> lines = new List<KeyValuePair<string, Color>>();
        int scroll; // lines scrolled up from the bottom
        public string Title = "PDA";

        public PdaLog()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Mono(9.5f);
            BackColor = Theme.Screen;
        }

        public void Add(string text, Color color)
        {
            lines.Add(new KeyValuePair<string, Color>(text, color));
            if (lines.Count > 400) lines.RemoveRange(0, 100);
            scroll = 0;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, scroll + (e.Delta > 0 ? 3 : -3));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var b = new SolidBrush(Theme.Screen)) g.FillRectangle(b, r);
            // screen glow
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-r.Width / 4, -r.Height / 2, r.Width * 3 / 2, r.Height * 2);
                using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(26, Theme.Amber), SurroundColors = new[] { Color.FromArgb(0, Theme.Amber) } })
                    g.FillRectangle(pg, r);
            }

            // header
            var head = new Rectangle(10, 6, r.Width - 20, 18);
            TextRenderer.DrawText(g, Title, Theme.Label(8.5f), head, Theme.AmberDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, DateTime.Now.ToString("HH:mm"), Theme.Label(8.5f), head, Theme.AmberDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Color.FromArgb(60, Theme.Amber))) g.DrawLine(p, 10, 26, r.Width - 10, 26);

            // wrapped lines, bottom up
            var area = new Rectangle(10, 30, r.Width - 20, r.Height - 38);
            var wrapped = new List<KeyValuePair<string, Color>>();
            foreach (var l in lines)
                foreach (var w in Wrap(g, l.Key, area.Width)) wrapped.Add(new KeyValuePair<string, Color>(w, l.Value));
            int lh = Font.Height + 1;
            int fit = Math.Max(1, area.Height / lh);
            scroll = Math.Min(scroll, Math.Max(0, wrapped.Count - fit));
            int start = Math.Max(0, wrapped.Count - fit - scroll);
            int y = area.Top;
            for (int i = start; i < Math.Min(wrapped.Count, start + fit); i++, y += lh)
                TextRenderer.DrawText(g, wrapped[i].Key, Font, new Point(area.Left, y), wrapped[i].Value, TextFormatFlags.NoPrefix);

            // scanlines and frame
            using (var p = new Pen(Color.FromArgb(28, 0, 0, 0))) for (int sy = 0; sy < r.Height; sy += 3) g.DrawLine(p, 0, sy, r.Width, sy);
            using (var p = new Pen(Theme.Edge, 2)) g.DrawRectangle(p, 1, 1, r.Width - 2, r.Height - 2);
            using (var p = new Pen(Color.FromArgb(70, 68, 56))) g.DrawRectangle(p, 3, 3, r.Width - 7, r.Height - 7);
            if (scroll > 0) TextRenderer.DrawText(g, "▼ ещё " + scroll, Theme.Label(8), new Rectangle(0, r.Height - 20, r.Width - 12, 16), Theme.AmberDim, TextFormatFlags.Right);
        }

        IEnumerable<string> Wrap(Graphics g, string text, int width)
        {
            var words = text.Split(' ');
            var line = "";
            foreach (var w in words)
            {
                var test = line.Length == 0 ? w : line + " " + w;
                if (TextRenderer.MeasureText(g, test, Font, Size.Empty, TextFormatFlags.NoPadding).Width > width && line.Length > 0)
                {
                    yield return line;
                    line = "   " + w;
                }
                else line = test;
            }
            if (line.Length > 0) yield return line;
        }
    }
}
