# QA v2.2: host options, money total and sleep on the real guest.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testB"
Copy-Item "$r\tests\zz_coop_teamtest.script", "$r\tests\zz_coop_syntax.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_teamtest.txt"; "1" | Set-Content "$t\appdata\coop_debug.txt"
Stop-Process -Name AnomalyDX11AVX, CoopLauncher -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherB\" -Force
(Get-Content "$r\local\launcherB\coop_launcher.ini") -replace "^mode=.*", "mode=client" -replace "^transport=.*", "transport=ip" | Set-Content "$r\local\launcherB\coop_launcher.ini"
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt', 'coop_teamtest_log.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
$peer = Start-Process python -ArgumentList "$r\tests\fake_peer.py", "host", "27015", "100", "$r\local\testA\appdata\savedgames", "team" -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\claude\fake_host.txt"
Start-Sleep 2
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testB" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
$peer.WaitForExit()
Get-Content "$env:TEMP\claude\fake_host.txt" | Select-String "events:|dn states" | % { $_.Line -replace "'(chat|die|unreg|sqm|art|session)[^']*',? ?", "" }
Get-Content "$t\appdata\coop_syntax.txt" | ? { $_ -notmatch ': ok$' }
"--- guest log"; Get-Content "$t\appdata\coop_teamtest_log.txt"; Get-Content "$t\appdata\coop_log.txt" | Select-String "team:|failed" | % Line
Remove-Item "$t\appdata\coop_teamtest.txt", "$t\appdata\coop_debug.txt"; Remove-Item "$t\gamedata\scripts\zz_coop_teamtest.script"
