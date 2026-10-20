# QA: an old launcher (v1.0 build in local\updtest) finds the latest GitHub release, updates itself and restarts.
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class Wn {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static IntPtr Find(IntPtr parent, string pattern) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h, l) => { var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (IsWindowVisible(h) && System.Text.RegularExpressions.Regex.IsMatch(sb.ToString(), pattern)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; } }
"@
$dir = 'D:\Projects\stalker_coop\local\updtest'; $exe = "$dir\CoopLauncher.exe"
"before: " + (Get-Item $exe).VersionInfo.FileVersion
$p = Start-Process $exe -WorkingDirectory $dir -PassThru
$btn = [IntPtr]::Zero
for ($i = 0; $i -lt 30 -and $btn -eq [IntPtr]::Zero; $i++) { Start-Sleep 1; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $btn = [Wn]::Find($p.MainWindowHandle, ' v\d+\.\d+$') } }
if ($btn -eq [IntPtr]::Zero) { "update button did not appear"; Stop-Process -Id $p.Id; exit 1 }
[Wn]::PostMessage($btn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # BM_CLICK
for ($i = 0; $i -lt 40 -and -not $p.HasExited; $i++) { Start-Sleep 1 }
"old launcher exited: $($p.HasExited)"
Start-Sleep 5
"after: " + (Get-Item $exe).VersionInfo.FileVersion
$new = Get-CimInstance Win32_Process -Filter "Name='CoopLauncher.exe'" | Where-Object { $_.ExecutablePath -eq $exe }
"restarted: $([bool]$new)"
$new | ForEach-Object { Stop-Process -Id $_.ProcessId }
