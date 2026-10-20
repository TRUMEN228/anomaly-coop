using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>Choose the in-game co-op keys. Values are DirectInput names (DIK_*) the mod understands.</summary>
    public sealed class KeysDialog : Form
    {
        public static readonly string[][] Actions =
        {
            new[] { "tp", "Телепорт к другу" },
            new[] { "sync", "Отправить сессию (хост)" },
            new[] { "load", "Загрузить сессию (гость)" },
            new[] { "status", "Статус подключения" },
        };

        // keys Anomaly does not use by default (F5/F9 are quicksave/quickload, F1-F4 slots)
        static readonly string[] Choices =
        {
            "NUMPAD0", "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5", "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9",
            "F6", "F7", "F8", "F10", "F11", "F12", "INSERT", "HOME", "PRIOR", "NEXT", "END", "DELETE",
            "LBRACKET", "RBRACKET", "SEMICOLON", "APOSTROPHE", "BACKSLASH", "PERIOD", "SLASH",
        };

        public static string Pretty(string key) =>
            key.StartsWith("NUMPAD") ? "Num" + key.Substring(6) :
            key == "PRIOR" ? "PgUp" : key == "NEXT" ? "PgDn" : key == "LBRACKET" ? "[" : key == "RBRACKET" ? "]" :
            key == "SEMICOLON" ? ";" : key == "APOSTROPHE" ? "'" : key == "BACKSLASH" ? "\\" : key == "PERIOD" ? "." :
            key == "SLASH" ? "/" : key == "INSERT" ? "Ins" : key == "DELETE" ? "Del" : key;

        readonly ComboBox[] boxes = new ComboBox[Actions.Length];

        public KeysDialog(Settings s)
        {
            Text = "Настройки";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 100 + Actions.Length * 32);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(225, 225, 225);

            for (int i = 0; i < Actions.Length; i++)
            {
                Controls.Add(new Label { Text = Actions[i][1], Left = 14, Top = 17 + i * 32, AutoSize = true });
                var cb = new ComboBox { Left = 210, Top = 14 + i * 32, Width = 115, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(28, 28, 28), ForeColor = ForeColor };
                cb.Items.AddRange(Choices.Select(Pretty).ToArray<object>());
                cb.SelectedIndex = System.Array.IndexOf(Choices, s.Key(Actions[i][0]));
                if (cb.SelectedIndex < 0) cb.SelectedIndex = 0;
                boxes[i] = cb;
                Controls.Add(cb);
            }
            var upnp = new CheckBox { Text = "Хост: открывать порт на роутере (UPnP)", Checked = s.Upnp, Left = 14, Top = 18 + Actions.Length * 32, AutoSize = true };
            Controls.Add(upnp);
            var ok = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, Left = 150, Top = 58 + Actions.Length * 32, Width = 85, FlatStyle = FlatStyle.Flat };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Left = 240, Top = 58 + Actions.Length * 32, Width = 85, FlatStyle = FlatStyle.Flat };
            Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            ok.Click += (o, e) =>
            {
                var chosen = boxes.Select(b => Choices[b.SelectedIndex]).ToArray();
                if (chosen.Distinct().Count() != chosen.Length)
                {
                    MessageBox.Show(this, "Одна клавиша назначена на два действия. Выберите разные.", Text);
                    DialogResult = DialogResult.None;
                    return;
                }
                for (int i = 0; i < Actions.Length; i++) s.SetKey(Actions[i][0], chosen[i]);
                s.Upnp = upnp.Checked;
            };
        }
    }
}
