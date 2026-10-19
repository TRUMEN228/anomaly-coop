"""Fake CoopBridge: pretends a partner walks in a circle around the local actor.
usage: python fake_bridge.py <game dir> [seconds]"""
import math, os, sys, time
game = sys.argv[1]; dur = float(sys.argv[2]) if len(sys.argv) > 2 else 60
out_p = os.path.join(game, "appdata", "coop_out.txt"); in_p = os.path.join(game, "appdata", "coop_in.txt")
shot_count = 0
center = None; t0 = time.time(); seen = 0; log = []
while time.time() - t0 < dur:
    try: raw = open(out_p, encoding="cp1251").read()
    except OSError: raw = ""
    if "\nend" in raw:
        for line in raw.splitlines():
            f = line.split("|")
            if f[0] == "s" and f[1] == "p":
                seen += 1
                if center is None:
                    h = float(f[6]); ax, az = float(f[3]), float(f[5])
                    center = (ax - 8 * math.sin(h), float(f[4]), az + 8 * math.cos(h)); lvl, lv, gv = f[2], f[7], f[8]
    if center:
        a = (time.time() - t0) * 0.3
        x, z = center[0] + 4 * math.cos(a), center[2] + 4 * math.sin(a)
        el = time.time() - t0
        burst = int(el) % 4 < 2 and el > 12          # 2 s bursts every 4 s after 12 s
        if burst: shot_count += 1
        aim = f"{center[0] + 20:.2f}:{center[1] + 1.5:.2f}:{center[2]:.2f}"
        mv = 0 if burst else 1
        st = f"s|p|{lvl}|{x:.2f}|{center[1]:.2f}|{z:.2f}|{a:.2f}|{lv}|{gv}|s|{mv}|wpn_ak74|||1.0|TestBot|{shot_count}|{aim}"
        body = f"b|fake1|0|host|1|TestBot\n{st}\nend\n"
        tmp = in_p + ".tmp"; open(tmp, "w").write(body)
        try: os.replace(tmp, in_p)
        except PermissionError: pass  # game is reading it right now
    time.sleep(0.1)
print("state lines seen:", seen, "center:", center)
