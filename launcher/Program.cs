using System;
using System.Windows.Forms;

namespace CoopLauncher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => MainForm.FileLog("CRASH " + e.ExceptionObject);
            Application.ThreadException += (s, e) => MainForm.FileLog("CRASH " + e.Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
