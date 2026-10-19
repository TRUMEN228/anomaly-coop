# QA phase 2: protocol-level fake host (serves testA's coop_session) against the real guest launcher + game (testB).
$r = 'D:\Projects\stalker_coop'
Stop-Process -Name AnomalyDX11AVX, CoopLauncher -Force -ErrorAction SilentlyContinue; Start-Sleep 2
Copy-Item "$r\dist\CoopLauncher.exe" "$r\local\launcherB\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt') { Remove-Item "$r\local\testB\appdata\$x" -ErrorAction SilentlyContinue }
Remove-Item "$r\local\launcherB\coop_launcher.log" -ErrorAction SilentlyContinue
$peer = Start-Process python -ArgumentList "$r\tests\fake_peer.py", "host", "27015", "150", "$r\local\testA\appdata\savedgames" -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\claude\fake_host.txt"
Start-Sleep 2
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto" -WorkingDirectory "$r\local\launcherB" | Out-Null
$peer.WaitForExit()
powershell -File "$r\tests\screen.ps1" -Out "$env:TEMP\claude\qa_guest.png" | Out-Null
"--- fake host"; Get-Content "$env:TEMP\claude\fake_host.txt"
"--- launcher"; Get-Content "$r\local\launcherB\coop_launcher.log" | Select -Last 6
"--- game log"; Get-Content "$r\local\testB\appdata\coop_log.txt" | Select-String -NotMatch "dist_to_target" | Select -Last 15
"--- puppet samples"; Get-Content "$r\local\testB\appdata\coop_log.txt" | Select-String "dist_to_target" | Select -Last 3
"--- character file"; Get-Content "$r\local\testB\appdata\coop_character.txt" -ErrorAction SilentlyContinue | Select -First 6
