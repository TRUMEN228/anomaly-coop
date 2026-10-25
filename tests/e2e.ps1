# End-to-end: host launcher (testA) + guest launcher (testB) on localhost.
# Prints a status line every 10 s until both games are in co-op or the timeout hits.
param([int]$Timeout = 360)
$r = 'D:\Projects\stalker_coop'
Stop-Process -Name AnomalyDX11AVX*, CoopLauncher -Force -ErrorAction SilentlyContinue; Start-Sleep 2
foreach ($n in 'A', 'B') {
    Copy-Item "$r\launcher\bin\Release\net48\CoopLauncher.exe" "$r\local\launcher$n\" -Force
    Remove-Item "$r\local\launcher$n\coop_launcher.log" -ErrorAction SilentlyContinue
    foreach ($x in 'coop_out.txt', 'coop_in.txt', 'coop_log.txt', 'coop_syntax.txt') { Remove-Item "$r\local\test$n\appdata\$x" -ErrorAction SilentlyContinue }
}
foreach ($x in 'scop', 'scoc', 'dds') { Remove-Item "$r\local\testB\appdata\savedgames\coop_session.$x" -ErrorAction SilentlyContinue }
Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testA" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherA\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherA" | Out-Null
$guestStarted = $false; $t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    Start-Sleep 10
    $sec = [int]((Get-Date) - $t0).TotalSeconds
    if (-not $guestStarted -and (Test-Path "$r\local\testA\appdata\coop_out.txt")) {
        Start-Process powershell -ArgumentList "-NoProfile", "-File", "$r\tests\wake_game.ps1", "-Dir", "$r\local\testB" -WindowStyle Hidden | Out-Null
Start-Process "$r\local\launcherB\CoopLauncher.exe" -ArgumentList "--auto", "--no-upnp" -WorkingDirectory "$r\local\launcherB" | Out-Null
        $guestStarted = $true
    }
    $procs = (Get-Process AnomalyDX11AVX*, CoopLauncher -ErrorAction SilentlyContinue | % ProcessName) -join ','
    $a = Test-Path "$r\local\testA\appdata\coop_out.txt"; $b = Test-Path "$r\local\testB\appdata\coop_out.txt"
    "{0,4}s procs=[{1}] hostGame={2} guestGame={3}" -f $sec, $procs, $a, $b
    if ($b) { Start-Sleep 25; break }
}
foreach ($n in 'A', 'B') {
    "--- launcher $n"; Get-Content "$r\local\launcher$n\coop_launcher.log" -ErrorAction SilentlyContinue | Select -Last 12
    "--- game $n"; Get-Content "$r\local\test$n\appdata\coop_log.txt" -ErrorAction SilentlyContinue | Select -Last 10
    Get-Content "$r\local\test$n\appdata\coop_syntax.txt" -ErrorAction SilentlyContinue | ? { $_ -notmatch ': ok$' }
}
