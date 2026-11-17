# Quick checks of the repository without a game (a few seconds): versions agree, the engine's revision agrees with
# itself, every script and window converts to cp1251, build\ holds nothing removed from mod\, the protocol constants.
# usage: smoke.ps1 [-Engine D:\Projects\xray-monolith]
# exit code: 0 when every check passed, 1 otherwise (WARN lines do not fail)
param([string]$Engine = 'D:\Projects\xray-monolith')
$r = Split-Path $PSScriptRoot -Parent
$fails = 0
function Check($name, $ok, $detail) {
    if ($ok) { "PASS $name - $detail" } else { "FAIL $name - $detail"; $script:fails++ }
}
function Warn($name, $detail) { "WARN $name - $detail" }
function Grab($file, $pattern) {
    $m = Select-String -Path $file -Pattern $pattern -ErrorAction SilentlyContinue | Select -First 1
    if ($m) { $m.Matches[0].Groups[1].Value } else { $null }
}

# versions: the mod, the launcher, the setup (x.y.0), the notes for the assistant
$lua = Grab "$r\mod\gamedata\scripts\coop_main.script" '^VERSION = "([^"]+)"'
$launcher = Grab "$r\launcher\CoopLauncher.csproj" '<Version>([^<]+)</Version>'
$setup = Grab "$r\installer\CoopSetup.csproj" '<Version>([^<]+)</Version>'
Check 'version' ($lua -and $lua -eq $launcher -and "$lua.0" -eq $setup) "mod $lua, launcher $launcher, setup $setup"
$ctx = Grab "$r\AI_CONTEXT.md" 'после релиза ([0-9.]+)'
if ($ctx -and $ctx -ne $lua) { Warn 'version-notes' "AI_CONTEXT says $ctx, the mod is $lua (fine between a version bump and its release)" }

# the engine's revision: told in the hello and returned to the scripts - the same number
$src = "$Engine\src\xrGame"
if (Test-Path $src) {
    $rev = Grab "$src\coop_link.cpp" 'ENGINE_REVISION = "(\d+)"'
    $scr = Grab "$src\script_game_object_script.cpp" 'coop_engine_version\(\) \{ return (\d+); \}'
    Check 'engine-revision' ($rev -and $rev -eq $scr) "hello $rev, coop_engine_version() $scr"
    $proto = Grab "$src\coop_link.cpp" 'const char\* PROTOCOL = "(\d+)"'
    $lp = Grab "$r\launcher\Bridge.cs" 'ProtocolVersion = "(\d+)"'
    $fp = Grab "$r\tests\fake_peer.py" '^PROTO = b"(\d+)"'
    "INFO protocol - engine $proto, launcher bridge $lp, fake_peer $fp (the launcher's bridge and fake_peer are the old path without the engine's link)"
    if ($lp -and $fp -and $lp -ne $fp) { Warn 'fake-peer' "fake_peer speaks $fp, the launcher wants ${lp}: the qa_*.ps1 runs on fake_peer are out of date" }
} else { Warn 'engine' "no engine sources at $Engine" }

# every script and window converts to cp1251 (the game's encoding): a character outside it breaks the build
$cp = [System.Text.Encoding]::GetEncoding(1251, [System.Text.EncoderFallback]::ExceptionFallback, [System.Text.DecoderFallback]::ExceptionFallback)
$files = @(Get-ChildItem "$r\mod\gamedata\scripts\coop_*.script", "$r\mod\gamedata\configs\ui\ui_coop_*.xml", "$r\tests\zz_coop_*.script")
$bad = @()
foreach ($f in $files) {
    $text = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    try { $null = $cp.GetBytes($text) } catch { $bad += $f.Name }
}
Check 'cp1251' ($bad.Count -eq 0) $(if ($bad.Count) { "not in cp1251: " + ($bad -join ', ') } else { "$($files.Count) files" })

# build\ holds no script or window that mod\ no longer has (it would go into a release)
$stale = @()
foreach ($pair in @(@("$r\build\gamedata\scripts", "$r\mod\gamedata\scripts", 'coop_*.script'), @("$r\build\gamedata\configs\ui", "$r\mod\gamedata\configs\ui", 'ui_coop_*.xml'))) {
    foreach ($f in @(Get-ChildItem $pair[0] -Filter $pair[2] -ErrorAction SilentlyContinue)) {
        if (-not (Test-Path (Join-Path $pair[1] $f.Name))) { $stale += $f.Name }
    }
}
Check 'build-stale' ($stale.Count -eq 0) $(if ($stale.Count) { "in build\ only: " + ($stale -join ', ') } else { 'nothing left over' })

# the scenario test: no control characters in its paths (a TAB and a BEL once broke the window focus)
$ps = [System.IO.File]::ReadAllText("$r\tests\scenario.ps1")
$ctl = @($ps.ToCharArray() | ? { [int]$_ -lt 32 -and $_ -notin "`r", "`n", "`t" }).Count
Check 'test-paths' ($ctl -eq 0) "$ctl control characters in scenario.ps1"

"RESULT: $fails failed"
if ($fails -gt 0) { exit 1 }
exit 0
