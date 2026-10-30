# Two real games on one PC, the host without a launcher: testA hosts through the link in the engine (r8,
# appdata\coop_link.txt), testB joins through lag_relay.py - with its launcher (protocol 7), or with -NoLauncher
# from the main menu, which loads the host's world by itself once it arrives.
# usage: duo_link.ps1 [-NoLauncher] [-MinMs 50 -MaxMs 150]   |   duo.ps1 -Stop closes them
param([switch]$NoLauncher, [int]$MinMs = 50, [int]$MaxMs = 150)
$r = 'D:\Projects\stalker_coop'
$iniB = "$r\local\launcherB\coop_launcher.ini"
& "$r\tests\duo.ps1" -Stop | Out-Null
Start-Sleep 2

foreach ($n in 'A', 'B') {
    $t = "$r\local\test$n"
    Copy-Item 'D:\Projects\xray-monolith\_build\_game\bin_dbg\AnomalyDX11AVX.exe' "$t\bin\AnomalyDX11AVX_coop.exe" -Force
    Copy-Item "$r\launcher\steam\steam_api64.dll" "$t\bin\" -Force
    foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt', 'coop_link.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
    "1" | Set-Content "$t\appdata\coop_debug.txt"
}
Copy-Item "$r\local\testA\coop_engine\version.txt" "$r\local\testB\coop_engine\" -Force
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherB\" -Force
Remove-Item "$r\local\launcherB\coop_launcher.log" -ErrorAction SilentlyContinue
# a game without a launcher gets the fresh mod by hand
$hand = if ($NoLauncher) { 'A', 'B' } else { 'A' }
foreach ($n in $hand) {
    Copy-Item "$r\build\gamedata\scripts\*.script" "$r\local\test$n\gamedata\scripts\" -Force
    New-Item -ItemType Directory -Force "$r\local\test$n\gamedata\configs\ui" | Out-Null
    Copy-Item "$r\build\gamedata\configs\ui\*.xml" "$r\local\test$n\gamedata\configs\ui\" -Force
}
"host 27015`nname Host" | Set-Content "$r\local\testA\appdata\coop_link.txt" -Encoding ascii
if ($NoLauncher) { "join 127.0.0.1 27016`nname Guest" | Set-Content "$r\local\testB\appdata\coop_link.txt" -Encoding ascii }
foreach ($x in 'scop', 'scoc', 'dds') { Remove-Item "$r\local\testB\appdata\savedgames\coop_session.$x" -ErrorAction SilentlyContinue }
(Get-Content $iniB) -replace '^port=.*', 'port=27016' | Set-Content $iniB

Start-Process python -ArgumentList "-u", "$r\tests\lag_relay.py", 27016, 27015, $MinMs, $MaxMs -WindowStyle Hidden `
    -RedirectStandardOutput "$r\local\relay.log" -RedirectStandardError "$r\local\relay_err.log" | Out-Null
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\testA\bin\AnomalyDX11AVX_coop.exe" -WorkingDirectory "$r\local\testA" `
    -ArgumentList "-fsltx fsgame_coop.ltx -nointro -start server(aaaaa/single/alife/load) client(localhost)" | Out-Null
$t0 = Get-Date
function Logged($d, $what) { (Test-Path "$d\appdata\coop_log.txt") -and (Select-String -Path "$d\appdata\coop_log.txt" -Pattern $what -Quiet) }
while (-not (Logged "$r\local\testA" "engine link") -and ((Get-Date) - $t0).TotalSeconds -lt 300) { Start-Sleep 2 }
"host game up after {0:n0} s" -f ((Get-Date) - $t0).TotalSeconds
Start-Sleep 15
$h = Get-Process AnomalyDX11AVX* -ErrorAction SilentlyContinue | ? { $_.Path -like "$r\local\testA\*" } | Select -First 1
if ($h) { & "$r\tests\wake_game.ps1" -ProcId $h.Id }
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testB" -WindowStyle Hidden | Out-Null
if ($NoLauncher) {
    # into the main menu: the link starts there and the host's world loads when it comes
    Start-Process "$r\local\testB\bin\AnomalyDX11AVX_coop.exe" -WorkingDirectory "$r\local\testB" -ArgumentList "-fsltx fsgame_coop.ltx -nointro" | Out-Null
} else {
    Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
}
while (-not (Logged "$r\local\testB" "puppet") -and ((Get-Date) - $t0).TotalSeconds -lt 600) { Start-Sleep 2 }
"guest game up after {0:n0} s" -f ((Get-Date) - $t0).TotalSeconds
Start-Sleep 20
"--- relay"; Get-Content "$r\local\relay.log"
foreach ($n in 'A', 'B') {
    "--- game $n"; Get-Content "$r\local\test$n\appdata\coop_log.txt" -ErrorAction SilentlyContinue | Select-String "link|connected|engine|failed|error" | Select -Last 8 | % Line
}
