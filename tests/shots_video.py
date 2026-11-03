"""The test run as a film: local/qa_shots/<seconds>_<step>_<A|B>.jpg (scenario.ps1) side by side - the host on the
left, the guest on the right - with the time and the step on each frame, saved as an animated WebP.
usage: python shots_video.py [shots dir] [out file] [frame ms]"""
import pathlib
import re
import sys

from PIL import Image, ImageDraw, ImageFont

shots = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else r"D:\Projects\stalker_coop\local\qa_shots")
out = pathlib.Path(sys.argv[2] if len(sys.argv) > 2 else shots / "run.webp")
frame_ms = int(sys.argv[3]) if len(sys.argv) > 3 else 700

frames = {}
for f in shots.glob("*.jpg"):
    m = re.match(r"^(\d+)_(.+)_([AB])$", f.stem)
    if m:
        frames.setdefault((int(m.group(1)), m.group(2)), {})[m.group(3)] = f

try:
    font = ImageFont.truetype("arial.ttf", 18)
except OSError:
    font = ImageFont.load_default()

film = []
for (sec, step), pair in sorted(frames.items()):
    halves = [Image.open(pair[n]).convert("RGB") if n in pair else None for n in "AB"]
    w = max(h.width for h in halves if h)
    h = max(h.height for h in halves if h)
    frame = Image.new("RGB", (w * 2, h + 28), (20, 22, 24))
    for i, half in enumerate(halves):
        if half:
            frame.paste(half, (i * w, 28))
    d = ImageDraw.Draw(frame)
    d.text((8, 4), f"{sec // 60}:{sec % 60:02d}   step {step}", fill=(255, 220, 120), font=font)
    d.text((w // 2 - 30, 4), "HOST", fill=(200, 200, 200), font=font)
    d.text((w + w // 2 - 30, 4), "GUEST", fill=(200, 200, 200), font=font)
    film.append(frame)

if not film:
    sys.exit("no frames in " + str(shots))
film[0].save(out, save_all=True, append_images=film[1:], duration=frame_ms, loop=0, quality=70)
print(f"{out} ({len(film)} frames, {out.stat().st_size // 1024} KB)")
