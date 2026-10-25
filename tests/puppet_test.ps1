# Puppet smoke test with fake partner. usage: puppet_test.ps1 [-Secs 25]
param([int]$Secs = 25, [string]$Save = "aaaaa")
$t = "D:\Projects\stalker_coop\local\testA"
Stop-Process -Name AnomalyDX11AVX* -Force -ErrorAction SilentlyContinue; Start-Sleep 2
Remove-Item "$t\appdata\coop_syntax.txt" -ErrorAction SilentlyContinue
$id = & D:\Projects\stalker_coop\tests\run_game.ps1 -Inst A -Save $Save
for ($i = 0; $i -lt 50 -and -not (Test-Path "$t\appdata\coop_out.txt"); $i++) { Start-Sleep 3 }
Get-Content "$t\appdata\coop_syntax.txt" | Where-Object { $_ -notmatch ": ok$" }
Start-Sleep 4
python D:\Projects\stalker_coop\tests\fake_bridge.py $t $Secs
powershell -File D:\Projects\stalker_coop\tests\screen.ps1 -Out "$env:TEMP\claude\scr.png" | Out-Null
Get-Content "$t\appdata\coop_log.txt" -Tail 14
