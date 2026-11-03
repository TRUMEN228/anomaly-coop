# The co-op test both games play together (zz_coop_scenario.script), watched by a person: two real games in the
# test sandbox side by side, a banner in each says what each step does and what to look for.
# Results: local\test{A,B}\appdata\coop_qa.txt, printed at the end; then both games close.
# usage: scenario.ps1 [-Minutes 8]
param([int]$Minutes = 8)
$r = 'D:\Projects\stalker_coop'

python "$r\build.py" --scripts-only | Out-Null
# the test sandbox, money of one's own (the money step), the scenario in both games
python -c @"
import re
p = r'$r\local\testA\appdata\coop_keys.txt'
s = open(p, 'rb').read().decode('cp1251')
for k, v in (('world', 'sandbox'), ('money', '0')):
    s = re.sub(r'(?m)^' + k + r'=.*$', k + '=' + v, s) if re.search(r'(?m)^' + k + '=', s) else s.rstrip('\n') + '\n' + k + '=' + v + '\n'
open(p, 'wb').write(s.encode('cp1251'))
src = open(r'$r\tests\zz_coop_scenario.script', encoding='utf-8').read()
for n in 'AB':
    open(r'$r\local\test' + n + r'\gamedata\scripts\zz_coop_scenario.script', 'wb').write(src.encode('cp1251'))
"@
foreach ($n in 'A', 'B') {
    "1" | Set-Content "$r\local\test$n\appdata\coop_scenario.txt"
    Remove-Item "$r\local\test$n\appdata\coop_qa.txt" -ErrorAction SilentlyContinue
}

& "$r\tests\duo_link.ps1" -NoLauncher -Save coop_sandbox

# side by side: the host on the left, the guest on the right
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public struct RECT { public int L, T, R, B; } }
"@
$sw = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Width
$win = @{}
foreach ($n in 'A', 'B') {
    $p = Get-Process AnomalyDX11AVX* -ErrorAction SilentlyContinue | ? { $_.Path -like "$r\local\test$n\*" } | Select -First 1
    if ($p -and $p.MainWindowHandle -ne 0) {
        $win[$n] = $p.MainWindowHandle
        $x = if ($n -eq 'A') { 0 } else { [Math]::Max(0, $sw - 1280) }
        [Win]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $x, 0, 0, 0, 0x0001 -bor 0x0004) | Out-Null
    }
}

# what each window shows, every few seconds (even when covered): local\qa_shots\<seconds>_<step>_<A|B>.png
$shots = "$r\local\qa_shots"
Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $shots | Out-Null
function Shot($h, $file) {
    $rc = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$rc) | Out-Null
    if ($rc.R -le $rc.L) { return }
    $bmp = New-Object System.Drawing.Bitmap ($rc.R - $rc.L), ($rc.B - $rc.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [Win]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    # half size is enough to see what is on screen
    $small = New-Object System.Drawing.Bitmap $bmp, ([int]($bmp.Width / 2)), ([int]($bmp.Height / 2))
    $small.Save($file, [System.Drawing.Imaging.ImageFormat]::Jpeg); $small.Dispose(); $bmp.Dispose()
}

"scenario running: watch both windows"
$t0 = Get-Date
$qa = "$r\local\testA\appdata\coop_qa.txt"
$seen = 0
$step = "wait"
while (((Get-Date) - $t0).TotalMinutes -lt $Minutes) {
    Start-Sleep 4
    $lines = @(Get-Content $qa -ErrorAction SilentlyContinue)
    for ($i = $seen; $i -lt $lines.Count; $i++) { "  " + $lines[$i] }
    $seen = $lines.Count
    if ($lines.Count -gt 0 -and $lines[-1] -match '^QA (\S+) ') { $step = $Matches[1] }
    $sec = [int]((Get-Date) - $t0).TotalSeconds
    foreach ($n in $win.Keys) { try { Shot $win[$n] ("$shots\{0:d4}_{1}_{2}.jpg" -f $sec, $step, $n) } catch { } }
    if ($lines -match '^QA end ') { break }
}
foreach ($n in 'A', 'B') {
    Remove-Item "$r\local\test$n\appdata\coop_scenario.txt" -ErrorAction SilentlyContinue
    "--- game $n (" + $(if ($n -eq 'A') { 'host' } else { 'guest' }) + ")"
    Get-Content "$r\local\test$n\appdata\coop_qa.txt" -ErrorAction SilentlyContinue
    Get-Content "$r\local\test$n\appdata\coop_log.txt" -ErrorAction SilentlyContinue | Select-String "failed|mirror:|not at the host" | Select -Last 12 | % Line
}
# both games (and the relay) close when the test is over
& "$r\tests\duo.ps1" -Stop | Out-Null
"games closed"
