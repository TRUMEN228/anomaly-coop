# QA phase 1: real host launcher + game (testA) against a protocol-level fake guest.
$r = 'D:\Projects\stalker_coop'
Stop-Process -Name AnomalyDX11AVX, CoopLauncher -Force -ErrorAction SilentlyContinue; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherA\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt') { Remove-Item "$r\local\testA\appdata\$x" -ErrorAction SilentlyContinue }
Remove-Item "$r\local\launcherA\coop_launcher.log" -ErrorAction SilentlyContinue
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherA\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherA" | Out-Null
for ($i = 0; $i -lt 60 -and -not (Test-Path "$r\local\testA\appdata\coop_out.txt"); $i++) { Start-Sleep 2 }
"host in game: $(Test-Path "$r\local\testA\appdata\coop_out.txt")"; Start-Sleep 5
python "$r\tests\fake_peer.py" guest 27015 45
powershell -File "$r\tests\screen.ps1" -Out "$env:TEMP\claude\qa_host.png" | Out-Null
"--- launcher"; Get-Content "$r\local\launcherA\coop_launcher.log" | Select -Last 8
"--- game log"; Get-Content "$r\local\testA\appdata\coop_log.txt" | Select-String -NotMatch "dist_to_target" | Select -Last 15
"--- puppet samples"; Get-Content "$r\local\testA\appdata\coop_log.txt" | Select-String "dist_to_target" | Select -Last 4
