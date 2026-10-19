# Disconnect check: after the fake partner stops, the puppet must be released and gone from alife.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testA"
python "$r\build.py" --scripts-only | Out-Null
Copy-Item "$r\build\gamedata\scripts\coop_*.script" "$t\gamedata\scripts\" -Force
Stop-Process -Name AnomalyDX11AVX -Force -ErrorAction SilentlyContinue; Start-Sleep 2
$id = & "$r\tests\run_game.ps1" -Inst A
for ($i = 0; $i -lt 100 -and -not (Test-Path "$t\appdata\coop_out.txt"); $i++) { Start-Sleep 3 }
Start-Sleep 4
python "$r\tests\fake_bridge.py" $t 15 | Out-Null
Start-Sleep 8
Get-Content "$t\appdata\coop_log.txt" | Select-String "spawned|released|failed" | % Line
