# Two real games on one PC: host in testA, guest in testB, the guest's link through lag_relay.py.
# usage: duo.ps1 [-MinMs 50 -MaxMs 150]   |   duo.ps1 -Stop   (closes both test games and the relay)
param([int]$MinMs = 50, [int]$MaxMs = 150, [switch]$Stop)
$r = 'D:\Projects\stalker_coop'
$iniB = "$r\local\launcherB\coop_launcher.ini"

function Stop-Duo {
    Get-Process AnomalyDX11AVX*, CoopLauncher -ErrorAction SilentlyContinue | ? { $_.Path -like "$r\local\*" } | Stop-Process -Force
    Get-CimInstance Win32_Process -Filter "Name='python.exe'" | ? { $_.CommandLine -match "lag_relay" } | % { Stop-Process -Id $_.ProcessId -Force }
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }
    (Get-Content $iniB) -replace '^port=.*', 'port=27015' | Set-Content $iniB
}
Stop-Duo
if ($Stop) { "stopped"; exit }
Start-Sleep 2

# the latest local engine build and the launcher in both installs
foreach ($n in 'A', 'B') { Copy-Item 'D:\Projects\xray-monolith\_build\_game\bin_dbg\AnomalyDX11AVX.exe' "$r\local\test$n\bin\AnomalyDX11AVX_coop.exe" -Force }
Copy-Item "$r\local\testA\coop_engine\version.txt" "$r\local\testB\coop_engine\" -Force
foreach ($n in 'A', 'B') {
    Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcher$n\" -Force
    Remove-Item "$r\local\launcher$n\coop_launcher.log" -ErrorAction SilentlyContinue
    foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt') { Remove-Item "$r\local\test$n\appdata\$x" -ErrorAction SilentlyContinue }
    "1" | Set-Content "$r\local\test$n\appdata\coop_debug.txt"
}
foreach ($x in 'scop', 'scoc', 'dds') { Remove-Item "$r\local\testB\appdata\savedgames\coop_session.$x" -ErrorAction SilentlyContinue }
(Get-Content $iniB) -replace '^port=.*', 'port=27016' | Set-Content $iniB

Start-Process python -ArgumentList "-u", "$r\tests\lag_relay.py", 27016, 27015, $MinMs, $MaxMs -WindowStyle Hidden `
    -RedirectStandardOutput "$r\local\relay.log" -RedirectStandardError "$r\local\relay_err.log" | Out-Null
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherA\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherA" | Out-Null
$t0 = Get-Date
while (-not (Test-Path "$r\local\testA\appdata\coop_out.txt") -and ((Get-Date) - $t0).TotalSeconds -lt 300) { Start-Sleep 2 }
"host game up after {0:n0} s" -f ((Get-Date) - $t0).TotalSeconds
# the first wake can come while the level is still loading: a paused host never sends its world to the guest
Start-Sleep 15
$h = Get-Process AnomalyDX11AVX* -ErrorAction SilentlyContinue | ? { $_.Path -like "$r\local\testA\*" } | Select -First 1
if ($h) { & "$r\tests\wake_game.ps1" -ProcId $h.Id }
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testB" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
while (-not (Test-Path "$r\local\testB\appdata\coop_out.txt") -and ((Get-Date) - $t0).TotalSeconds -lt 600) { Start-Sleep 2 }
"guest game up after {0:n0} s" -f ((Get-Date) - $t0).TotalSeconds
Start-Sleep 20
"--- relay"; Get-Content "$r\local\relay.log"
foreach ($n in 'A', 'B') {
    "--- game $n"; Get-Content "$r\local\test$n\appdata\coop_log.txt" -ErrorAction SilentlyContinue | Select-String "puppet|connected|engine|failed|error" | Select -Last 8 | % Line
    Get-Content "$r\local\test$n\appdata\coop_syntax.txt" -ErrorAction SilentlyContinue | ? { $_ -notmatch ': ok$' }
}
