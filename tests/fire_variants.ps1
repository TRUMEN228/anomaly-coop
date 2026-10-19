# Try puppet firing variants; prints magazine samples for each.
$r = 'D:\Projects\stalker_coop'
python "$r\build.py" --scripts-only | Out-Null
Copy-Item "$r\build\gamedata\scripts\coop_*.script" "$r\local\testA\gamedata\scripts\" -Force
foreach ($v in 'B', 'C') {
    "fire=$v" | Set-Content "$r\local\testA\appdata\coop_debug.txt"
    powershell -NoProfile -File "$r\tests\puppet_test.ps1" -Secs 30 | Out-Null
    "=== variant $v"; Get-Content "$r\local\testA\appdata\coop_log.txt" | Select-String "fire|failed" | Select -First 6 | % Line
}
Stop-Process -Name AnomalyDX11AVX -Force -ErrorAction SilentlyContinue
