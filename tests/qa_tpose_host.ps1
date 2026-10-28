# usage: qa_tpose_host.ps1 [vis=<outfit model>] [other fake_peer args]
# QA (E4 T-pose): the fake guest's actor partner goes through five phases of 12 s (runs/stands, movement flags, weapon or
# empty hands, named Bot1..Bot5); the actor partner is kept (coop_actor.txt "keep"), screenshots every 7 s.
# Prints the partner's pose per second: phase, flags, hand span, pelvis height and the motions it plays.
$r = 'D:\Projects\stalker_coop'; $t = "$r\local\testA"
Copy-Item "$r\tests\zz_coop_syntax.script" "$t\gamedata\scripts\" -Force
"1" | Set-Content "$t\appdata\coop_debug.txt"; "keep" | Set-Content "$t\appdata\coop_actor.txt"
Stop-Process -Name AnomalyDX11AVX*, CoopLauncher -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 2
Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcherA\" -Force
foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt') { Remove-Item "$t\appdata\$x" -ErrorAction SilentlyContinue }
Remove-Item "$r\local\shots\tpose_*.png" -ErrorAction SilentlyContinue
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherA\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherA" | Out-Null
for ($i = 0; $i -lt 60 -and -not (Test-Path "$t\appdata\coop_out.txt"); $i++) { Start-Sleep 2 }
Start-Sleep 5
$shots = Start-Job { param($r) Start-Sleep 8; for ($k = 1; $k -le 9; $k++) { $g = Get-Process AnomalyDX11AVX* | select -First 1; if ($g) { & "$r\tests\shot.ps1" -ProcId $g.Id -Out "$r\local\shots\tpose_$k.png" | Out-Null }; Start-Sleep 7 } } -ArgumentList $r
python "$r\tests\fake_peer.py" guest 27015 70 stamp fast actor phases @args | Out-Null
Get-Content "$t\appdata\coop_syntax.txt" | ? { $_ -notmatch ': ok$' }
Wait-Job $shots | Out-Null; if (-not (Get-Process AnomalyDX11AVX* -ErrorAction SilentlyContinue)) { "!!! game process is gone" }
Get-Content "$t\appdata\coop_log.txt" | Select-String "actor puppet \(|does not animate|failed|error" | % {
    $l = $_.Line
    if ($l -match '^\[(\d+)\] actor puppet \((\w+)\).*active=(\S+).*mstate=(\d+) hand_span=([-\d.]+) pelvis_y=([-\d.]+) xform_dy=([-\d.]+).* legs=(\S+) .* torso=(\S+) ') {
        "{0,6} {1} active={2} mstate={3} hands={4} pelvis={5} dy={6} legs={7} torso={8}" -f $Matches[1], $Matches[2], $Matches[3], $Matches[4], $Matches[5], $Matches[6], $Matches[7], $Matches[8], $Matches[9]
    } else { $l }
}
Remove-Item "$t\appdata\coop_debug.txt", "$t\appdata\coop_actor.txt"
Get-Process AnomalyDX11AVX*, CoopLauncher -ErrorAction SilentlyContinue | ? { $_.Path -like "$r\local\*" } | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -match "wake_game" } | % { Stop-Process -Id $_.ProcessId -Force }
