# QA v2.0: the fake host sends a new squad (host ids 60001-60003), a death and a removal; the real guest must map ids.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testB"
Copy-Item "$r\tests\zz_coop_syntax.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_debug.txt"
Stop-Process -Name AnomalyDX11AVX, CoopLauncher -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherB\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
$peer = Start-Process python -ArgumentList "$r\tests\fake_peer.py", "host", "27015", "110", "$r\local\testA\appdata\savedgames", "spawn", "world" -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\claude\fake_host.txt"
Start-Sleep 2
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testB" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
$peer.WaitForExit()
Get-Content "$env:TEMP\claude\fake_host.txt" | Select-String "sqm|unreg|die" | % Line
Get-Content "$t\appdata\coop_syntax.txt" | ? { $_ -notmatch ': ok$' }
"--- guest log"; Get-Content "$t\appdata\coop_log.txt" | Select-String "spawn:|objects:|loot |recv die|recv hit|failed" | % Line
Remove-Item "$t\appdata\coop_debug.txt"
