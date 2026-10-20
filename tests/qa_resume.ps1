# QA: guest reconnects after a network drop and must resume (send "resume"), not reload the world.
$r = 'D:\Projects\stalker_coop'
Stop-Process -Name AnomalyDX11AVX, CoopLauncher -Force -ErrorAction SilentlyContinue; Start-Sleep 2
Copy-Item "$r\dist\CoopLauncher.exe" "$r\local\launcherB\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt') { Remove-Item "$r\local\testB\appdata\$x" -ErrorAction SilentlyContinue }
Remove-Item "$r\local\launcherB\coop_launcher.log" -ErrorAction SilentlyContinue
$peer = Start-Process python -ArgumentList "$r\tests\fake_peer.py", "host", "27015", "160", "$r\local\testA\appdata\savedgames", "reconnect" -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\claude\fake_host.txt"
Start-Sleep 2
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
$peer.WaitForExit()
"--- fake host"; Get-Content "$env:TEMP\claude\fake_host.txt"
"--- game launches by guest launcher: " + (Get-Content "$r\local\launcherB\coop_launcher.log" | Select-String "AnomalyDX11").Count
"--- keys file"; Get-Content "$r\local\testB\appdata\coop_keys.txt"
