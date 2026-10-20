# Screenshot the launcher's child windows: Options and the folder picker.
param([string]$Exe = 'D:\Projects\stalker_coop\local\uitest\CoopLauncher.exe')
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class Wn {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static IntPtr Child(IntPtr parent, string pattern) { IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h, l) => { var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (System.Text.RegularExpressions.Regex.IsMatch(sb.ToString(), pattern)) { found = h; return false; } return true; }, IntPtr.Zero); return found; }
  public static IntPtr Top(int pid, string pattern) { IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (p == pid && IsWindowVisible(h) && System.Text.RegularExpressions.Regex.IsMatch(sb.ToString(), pattern)) { found = h; return false; } return true; }, IntPtr.Zero); return found; }
  public static void Click(IntPtr h) { PostMessage(h, 0x201, (IntPtr)1, (IntPtr)0x00050005); PostMessage(h, 0x202, IntPtr.Zero, (IntPtr)0x00050005); } }
"@
Add-Type -AssemblyName System.Drawing
Add-Type 'using System; using System.Runtime.InteropServices; public class PW { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[PW]::SetProcessDPIAware() | Out-Null
function Shot($h, $out) { $r = New-Object PW+R; [PW]::GetWindowRect($h, [ref]$r) | Out-Null; $bmp = New-Object System.Drawing.Bitmap ($r.Rt - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [PW]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $bmp.Save($out) }
$p = Start-Process $Exe -WorkingDirectory (Split-Path $Exe) -PassThru; Start-Sleep 4; $p.Refresh()
[Wn]::Click([Wn]::Child($p.MainWindowHandle, '^\u041e\u043f\u0446\u0438\u0438$')); Start-Sleep 2      # "Опции"
$d = [Wn]::Top($p.Id, '^\u041e\u043f\u0446\u0438\u0438$'); if ($d -ne 0) { Shot $d "$env:TEMP\claude\dlg_options.png"; [Wn]::PostMessage($d, 0x10, 0, 0) | Out-Null } else { "options dialog not found" }
Start-Sleep 1
[Wn]::Click([Wn]::Child($p.MainWindowHandle, '^\.\.\.$')); Start-Sleep 2
$d = [Wn]::Top($p.Id, '^\u041f\u0430\u043f\u043a\u0430'); if ($d -ne 0) { Shot $d "$env:TEMP\claude\dlg_folder.png"; [Wn]::PostMessage($d, 0x10, 0, 0) | Out-Null } else { "folder dialog not found" }
Start-Sleep 1; Stop-Process -Id $p.Id
