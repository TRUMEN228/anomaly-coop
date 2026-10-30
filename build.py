"""Build: convert mod scripts to cp1251 (the game's encoding) into build/, fetch the pinned Steam redistributable,
then build CoopLauncher.exe into dist/."""
import hashlib, io, pathlib, shutil, subprocess, sys, urllib.request, zipfile
root = pathlib.Path(__file__).resolve().parent
src, out = root / "mod" / "gamedata" / "scripts", root / "build" / "gamedata" / "scripts"
out.mkdir(parents=True, exist_ok=True)
for old in out.glob("coop_*.script"): old.unlink()
for f in sorted(src.glob("coop_*.script")):
    (out / f.name).write_bytes(f.read_text(encoding="utf-8").encode("cp1251"))
    print("script", f.name)
# the co-op menu's window (coop_menu.script), in cp1251 like the scripts
ui_src, ui_out = root / "mod" / "gamedata" / "configs" / "ui", root / "build" / "gamedata" / "configs" / "ui"
ui_out.mkdir(parents=True, exist_ok=True)
for f in sorted(ui_src.glob("ui_coop_*.xml")):
    (ui_out / f.name).write_bytes(f.read_text(encoding="utf-8").encode("cp1251"))
    print("ui", f.name)
if "--scripts-only" in sys.argv: sys.exit(0)

# steam_api64.dll from Steamworks.NET standalone 2025.164.1 (Valve redistributable), checked by SHA-256
STEAM_ZIP = "https://github.com/rlabrecque/Steamworks.NET/releases/download/2025.164.1/Steamworks.NET-Standalone_2025.164.1.zip"
STEAM_SHA = "eb17909a76668cf9ae0b92a618a34a50f6c73d3a6787cb4dd8ce36a8b10bfb75"
dll = root / "launcher" / "steam" / "steam_api64.dll"
if not dll.exists() or hashlib.sha256(dll.read_bytes()).hexdigest() != STEAM_SHA:
    print("fetching", STEAM_ZIP)
    data = zipfile.ZipFile(io.BytesIO(urllib.request.urlopen(STEAM_ZIP).read())).read("Windows-x64/steam_api64.dll")
    if hashlib.sha256(data).hexdigest() != STEAM_SHA: sys.exit("steam_api64.dll checksum mismatch")
    dll.parent.mkdir(exist_ok=True); dll.write_bytes(data)
subprocess.check_call(["dotnet", "build", str(root / "launcher" / "CoopLauncher.csproj"), "-c", "Release", "-nologo", "-v", "q"])
dist = root / "dist"; dist.mkdir(exist_ok=True)
built = root / "launcher" / "bin" / "Release" / "net48" / "CoopLauncher.exe"
try:
    shutil.copy2(built, dist / "CoopLauncher.exe")
    print("built", dist / "CoopLauncher.exe")
except PermissionError:
    print("built", built, "(dist/CoopLauncher.exe is running, close it to refresh dist)")

# --zip: AnomalyCoop.zip for playing without the launcher, laid out as the game folder: the co-op engine in place
# of the DX11 exes (like any modded exes), steam_api64.dll next to them, the engine's gamedata and the mod.
# The game updates itself from the newest release's copy of this archive (coop_update.cpp).
if "--zip" in sys.argv:
    engine = pathlib.Path(r"D:\Projects\xray-monolith")
    exes = engine / "_build" / "_game" / "bin_dbg"
    files = {f"bin/{e}": exes / e for e in ("AnomalyDX11.exe", "AnomalyDX11AVX.exe")}
    files["bin/steam_api64.dll"] = dll
    for base, prefix in ((engine / "gamedata", "gamedata"), (root / "build" / "gamedata", "gamedata")):
        for f in base.rglob("*"):
            if f.is_file(): files[prefix + "/" + f.relative_to(base).as_posix()] = f
    for f in (root / "mod" / "gamedata" / "configs").glob("mod_system_coop*.ltx"):
        files["gamedata/configs/" + f.name] = f
    out = dist / "AnomalyCoop.zip"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for name, f in sorted(files.items()):
            z.write(f, name)
    print("zip", out, f"({len(files)} files, {out.stat().st_size // 1024} KB)")
