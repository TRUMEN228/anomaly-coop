# QA v2.3: the fake guest stands in front of the real host; screenshots show the tag, torch, reload, wounded puppet.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testA"; $out = "$env:TEMP\claude\live"
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$r\tests\zz_coop_livetest.script", "$r\tests\zz_coop_syntax.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_livetest.txt"; "1" | Set-Content "$t\appdata\coop_debug.txt"
Stop-Process -Name AnomalyDX11AVX*, CoopLauncher -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherA\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherA\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherA" | Out-Null
for ($i = 0; $i -lt 60 -and -not (Test-Path "$t\appdata\coop_out.txt"); $i++) { Start-Sleep 2 }
Start-Sleep 5
$peer = Start-Process python -ArgumentList "$r\tests\fake_peer.py", "guest", "27015", "85", "live" -NoNewWindow -PassThru -RedirectStandardOutput "$out\peer.txt"
$game = (Get-Process AnomalyDX11AVX* | Select-Object -First 1).Id
foreach ($s in 47, 59, 74, 80) {
    while (((Get-Date) - $peer.StartTime).TotalSeconds -lt $s) { Start-Sleep -Milliseconds 300 }
    & "$r\tests\shot.ps1" -ProcId $game -Out "$out\live_$s.png" | Out-Null
}
$peer.WaitForExit()
Get-Content "$t\appdata\coop_syntax.txt" | ? { $_ -notmatch ': ok$' }
"--- host log"; Get-Content "$t\appdata\coop_log.txt" | Select-String "LIVETEST|fx:|failed" | % Line
Remove-Item "$t\appdata\coop_livetest.txt", "$t\appdata\coop_debug.txt"; Remove-Item "$t\gamedata\scripts\zz_coop_livetest.script"
