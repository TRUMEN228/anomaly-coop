# The co-op test both games play together (zz_coop_scenario.script), watched by a person: two real games in the
# test sandbox side by side, a banner in each says what each step does and what to look for.
# Results: local\test{A,B}\appdata\coop_qa.txt, printed at the end; then both games close.
# The run is recorded (ffmpeg, only the two game windows, side by side): local\qa_rec\run.mp4; frames of both
# windows every 2 s: local\qa_shots (and run.webp made of them).
# -Mode travel: the travel test instead (zz_coop_travel.script): the host goes to another level, then to the main
# menu; what the guest does then is checked here, in its log
# usage: scenario.ps1 [-Minutes 8] [-Mode scenario|travel]
param([int]$Minutes = 8, [string]$Mode = "scenario")
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
for name in ('zz_coop_scenario', 'zz_coop_travel', 'zz_coop_syntax'):
    src = open(r'$r\tests\\' + name + '.script', encoding='utf-8').read()
    for n in 'AB':
        open(r'$r\local\test' + n + r'\gamedata\scripts\\' + name + '.script', 'wb').write(src.encode('cp1251'))
"@
$flag = if ($Mode -eq 'travel') { 'coop_travel.txt' } else { 'coop_scenario.txt' }
foreach ($n in 'A', 'B') {
    $a = "$r\local\test$n\appdata"
    Remove-Item "$a\coop_scenario.txt", "$a\coop_travel.txt", "$a\coop_qt_phase.txt", "$a\coop_qt_loads.txt" -ErrorAction SilentlyContinue
    "1" | Set-Content "$a\$flag"
    Remove-Item "$a\coop_qa.txt", "$a\coop_qa_steps.txt" -ErrorAction SilentlyContinue
}

& "$r\tests\duo_link.ps1" -NoLauncher -Save coop_sandbox

# side by side: the host on the left, the guest on the right (each on a screen of its own when there are two)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool a);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  public struct RECT { public int L, T, R, B; } }
"@
# real pixels from here on: the window rectangles are what the recording crops
[Win]::SetProcessDPIAware() | Out-Null
$screens = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X }
$left = $screens[0].Bounds
$right = $screens[-1].Bounds
$win = @{}
foreach ($n in 'A', 'B') {
    $p = Get-Process AnomalyDX11AVX* -ErrorAction SilentlyContinue | ? { $_.Path -like "$r\local\test$n\*" } | Select -First 1
    if ($p -and $p.MainWindowHandle -ne 0) {
        $win[$n] = $p.MainWindowHandle
        $x = if ($n -eq 'A') { $left.X } elseif ($screens.Count -gt 1) { $right.X } else { [Math]::Max(0, $right.Right - 1300) }
        [Win]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $x, $right.Y, 0, 0, 0x0001 -bor 0x0004) | Out-Null
    }
}
Start-Sleep 1

# the recording: just the two windows' rectangles (nothing else of the desktop), side by side
$ffmpeg = 'S:\Tools\ffmpeg\bin\ffmpeg.exe'
$rec = "$r\local\qa_rec"
New-Item -ItemType Directory -Force $rec | Out-Null
Remove-Item "$rec\run.mp4" -ErrorAction SilentlyContinue
$recorder = $null
if ((Test-Path $ffmpeg) -and $win['A'] -and $win['B']) {
    $in = @()
    foreach ($n in 'A', 'B') {
        # the visible frame (the window rectangle also holds an invisible shadow border: a strip of whatever is beside)
        $rc = New-Object Win+RECT
        if ([Win]::DwmGetWindowAttribute($win[$n], 9, [ref]$rc, 16) -ne 0) { [Win]::GetWindowRect($win[$n], [ref]$rc) | Out-Null }
        $w = ($rc.R - $rc.L) - (($rc.R - $rc.L) % 2); $h = ($rc.B - $rc.T) - (($rc.B - $rc.T) % 2)
        $in += "-f gdigrab -framerate 15 -offset_x $($rc.L) -offset_y $($rc.T) -video_size ${w}x$h -draw_mouse 0 -i desktop"
    }
    $ffargs = "-hide_banner -loglevel error -y $($in -join ' ') -filter_complex `"[0]scale=1280:-2[a];[1]scale=1280:-2[b];[a][b]hstack`" " +
        "-c:v libx264 -preset veryfast -crf 26 -pix_fmt yuv420p -movflags +frag_keyframe+empty_moov `"$rec\run.mp4`""
    $psi = New-Object System.Diagnostics.ProcessStartInfo $ffmpeg, $ffargs
    $psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.CreateNoWindow = $true
    $recorder = [System.Diagnostics.Process]::Start($psi)
    $recStart = [DateTimeOffset]::Now.ToUnixTimeSeconds()
    "recording both windows: $rec\run.mp4"
}

# what each window shows, every 2 s (even when covered): local\qa_shots\<seconds>_<step>_<A|B>.png
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
$focusFile = "$r\local	estAppdata\coop_qa_focus.txt"
Remove-Item $focusFile -ErrorAction SilentlyContinue
$focused = ""
while (((Get-Date) - $t0).TotalMinutes -lt $Minutes) {
    Start-Sleep 2
    # the scenario asks for a game's window to be the active one (a game without it closes a talk as it opens)
    $want = (Get-Content $focusFile -ErrorAction SilentlyContinue | Select -First 1)
    if ($want -and $want -ne $focused -and $win[$want]) { [Win]::SwitchToThisWindow($win[$want], $true); $focused = $want }
    $lines = @(Get-Content $qa -ErrorAction SilentlyContinue)
    for ($i = $seen; $i -lt $lines.Count; $i++) { "  " + $lines[$i] }
    $seen = $lines.Count
    if ($lines.Count -gt 0 -and $lines[-1] -match '^QA (\S+) ') { $step = $Matches[1] }
    $sec = [int]((Get-Date) - $t0).TotalSeconds
    foreach ($n in $win.Keys) { try { Shot $win[$n] ("$shots\{0:d4}_{1}_{2}.jpg" -f $sec, $step, $n) } catch { } }
    if ($lines -match '^QA end ') { break }
    # travel: the host has left for the menu; the guest's part takes a while (its game ends, the menu comes)
    if ($Mode -eq 'travel' -and $lines -match '^QA menu_go ') {
        if (-not $menuAt) { $menuAt = Get-Date }
        elseif (((Get-Date) - $menuAt).TotalSeconds -gt 40) { break }
    }
}
if ($Mode -eq 'travel') {
    # the guest after the host left: its game ended with a window saying why, and nothing loaded after
    $gl = @(Get-Content "$r\local\testB\appdata\coop_log.txt" -Encoding 1251 -ErrorAction SilentlyContinue)
    $k = -1
    for ($i = 0; $i -lt $gl.Count; $i++) { if ($gl[$i] -match 'kicked: ') { $k = $i } }
    $res = @()
    if ($k -lt 0) { $res += "QA menu_kick FAIL the guest's game did not end" }
    else {
        $res += "QA menu_kick PASS " + ($gl[$k] -replace '^\[\d+\]\s*', '')
        $loadsAfter = @($gl[($k + 1)..($gl.Count - 1)] | ? { $_ -match '\] load|loading the host|=== Anomaly' })
        $res += if ($loadsAfter.Count -eq 0) { "QA menu_noload PASS the guest loaded nothing after" } else { "QA menu_noload FAIL " + ($loadsAfter -join ' / ') }
        $shown = @($gl[($k + 1)..($gl.Count - 1)] | ? { $_ -match 'kick window shown' })
        $res += if ($shown.Count -gt 0) { "QA menu_window PASS the main menu showed why" } else { "QA menu_window FAIL the main menu showed no window" }
    }
    $res | % { "  $_" }
    $res | Add-Content "$r\local\testA\appdata\coop_qa.txt"
}
foreach ($n in 'A', 'B') {
    Remove-Item "$r\local\test$n\appdata\coop_scenario.txt", "$r\local\test$n\appdata\coop_travel.txt" -ErrorAction SilentlyContinue
    "--- game $n (" + $(if ($n -eq 'A') { 'host' } else { 'guest' }) + ")"
    Get-Content "$r\local\test$n\appdata\coop_syntax.txt" -ErrorAction SilentlyContinue | ? { $_ -notmatch ': ok$' } | % { "SYNTAX $_" }
    Get-Content "$r\local\test$n\appdata\coop_qa.txt" -ErrorAction SilentlyContinue
    Get-Content "$r\local\test$n\appdata\coop_log.txt" -ErrorAction SilentlyContinue | Select-String "failed|mirror:|not at the host" | Select -Last 12 | % Line
}
# the recording ends ("q" lets ffmpeg finish the file), then both games (and the relay) close
if ($recorder) {
    try { $recorder.StandardInput.Write("q"); $recorder.StandardInput.Close() } catch { }
    if (-not $recorder.WaitForExit(20000)) { $recorder.Kill() }
    $f = Get-Item "$rec\run.mp4" -ErrorAction SilentlyContinue
    if ($f) { "recording: $($f.FullName) ($([int]($f.Length / 1MB)) MB)" }
}
& "$r\tests\duo.ps1" -Stop | Out-Null
"games closed"

# frames of the recording 1, 3 and 6 s after each step's banner went up (the host wrote when): local\qa_rec\frames,
# both games side by side; frames.txt says which step each is
$steps = "$r\local\testA\appdata\coop_qa_steps.txt"
$fr = "$rec\frames"
if ($recorder -and (Test-Path "$rec\run.mp4") -and (Test-Path $steps)) {
    Remove-Item $fr -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $fr | Out-Null
    $index = @()
    $i = 0
    foreach ($line in Get-Content $steps -Encoding 1251) {
        $t, $text = $line -split '\|', 2
        $i++
        foreach ($d in 1, 3, 6) {
            $at = [long]$t - $recStart + $d
            if ($at -lt 0) { continue }
            $name = "{0:d2}_{1}s.jpg" -f $i, $d
            & $ffmpeg -hide_banner -loglevel error -y -ss $at -i "$rec\run.mp4" -frames:v 1 -vf scale=1600:-2 "$fr\$name"
            if (Test-Path "$fr\$name") { $index += "$name  ($at s)  $text" }
        }
    }
    $index | Set-Content "$fr\frames.txt" -Encoding utf8
    "frames: $fr ($($index.Count))"
}
python -I "$r\tests\shots_video.py" $shots "$shots\run.webp" 500
