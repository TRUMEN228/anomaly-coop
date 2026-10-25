# QA v2.4 (E2): the fake host runs a real stalker of the shared save in circles; the real guest measures its steps.
# -Engine 1 starts the co-op engine (must be installed in testB), -Engine 0 the stock exe for comparison.
param([int]$Engine = 1)
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testB"
Copy-Item "$r\tests\zz_coop_npctest.script", "$r\tests\zz_coop_syntax.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_npctest.txt"; "1" | Set-Content "$t\appdata\coop_debug.txt"
Stop-Process -Name AnomalyDX11AVX*, CoopLauncher -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherB\" -Force
$ini = "$r\local\launcherB\coop_launcher.ini"
(Get-Content $ini | ? { $_ -notmatch '^coop_engine=' }) -replace "^mode=.*", "mode=client" -replace "^transport=.*", "transport=ip" | Set-Content $ini
Add-Content $ini "coop_engine=$Engine"
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt', 'coop_npctest_log.txt', 'coop_npctest_pick.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
$peer = Start-Process python -ArgumentList "$r\tests\fake_peer.py", "host", "27015", "100", "$r\local\testA\appdata\savedgames", "npc", "$t\appdata\coop_npctest_pick.txt" -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\claude\fake_host.txt"
Start-Sleep 2
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testB" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
$peer.WaitForExit()
Get-Content "$env:TEMP\claude\fake_host.txt" | Select-String "npc picked" | % Line
Get-Content "$t\appdata\coop_syntax.txt" | ? { $_ -notmatch ': ok$' }
"--- guest"; Get-Content "$r\local\launcherB\coop_launcher.log" -Tail 15 | Select-String "движок|Запуск игры" | % Line
Get-Content "$t\appdata\coop_npctest_log.txt"; Get-Content "$t\appdata\coop_log.txt" | Select-String "failed|engine" | % Line
Get-Process | ? { ($_.ProcessName -like "Anomaly*" -or $_.ProcessName -eq "CoopLauncher") -and $_.Path -like "$r\local\*" } | Stop-Process -Force
Remove-Item "$t\appdata\coop_npctest.txt", "$t\appdata\coop_debug.txt"; Remove-Item "$t\gamedata\scripts\zz_coop_npctest.script"
