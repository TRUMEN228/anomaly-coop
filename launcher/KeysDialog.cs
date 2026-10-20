using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoopLauncher
{
    /// <summary>The in-game co-op actions and the keys a player may bind (DirectInput names the mod understands).
    /// The dialog itself is OptionsDialog.</summary>
    public static class KeysDialog
    {
        public static readonly string[][] Actions =
        {
            new[] { "tp", "Телепорт к другу" },
            new[] { "sync", "Отправить сессию (хост)" },
            new[] { "load", "Загрузить сессию (гость)" },
            new[] { "status", "Статус подключения" },
        };

        // keys Anomaly does not use by default (F5/F9 are quicksave/quickload, F1-F4 slots)
        public static readonly string[] Choices =
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

    }
}
