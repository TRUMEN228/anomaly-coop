# QA v2.2: the real host gives items through the exchange box; the fake guest gives some back.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testA"
Copy-Item "$r\tests\zz_coop_tradetest.script", "$r\tests\zz_coop_syntax.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_tradetest.txt"; "1" | Set-Content "$t\appdata\coop_debug.txt"
Stop-Process -Name AnomalyDX11AVX*, CoopLauncher -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherA\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherA\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherA" | Out-Null
for ($i = 0; $i -lt 60 -and -not (Test-Path "$t\appdata\coop_out.txt"); $i++) { Start-Sleep 2 }
Start-Sleep 5
python "$r\tests\fake_peer.py" guest 27015 60 trade | Select-String "events:" | % { $_.Line -replace "'(chat|die|session_ready|unreg|sqm|art)[^']*',? ?", "" }
Get-Content "$t\appdata\coop_syntax.txt" | ? { $_ -notmatch ': ok$' }
"--- host log"; Get-Content "$t\appdata\coop_log.txt" | Select-String "TRADETEST|trade:|failed" | % Line
Remove-Item "$t\appdata\coop_tradetest.txt", "$t\appdata\coop_debug.txt"; Remove-Item "$t\gamedata\scripts\zz_coop_tradetest.script"
