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
        // palette: graphite with a muted amber for accents only
        public static readonly Color Bg = Color.FromArgb(19, 20, 21);
        public static readonly Color BgHigh = Color.FromArgb(25, 26, 27);
        public static readonly Color Plate = Color.FromArgb(36, 38, 39);
        public static readonly Color PlateLow = Color.FromArgb(27, 28, 29);
        public static readonly Color Edge = Color.FromArgb(9, 9, 10);
        public static readonly Color Bevel = Color.FromArgb(56, 59, 61);
        public static readonly Color Text = Color.FromArgb(214, 216, 210);
        public static readonly Color TextDim = Color.FromArgb(126, 131, 129);
        public static readonly Color Amber = Color.FromArgb(200, 156, 78);
        public static readonly Color AmberDim = Color.FromArgb(108, 86, 48);
        public static readonly Color Hazard = Color.FromArgb(200, 156, 78);
        public static readonly Color Rust = Color.FromArgb(92, 60, 40);
        public static readonly Color Danger = Color.FromArgb(186, 78, 62);
        public static readonly Color Ok = Color.FromArgb(132, 170, 108);
        public static readonly Color Screen = Color.FromArgb(14, 15, 15);
        public static readonly Color FieldBack = Color.FromArgb(14, 15, 16);

        // type: Bahnschrift ships with Windows 10/11; Consolas for the PDA
        public static Font Display(float size) => Make("Bahnschrift SemiBold Condensed", size, FontStyle.Regular, "Bahnschrift", FontStyle.Bold);
        public static Font Label(float size) => Make("Bahnschrift SemiCondensed", size, FontStyle.Regular, "Segoe UI", FontStyle.Bold);
        public static Font Body(float size) => Make("Bahnschrift", size, FontStyle.Regular, "Segoe UI", FontStyle.Regular);
        public static Font Mono(float size) => Make("Consolas", size, FontStyle.Regular, "Courier New", FontStyle.Regular);

        static readonly HashSet<string> installed = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        static Font Make(string family, float size, FontStyle style, string fallback, FontStyle fallbackStyle) =>
            installed.Contains(family) ? new Font(family, size, style) : new Font(fallback, size, fallbackStyle);

        /// <summary>Section and heading labels: plain uppercase.</summary>
        public static string Spaced(string s) => s.ToUpperInvariant();

        public static void Rivet(Graphics g, float x, float y, float r = 2.6f) { } // the strict style has no rivets

        /// <summary>A thin accent rule where the old hazard stripes were.</summary>
        public static void HazardBand(Graphics g, Rectangle r, int step = 14)
        {
            using (var b = new SolidBrush(Bg)) g.FillRectangle(b, r);
            using (var p = new Pen(Edge)) g.DrawLine(p, r.Left, r.Top, r.Right, r.Top);
            using (var p = new Pen(Color.FromArgb(150, AmberDim))) g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
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

        /// <summary>Flat panel with a barely visible vertical gradient.</summary>
        public static void Steel(Graphics g, Rectangle r, Color top, Color bottom, int seed)
        {
            using (var lg = new LinearGradientBrush(r, top, bottom, 90f)) g.FillRectangle(lg, r);
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
            Font = primary ? Theme.Label(13) : Theme.Label(9.5f);
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
            bool on = Enabled && (Checked || down);
            Color fill = !Enabled ? Theme.PlateLow : on ? Color.FromArgb(46, 42, 33) : hover ? Color.FromArgb(44, 46, 47) : Theme.Plate;
            if (Primary && Enabled) fill = down ? Color.FromArgb(52, 44, 30) : hover ? Color.FromArgb(44, 39, 30) : Color.FromArgb(34, 31, 26);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, body);
            Color frame = !Enabled ? Color.FromArgb(40, 42, 43) : Primary ? Theme.Amber : hover || Focused ? Theme.AmberDim : Theme.Bevel;
            using (var p = new Pen(frame)) g.DrawRectangle(p, body.X, body.Y, body.Width - 1, body.Height - 1);
            if (Checked && Toggle)
                using (var b = new SolidBrush(Theme.Amber)) g.FillRectangle(b, body.X + 1, body.Bottom - 3, body.Width - 2, 2);

            Color fg = !Enabled ? Color.FromArgb(84, 88, 88) : (Checked || Primary) ? Theme.Amber : hover ? Color.White : Theme.Text;
            TextRenderer.DrawText(g, Primary ? Theme.Spaced(Text) : Text.ToUpperInvariant(), Font, body, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
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
            using (var p = new Pen(Inner.Focused ? Theme.AmberDim : Theme.Bevel))
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

            // header
            var head = new Rectangle(10, 6, r.Width - 20, 18);
            TextRenderer.DrawText(g, Title, Theme.Label(8.5f), head, Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, DateTime.Now.ToString("HH:mm"), Theme.Label(8.5f), head, Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Theme.Bevel)) g.DrawLine(p, 10, 26, r.Width - 10, 26);

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
            using (var p = new Pen(Theme.Bevel)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
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
