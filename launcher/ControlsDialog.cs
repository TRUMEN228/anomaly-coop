using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>
    /// "Управление": a drawn keyboard and mouse in the middle; every bound key gets a callout around it
    /// (top, bottom, left or right — the nearest edge that still has room) with a leader line to the key.
    /// Co-op keys are amber, the game's own (appdata\user.ltx of the chosen install) steel, keys used by both red.
    /// </summary>
    public sealed class ControlsDialog : ZoneDialog
    {
        const int U = 30, Gap = 4, W = 1360, H = 730, BarH = 40;
        const int TierH = 20, Tiers = 3, RowH = 20;

        sealed class Cap { public string Code, Label; public int Row; public RectangleF R; }
        sealed class Note
        {
            public string Text; public List<Cap> Keys = new List<Cap>(); public bool Coop, Clash;
            public PointF Anchor; public string Side; public RectangleF Box; public string Cat;
            public List<PointF[]> Paths = new List<PointF[]>();
        }
        readonly List<Cap> caps = new List<Cap>();
        readonly List<Note> notes = new List<Note>();
        readonly Dictionary<string, List<string>> game = new Dictionary<string, List<string>>();  // key -> game actions
        readonly Dictionary<string, string> coop = new Dictionary<string, string>();               // key -> co-op action
        readonly bool fromFile;
        RectangleF kb;               // keyboard + mouse bounds
        Cap hover;
        Note hoverNote;
        string cat = "Кооператив";
        List<Note> Shown => notes.Where(n => n.Cat == cat || (cat == "Кооператив" && n.Clash)).ToList();
        List<Note> shown = new List<Note>();
        readonly List<ZoneButton> tabs = new List<ZoneButton>();
        static readonly string[] Cats = { "Кооператив", "Движение", "Оружие", "Снаряжение", "Прочее" };

        // Anomaly action names (ui_keybinding.xml "exe" ids) in the game's own Russian wording
        static readonly string[][] GameActions =
        {
            // group, exe, name
            new[] { "Движение", "forward", "Вперёд" }, new[] { "Движение", "back", "Назад" },
            new[] { "Движение", "lstrafe", "Шаг влево" }, new[] { "Движение", "rstrafe", "Шаг вправо" },
            new[] { "Движение", "jump", "Прыжок" }, new[] { "Движение", "crouch", "Присесть" },
            new[] { "Движение", "accel", "Медленный шаг" }, new[] { "Движение", "sprint_toggle", "Бег" },
            new[] { "Движение", "llookout", "Выглянуть влево" }, new[] { "Движение", "rlookout", "Выглянуть вправо" },
            new[] { "Движение", "freelook", "Свободный обзор" },
            new[] { "Оружие", "wpn_fire", "Огонь" }, new[] { "Оружие", "wpn_zoom", "Прицел" },
            new[] { "Оружие", "wpn_reload", "Перезарядка" }, new[] { "Оружие", "wpn_func", "Подствольник" },
            new[] { "Оружие", "wpn_firemode_next", "Режим огня +" }, new[] { "Оружие", "wpn_firemode_prev", "Режим огня −" },
            new[] { "Оружие", "wpn_next", "Следующее оружие" }, new[] { "Оружие", "safemode", "Опустить оружие" },
            new[] { "Оружие", "custom13", "Глушитель" }, new[] { "Оружие", "custom19", "Бросить гранату" },
            new[] { "Оружие", "custom15", "Разрядить всё" },
            new[] { "Оружие", "wpn_1", "Нож" }, new[] { "Оружие", "wpn_2", "Пистолет" }, new[] { "Оружие", "wpn_3", "Основное оружие" },
            new[] { "Оружие", "wpn_4", "Гранаты" }, new[] { "Оружие", "wpn_5", "Бинокль" }, new[] { "Оружие", "wpn_6", "Болт" },
            new[] { "Снаряжение", "use", "Использовать / говорить" }, new[] { "Снаряжение", "inventory", "Инвентарь" },
            new[] { "Снаряжение", "active_jobs", "PDA" }, new[] { "Снаряжение", "custom20", "Карта" },
            new[] { "Снаряжение", "torch", "Фонарь" }, new[] { "Снаряжение", "night_vision", "ПНВ" },
            new[] { "Снаряжение", "show_detector", "Детектор" }, new[] { "Снаряжение", "custom14", "Счётчик Гейгера" },
            new[] { "Снаряжение", "drop", "Выбросить" }, new[] { "Снаряжение", "cam_zoom_in", "Протереть маску" },
            new[] { "Снаряжение", "cam_zoom_out", "Анимация предмета" },
            new[] { "Снаряжение", "quick_use_1", "Быстрый слот 1" }, new[] { "Снаряжение", "quick_use_2", "Быстрый слот 2" },
            new[] { "Снаряжение", "quick_use_3", "Быстрый слот 3" }, new[] { "Снаряжение", "quick_use_4", "Быстрый слот 4" },
            new[] { "Компаньоны", "custom18", "Меню компаньонов" }, new[] { "Компаньоны", "custom1", "Компаньоны: бой" },
            new[] { "Компаньоны", "custom2", "Компаньоны: движение" }, new[] { "Компаньоны", "custom3", "Компаньоны: идти" },
            new[] { "Компаньоны", "custom4", "Компаньоны: укрытие" }, new[] { "Компаньоны", "custom5", "Компаньоны: лут" },
            new[] { "Прочее", "custom6", "Скрыть HUD" }, new[] { "Прочее", "scores", "Статистика" },
            new[] { "Прочее", "custom7", "Рация" }, new[] { "Прочее", "custom8", "Плеер" },
            new[] { "Прочее", "quick_save", "Быстрое сохранение" }, new[] { "Прочее", "quick_load", "Быстрая загрузка" },
            new[] { "Прочее", "pause", "Пауза" }, new[] { "Прочее", "screenshot", "Скриншот" },
            new[] { "Прочее", "console", "Консоль" }, new[] { "Прочее", "quit", "Меню" },
            new[] { "Прочее", "custom17", "Отладка" },
        };

        // Anomaly 1.5.3 defaults, used when user.ltx is not there yet
        static readonly string Defaults = "forward kW|back kS|lstrafe kA|rstrafe kD|jump kSPACE|crouch kLCONTROL|accel kX|sprint_toggle kLSHIFT|" +
            "llookout kQ|rlookout kE|wpn_fire mouse1|wpn_zoom mouse2|wpn_reload kR|wpn_func kV|wpn_firemode_next k0|wpn_firemode_prev k9|" +
            "wpn_next kY|safemode kB|custom13 k7|custom15 kU|wpn_1 k1|wpn_2 k2|wpn_3 k3|wpn_4 k4|wpn_5 k5|wpn_6 k6|use kF|inventory kI|" +
            "active_jobs kP|torch kL|night_vision kN|show_detector kO|custom14 k8|drop kG|cam_zoom_in kT|cam_zoom_out kRBRACKET|" +
            "quick_use_1 kF1|quick_use_2 kF2|quick_use_3 kF3|quick_use_4 kF4|custom18 mouse3|custom1 kNUMPAD1|custom2 kNUMPAD2|" +
            "custom3 kNUMPAD3|custom4 kNUMPAD4|custom5 kNUMPAD5|custom6 kNUMPAD0|scores kTAB|quick_save kF5|quick_load kF9|pause kPAUSE|" +
            "screenshot kF12|console kGRAVE|quit kESCAPE|custom17 kF7";

        // numbered actions get one callout
        static readonly string[][] Groups =
        {
            new[] { "Слоты оружия 1–6", "Оружие", "wpn_1", "wpn_2", "wpn_3", "wpn_4", "wpn_5", "wpn_6" },
            new[] { "Быстрые слоты", "Снаряжение", "quick_use_1", "quick_use_2", "quick_use_3", "quick_use_4" },
            new[] { "Команды компаньонам", "Прочее", "custom1", "custom2", "custom3", "custom4", "custom5" },
            new[] { "Режим огня −/+", "Оружие", "wpn_firemode_prev", "wpn_firemode_next" },
            new[] { "Выглянуть влево/вправо", "Движение", "llookout", "rlookout" },
        };

        public ControlsDialog(Settings s) : base("Управление", W, H)
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
            var file = string.IsNullOrEmpty(s.GameDir) ? null : Path.Combine(s.GameDir, "appdata", "user.ltx");
            IEnumerable<string> binds;
            if (file != null && File.Exists(file))
            {
                fromFile = true;
                binds = File.ReadAllLines(file).Where(l => l.StartsWith("bind ")).Select(l => l.Substring(5));
            }
            else binds = Defaults.Split('|');
            var names = GameActions.ToDictionary(a => a[1], a => a[2]);
            var keyOf = new Dictionary<string, string>();
            foreach (var b in binds)
            {
                var p = b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2 || !names.ContainsKey(p[0])) continue;
                var key = Norm(p[1]);
                if (!game.TryGetValue(key, out var list)) game[key] = list = new List<string>();
                list.Add(p[0]);
                keyOf[p[0]] = key;
            }
            foreach (var a in KeysDialog.Actions) coop[s.Key(a[0]).ToUpperInvariant()] = a[1];
            BuildLayout();

            Cap C(string code) => caps.FirstOrDefault(c => c.Code == code);
            foreach (var a in KeysDialog.Actions)
            {
                var cap = C(s.Key(a[0]).ToUpperInvariant());
                if (cap != null) notes.Add(new Note { Text = a[1], Keys = { cap }, Coop = true, Clash = game.ContainsKey(cap.Code), Cat = "Кооператив" });
            }
            var done = new HashSet<string>();
            foreach (var g in Groups)
            {
                var keys = g.Skip(2).Where(keyOf.ContainsKey).Select(x => C(keyOf[x])).Where(c => c != null).Distinct().ToList();
                if (keys.Count < 2) continue;
                foreach (var x in g.Skip(2)) done.Add(x);
                notes.Add(new Note { Text = g[0], Keys = keys, Clash = keys.Any(k => coop.ContainsKey(k.Code)), Cat = g[1] });
            }
            foreach (var a in GameActions)
            {
                if (done.Contains(a[1]) || !keyOf.ContainsKey(a[1])) continue;
                var cap = C(keyOf[a[1]]);
                if (cap == null) continue;
                notes.Add(new Note { Text = a[2], Keys = { cap }, Clash = coop.ContainsKey(cap.Code), Cat = a[0] == "Компаньоны" ? "Прочее" : a[0] });
            }
            // one section at a time keeps the callouts readable
            int tx = 24;
            foreach (var c in Cats)
            {
                var b = Add(new ZoneButton(c) { Toggle = true }, tx, 52, 150, 30);
                b.Click += (o, e) => Show(c);
                tabs.Add(b);
                tx += 156;
            }
            Show(cat);

            var close = Add(new ZoneButton("Закрыть"), W - 128, H - 50, 104, 34);
            close.Click += (o, e) => Close();
        }

        public void Show(string c)
        {
            cat = c;
            foreach (var t in tabs) t.Checked = t.Text == c;
            shown = Shown;
            PlaceNotes();
            Invalidate();
        }

        // user.ltx "kNUMPAD1" / "mouse1" and launcher "NUMPAD1" -> one key code
        static string Norm(string k)
        {
            k = k.Trim().ToUpperInvariant();
            if (k.StartsWith("MOUSE")) return k;
            return k.StartsWith("K") ? k.Substring(1) : k;
        }

        const int RowGap = 14;           // room between key rows: leader lines run there

        void Row(int row, float y, float x, params object[] keys)
        {
            foreach (var k in keys)
            {
                if (k is float gap) { x += gap; continue; }
                var p = ((string)k).Split('~');
                float w = p.Length > 2 ? float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                float h = p.Length > 3 ? float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                caps.Add(new Cap { Code = p[0], Label = p[1], Row = row, R = new RectangleF(x, y, w * U + (w - 1) * Gap, h * U + (h - 1) * RowGap) });
                x += w * U + w * Gap;
            }
        }

        float mainMid;               // middle of the letter block: keys right of it go to the right column

        void BuildLayout()
        {
            float step = U + Gap, vstep = U + RowGap;
            float width = 22.8f * step + 14 + 84;                       // keyboard + mouse
            float left = (W - width) / 2, y0 = 62 + BarH + Tiers * TierH + 26;
            float Y(int r) => y0 + r * vstep;
            Row(0, Y(0), left, "ESCAPE~Esc", 0.6f * step, "F1~F1", "F2~F2", "F3~F3", "F4~F4", 0.4f * step, "F5~F5", "F6~F6", "F7~F7", "F8~F8",
                0.4f * step, "F9~F9", "F10~F10", "F11~F11", "F12~F12");
            float nav = left + 15.4f * step, pad = left + 18.8f * step;
            Row(0, Y(0), nav, "SYSRQ~PrtSc", "SCROLL~ScrLk", "PAUSE~Pause");
            Row(1, Y(1), left, "GRAVE~`", "1~1", "2~2", "3~3", "4~4", "5~5", "6~6", "7~7", "8~8", "9~9", "0~0", "MINUS~-", "EQUALS~=", "BACK~←~2");
            Row(2, Y(2), left, "TAB~Tab~1.5", "Q~Q", "W~W", "E~E", "R~R", "T~T", "Y~Y", "U~U", "I~I", "O~O", "P~P", "LBRACKET~[", "RBRACKET~]", "BACKSLASH~\\~1.5");
            Row(3, Y(3), left, "CAPITAL~Caps~1.75", "A~A", "S~S", "D~D", "F~F", "G~G", "H~H", "J~J", "K~K", "L~L", "SEMICOLON~;", "APOSTROPHE~'", "RETURN~Enter~2.25");
            Row(4, Y(4), left, "LSHIFT~Shift~2.25", "Z~Z", "X~X", "C~C", "V~V", "B~B", "N~N", "M~M", "COMMA~,", "PERIOD~.", "SLASH~/", "RSHIFT~Shift~2.75");
            Row(5, Y(5), left, "LCONTROL~Ctrl~1.25", "LWIN~Win~1.25", "LMENU~Alt~1.25", "SPACE~Пробел~6.25", "RMENU~Alt~1.25", "RWIN~Win~1.25", "APPS~Menu~1.25", "RCONTROL~Ctrl~1.25");
            Row(1, Y(1), nav, "INSERT~Ins", "HOME~Home", "PRIOR~PgUp");
            Row(2, Y(2), nav, "DELETE~Del", "END~End", "NEXT~PgDn");
            Row(4, Y(4), nav + step, "UP~↑");
            Row(5, Y(5), nav, "LEFT~←", "DOWN~↓", "RIGHT~→");
            Row(1, Y(1), pad, "NUMLOCK~Num", "DIVIDE~/", "MULTIPLY~*", "SUBTRACT~-");
            Row(2, Y(2), pad, "NUMPAD7~7", "NUMPAD8~8", "NUMPAD9~9", "ADD~+~1~2");
            Row(3, Y(3), pad, "NUMPAD4~4", "NUMPAD5~5", "NUMPAD6~6");
            Row(4, Y(4), pad, "NUMPAD1~1", "NUMPAD2~2", "NUMPAD3~3", "NUMPADENTER~Ent~1~2");
            Row(5, Y(5), pad, "NUMPAD0~0~2", "DECIMAL~.");
            float mx = pad + 4 * step + 14, my = Y(1) + 4, mh = U + vstep;
            caps.Add(new Cap { Code = "MOUSE1", Label = "ЛКМ", Row = -1, R = new RectangleF(mx, my, 34, mh * 0.55f) });
            caps.Add(new Cap { Code = "MOUSE3", Label = "", Row = -1, R = new RectangleF(mx + 36, my + 8, 12, mh * 0.3f) });
            caps.Add(new Cap { Code = "MOUSE2", Label = "ПКМ", Row = -1, R = new RectangleF(mx + 50, my, 34, mh * 0.55f) });
            kb = new RectangleF(left, y0, width, Y(5) + U - y0);
            mainMid = left + 7.5f * step;
        }

        string SideOf(PointF a, Cap k)
        {
            if (k.Row < 0 || k.R.X > kb.Left + 15 * (U + Gap)) return "right"; // mouse, arrows, numpad
            var d = new Dictionary<string, float>
            {
                ["top"] = a.Y - kb.Top, ["bottom"] = kb.Bottom - a.Y,
                ["left"] = (a.X - kb.Left) * 0.5f, ["right"] = (kb.Right - a.X) * 0.5f,
            };
            return d.OrderBy(kv => kv.Value).First().Key;
        }

        // each callout goes to the side its key belongs to; labels are spread along that side and every
        // leader line is routed through the gaps between key rows instead of across the keys
        void PlaceNotes()
        {
            var f = Theme.Body(8.5f);
            float sideTop = 52 + BarH, sideBottom = H - 100;
            foreach (var n in shown)
            {
                n.Paths.Clear();
                float x = n.Keys.Average(k => k.R.X + k.R.Width / 2), y = n.Keys.Average(k => k.R.Y + k.R.Height / 2);
                n.Anchor = new PointF(x, y);
                n.Side = SideOf(n.Anchor, n.Keys[0]);
                var sz = TextRenderer.MeasureText(n.Text, f);
                n.Box = new RectangleF(0, 0, sz.Width + 12, TierH - 4);
            }

            foreach (var side in new[] { "top", "bottom" })
            {
                var right = new float[Tiers];
                for (int t = 0; t < Tiers; t++) right[t] = 16;
                foreach (var n in shown.Where(n => n.Side == side).OrderBy(n => n.Anchor.X))
                {
                    int best = -1; float bestX = 0;
                    float want = n.Anchor.X - n.Box.Width / 2;
                    for (int t = 0; t < Tiers; t++)
                    {
                        float x = Math.Max(want, right[t] + 10);
                        if (x - want < 30) { best = t; bestX = x; break; }
                        if (best < 0 || x < bestX) { best = t; bestX = x; }
                    }
                    right[best] = bestX + n.Box.Width;
                    float y = side == "top" ? kb.Top - 12 - (best + 1) * TierH : kb.Bottom + 14 + best * TierH;
                    n.Box = new RectangleF(bestX, y, n.Box.Width, n.Box.Height);
                    // out of the label, a jog just outside the keyboard, then straight onto the key
                    float lane = side == "top" ? kb.Top - 4 - best * 3 : kb.Bottom + 4 + best * 3;
                    var port = Port(n);
                    foreach (var k in n.Keys)
                    {
                        float kx = k.R.X + k.R.Width / 2, ky = side == "top" ? k.R.Y : k.R.Bottom;
                        n.Paths.Add(new[] { port, new PointF(port.X, lane), new PointF(kx, lane), new PointF(kx, ky) });
                    }
                }
            }

            var lanes = new Dictionary<string, int>();                     // "row|side" -> lines already in that gap
            foreach (var side in new[] { "left", "right" })
            {
                var list = shown.Where(n => n.Side == side)
                    .OrderBy(n => n.Keys[0].Row < 0 ? 0 : n.Keys[0].Row).ThenBy(n => side == "left" ? n.Anchor.X : -n.Anchor.X).ToList();
                float total = list.Count * RowH;
                float y = Math.Max(sideTop, Math.Min(sideBottom - total, kb.Top + kb.Height / 2 - total / 2));
                for (int i = 0; i < list.Count; i++)
                {
                    var n = list[i];
                    float x = side == "left" ? kb.Left - 30 - n.Box.Width : kb.Right + 30;
                    x = side == "left" ? Math.Max(16, x) : Math.Min(W - 16 - n.Box.Width, x);
                    n.Box = new RectangleF(x, y + i * RowH, n.Box.Width, n.Box.Height);
                    var port = Port(n);
                    float margin = side == "left" ? kb.Left - 8 - (list.Count - i) * 1.6f : kb.Right + 8 + (list.Count - i) * 1.6f;
                    foreach (var k in n.Keys)
                    {
                        float kx = k.R.X + k.R.Width / 2;
                        if (k.Row < 0)
                        {   // mouse buttons: straight across from the right column
                            float ky = k.R.Y + k.R.Height / 2;
                            n.Paths.Add(new[] { port, new PointF(margin, port.Y), new PointF(margin, ky), new PointF(k.R.Right, ky) });
                            continue;
                        }
                        var key = k.Row + "|" + side;
                        lanes.TryGetValue(key, out var lane);
                        lanes[key] = lane + 1;
                        float gy = k.R.Y - RowGap + 3 + (lane % 5) * 2.2f;      // inside the gap above the key's row
                        n.Paths.Add(new[] { port, new PointF(margin, port.Y), new PointF(margin, gy), new PointF(kx, gy), new PointF(kx, k.R.Y) });
                    }
                }
            }
        }

        string Describe(Cap c)
        {
            var parts = new List<string>();
            if (coop.TryGetValue(c.Code, out var a)) parts.Add("Кооператив: " + a);
            if (game.TryGetValue(c.Code, out var g))
            {
                var names = GameActions.ToDictionary(x => x[1], x => x[2]);
                parts.Add("Anomaly: " + string.Join(", ", g.Select(x => names[x])));
            }
            var label = c.Code.StartsWith("NUMPAD") ? "Num" + c.Label : c.Label == "" ? "Колесо" : c.Label;
            if (parts.Count == 0) return label + " — не назначена";
            var s = label + " — " + string.Join(" · ", parts);
            if (parts.Count == 2) s += "   ⚠ сработает и то, и другое";
            return s;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pt = new PointF(e.X / S, e.Y / S);
            var h = caps.FirstOrDefault(c => c.R.Contains(pt));
            var hn = h == null ? shown.FirstOrDefault(n => n.Box.Contains(pt)) : null;
            if (h != hover || hn != hoverNote) { hover = h; hoverNote = hn; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = null; hoverNote = null; Invalidate(); }

        RectangleF Sc(RectangleF r) => new RectangleF(r.X * S, r.Y * S, r.Width * S, r.Height * S);
        PointF Sc(PointF p) => new PointF(p.X * S, p.Y * S);

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Color Blend(Color a, Color b, float t) => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        static readonly Color GameColor = Color.FromArgb(150, 162, 120);
        Color NoteColor(Note n) => n.Clash ? Theme.Danger : n.Coop ? Theme.Amber : GameColor;
        bool Lit(Note n) => hoverNote == n || (hover != null && n.Keys.Contains(hover));
        bool InView(Cap c) => shown.Any(n => n.Keys.Contains(c));

        // the label's edge facing the keyboard
        static PointF Port(Note n)
        {
            switch (n.Side)
            {
                case "top": return new PointF(n.Box.X + n.Box.Width / 2, n.Box.Bottom);
                case "bottom": return new PointF(n.Box.X + n.Box.Width / 2, n.Box.Y);
                case "left": return new PointF(n.Box.Right, n.Box.Y + n.Box.Height / 2);
                default: return new PointF(n.Box.X, n.Box.Y + n.Box.Height / 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var m1 = caps.First(c => c.Code == "MOUSE1").R;
            using (var body = Round(Sc(new RectangleF(m1.X - 4, m1.Y - 4, 92, 120)), 30 * S))
            using (var b = new SolidBrush(Theme.PlateLow)) using (var p = new Pen(Theme.Bevel)) { g.FillPath(b, body); g.DrawPath(p, body); }

            var lit = new HashSet<Cap>(shown.Where(Lit).SelectMany(n => n.Keys));
            if (hover != null) lit.Add(hover);
            var small = Theme.Label(7.5f);
            foreach (var c in caps)
            {
                bool co = coop.ContainsKey(c.Code), ga = game.ContainsKey(c.Code);
                var r = Sc(c.R);
                Color top = Theme.Plate, bottom = Theme.PlateLow, text = Theme.TextDim;
                if (ga) { top = Color.FromArgb(70, 76, 58); bottom = Color.FromArgb(44, 48, 36); text = Theme.Text; }
                if (co) { top = Color.FromArgb(150, 108, 36); bottom = Color.FromArgb(96, 68, 22); text = Color.FromArgb(255, 236, 196); }
                if (co && ga) { top = Color.FromArgb(150, 62, 40); bottom = Color.FromArgb(98, 36, 24); text = Color.FromArgb(255, 222, 210); }
                if ((co || ga) && !InView(c) && c != hover) { top = Blend(top, Theme.PlateLow, 0.6f); bottom = Blend(bottom, Theme.PlateLow, 0.6f); text = Blend(text, Theme.TextDim, 0.6f); }
                using (var path = Round(r, 4 * S))
                {
                    using (var b = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(b, path);
                    using (var p = new Pen(lit.Contains(c) ? Theme.Amber : Theme.Edge, lit.Contains(c) ? 2 : 1)) g.DrawPath(p, path);
                }
                if (c.Label != "")
                    TextRenderer.DrawText(g, c.Label, small, Rectangle.Round(r), text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // leader lines along their routed paths, an arrow head on the key
            foreach (var n in shown.OrderBy(Lit))
            {
                bool on = Lit(n);
                using (var p = new Pen(Color.FromArgb(on ? 255 : 150, NoteColor(n)), on ? 2f : 1.1f) { CustomEndCap = new AdjustableArrowCap(3.5f, 4.5f), LineJoin = LineJoin.Round })
                    foreach (var path in n.Paths) g.DrawLines(p, path.Select(Sc).ToArray());
            }

            var f = Theme.Body(8.5f);
            foreach (var n in shown)
            {
                var r = Sc(n.Box);
                bool on = Lit(n);
                using (var b = new SolidBrush(on ? Color.FromArgb(80, 56, 20) : Color.FromArgb(225, 18, 18, 14))) g.FillRectangle(b, r);
                using (var p = new Pen(Color.FromArgb(on ? 255 : 130, NoteColor(n)))) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                TextRenderer.DrawText(g, n.Text, f, Rectangle.Round(r), n.Clash ? Color.FromArgb(255, 170, 150) : n.Coop ? Theme.Amber : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // what the hovered key does, the colours, where the bindings come from
            var info = new Rectangle(X(24), X(H - 90), X(W - 48), X(26));
            using (var b = new SolidBrush(Theme.Screen)) g.FillRectangle(b, info);
            using (var p = new Pen(Theme.Bevel)) g.DrawRectangle(p, info);
            string line = hover != null ? Describe(hover)
                : hoverNote != null ? string.Join("   ", hoverNote.Keys.Select(Describe))
                : "Наведите курсор на клавишу или подпись. Разделы — кнопками сверху";
            TextRenderer.DrawText(g, "  " + line, Theme.Body(9.5f), info, hover != null || hoverNote != null ? Theme.Amber : Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int lx = X(24), ly = X(H - 52);
            var legend = new[] { Tuple.Create(Theme.Amber, "кооператив"), Tuple.Create(GameColor, "Anomaly"),
                Tuple.Create(Theme.Danger, "занята и игрой, и кооперативом: сработает и то, и другое") };
            foreach (var item in legend)
            {
                using (var b = new SolidBrush(item.Item1)) g.FillRectangle(b, lx, ly + X(4), X(12), X(12));
                var sz = TextRenderer.MeasureText(g, item.Item2, f);
                TextRenderer.DrawText(g, item.Item2, f, new Point(lx + X(18), ly + X(1)), Theme.Text);
                lx += X(18) + sz.Width + X(18);
            }
            var note = fromFile ? "Клавиши игры — из её настроек (appdata\\user.ltx), кооператива — из «Опций» лаунчера."
                                : "Клавиши игры — по умолчанию Anomaly, кооператива — из «Опций» лаунчера.";
            TextRenderer.DrawText(g, note, f, new Point(X(24), ly + X(22)), Theme.TextDim);
        }
    }
}
