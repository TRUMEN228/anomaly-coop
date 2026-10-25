# Visual check of puppet firing: fake partner shoots in bursts, take screenshots during a burst.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testA"
python "$r\build.py" --scripts-only | Out-Null
Copy-Item "$r\build\gamedata\scripts\coop_*.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_debug.txt"
Stop-Process -Name AnomalyDX11AVX* -Force -ErrorAction SilentlyContinue; Start-Sleep 2
$id = & "$r\tests\run_game.ps1" -Inst A
for ($i = 0; $i -lt 50 -and -not (Test-Path "$t\appdata\coop_out.txt"); $i++) { Start-Sleep 3 }
Start-Sleep 4
$fb = Start-Process python -ArgumentList "$r\tests\fake_bridge.py", $t, "40" -NoNewWindow -PassThru
Start-Sleep 12.5
foreach ($k in 1..5) { & "$r\tests\shot.ps1" -ProcId $id -Out "$env:TEMP\claude\fire$k.png" | Out-Null; Start-Sleep -Milliseconds 250 }
$fb.WaitForExit()
Get-Content "$t\appdata\coop_log.txt" | Select-String "fire|failed|weapon" | Select -First 5 | % Line
