"""Build: convert mod scripts to cp1251 (the game's encoding) into build/, then build CoopLauncher.exe into dist/."""
import pathlib, shutil, subprocess, sys
root = pathlib.Path(__file__).resolve().parent
src, out = root / "mod" / "gamedata" / "scripts", root / "build" / "gamedata" / "scripts"
out.mkdir(parents=True, exist_ok=True)
for old in out.glob("coop_*.script"): old.unlink()
for f in sorted(src.glob("coop_*.script")):
    (out / f.name).write_bytes(f.read_text(encoding="utf-8").encode("cp1251"))
    print("script", f.name)
if "--scripts-only" in sys.argv: sys.exit(0)
subprocess.check_call(["dotnet", "build", str(root / "launcher" / "CoopLauncher.csproj"), "-c", "Release", "-nologo", "-v", "q"])
dist = root / "dist"; dist.mkdir(exist_ok=True)
shutil.copy2(root / "launcher" / "bin" / "Release" / "net48" / "CoopLauncher.exe", dist / "CoopLauncher.exe")
print("built", dist / "CoopLauncher.exe")
