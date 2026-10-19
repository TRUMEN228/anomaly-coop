# Start a test instance straight into a save. usage: run_game.ps1 -Inst A -Save aaaaa
param([string]$Inst = "A", [string]$Save = "aaaaa")
$t = "D:\Projects\stalker_coop\local\test$Inst"
Remove-Item "$t\appdata\coop_out.txt", "$t\appdata\coop_in.txt" -ErrorAction SilentlyContinue
$p = Start-Process "$t\bin\AnomalyDX11AVX.exe" -ArgumentList "-nointro -start server($Save/single/alife/load) client(localhost)" -WorkingDirectory $t -PassThru
$p.Id
