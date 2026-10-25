# Activate a freshly loaded test game window once (Anomaly stays paused until its window has been active),
# then give focus back to whatever the user had in front. usage: wake_game.ps1 -ProcId <pid>
# or: wake_game.ps1 -Dir <test install> waits (up to 4 min) for that install's game to load, then wakes it
param([int]$ProcId = 0, [string]$Dir = "")
Add-Type 'using System; using System.Runtime.InteropServices; public class Fg { [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool a); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); }'
if ($Dir) {
    for ($i = 0; $i -lt 240; $i++) {
        $p = Get-Process AnomalyDX11AVX* -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Dir\*" } | Select -First 1
        if ($p -and (Test-Path "$Dir\appdata\coop_out.txt") -and (Get-Item "$Dir\appdata\coop_out.txt").LastWriteTime -gt $p.StartTime) { break }
        Start-Sleep 1
    }
    if (-not $p) { "no game in $Dir"; exit 1 }
    Start-Sleep 2
} else { $p = Get-Process -Id $ProcId -ErrorAction SilentlyContinue; if (-not $p) { "no process"; exit 1 } }
$prev = [Fg]::GetForegroundWindow()
[Fg]::ShowWindow($p.MainWindowHandle, 9) | Out-Null; [Fg]::SwitchToThisWindow($p.MainWindowHandle, $true)
Start-Sleep -Milliseconds 1500
if ($prev -ne [IntPtr]::Zero -and $prev -ne $p.MainWindowHandle) { [Fg]::SwitchToThisWindow($prev, $true) }
"woke $ProcId"
