using System;
using System.Linq;
using System.Windows.Forms;

namespace CoopLauncher
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var test = args.FirstOrDefault(a => a.StartsWith("--selftest-"));
            if (test != null) return SelfTest.Run(test);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => MainForm.FileLog("CRASH " + e.ExceptionObject);
            Application.ThreadException += (s, e) => MainForm.FileLog("CRASH " + e.Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
