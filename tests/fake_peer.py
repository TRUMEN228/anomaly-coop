"""Protocol-level fake partner for CoopLauncher (one real game per PC is all Anomaly allows).

  python fake_peer.py guest <port> <secs>            connect to a real host launcher on 127.0.0.1
  python fake_peer.py host  <port> <secs> <save dir> serve <save dir>/coop_session.* to a real guest launcher
  ... host ... npc <pick file>   run the stalker the guest wrote to <pick file> in circles (QA v2.4, E2)

The fake player circles around the real player's position, then sends chat, a drop, a hit and a kill
for the nearest creature from the host's creature list. Everything received is summarised at the end.
"""
import math, os, random, socket, struct, sys, time, zlib

HELLO, STATE, EVENT, FILE, PING, STATEZ = 1, 2, 3, 4, 5, 6
PROTO = b"6"
mode, port, secs = sys.argv[1], int(sys.argv[2]), float(sys.argv[3])
enc = "cp1251"


def frame(t, payload):
    return struct.pack("<i", len(payload) + 1) + bytes([t]) + payload


def recv_exact(s, n):
    b = b""
    while len(b) < n:
        c = s.recv(n - len(b))
        if not c:
            raise ConnectionError("closed")
        b += c
    return b


if mode == "guest":
    s = socket.create_connection(("127.0.0.1", port))
else:
    srv = socket.socket(); srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(("0.0.0.0", port)); srv.listen(1)
    print("waiting for guest launcher..."); s, _ = srv.accept()
s.sendall(frame(HELLO, PROTO + b"|Bot" + mode.capitalize().encode()))
s.settimeout(0.05)

got = {"files": [], "events": [], "states": {}}
peer_pos = None; npcs = []; t0 = time.time(); sent = set(); buf = b""


def send_event(text):
    s.sendall(frame(EVENT, text.encode(enc)))
    print("sent event", text)


def pump():
    global buf, peer_pos, npcs
    try:
        data = s.recv(1 << 20)
        if not data:
            raise ConnectionError("closed")
        buf += data
    except socket.timeout:
        pass
    while len(buf) >= 4:
        n = struct.unpack("<i", buf[:4])[0]
        if len(buf) < 4 + n:
            break
        t, p = buf[4], buf[5:4 + n]; buf = buf[4 + n:]
        if t == HELLO:
            print("hello from", p.decode())
        elif t == FILE:
            ln = struct.unpack("<i", p[:4])[0]; got["files"].append((p[4:4 + ln].decode(), len(p) - 4 - ln))
        elif t == EVENT:
            got["events"].append(p.decode(enc))
        elif t in (STATE, STATEZ):
            if t == STATEZ:
                p = zlib.decompress(p, -15)  # raw deflate
                got["states"]["(compressed frames)"] = got["states"].get("(compressed frames)", 0) + 1
            for line in p.decode(enc).splitlines():
                f = line.split("|")
                got["states"][f[1]] = got["states"].get(f[1], 0) + 1
                if f[1] in ("dn", "o"):
                    got.setdefault("last_" + f[1], set()).add("|".join(f[2:]))
                if f[1] == "pd" and len(f) > 3:
                    got.setdefault("doors", []).extend(f[3:])
                if f[1] == "p":
                    peer_pos = (f[2], float(f[3]), float(f[4]), float(f[5]), f[7], f[8])
                elif f[1] == "n":
                    npcs = f[3:]


if mode == "host":
    d = sys.argv[4]
    for ext in ("scop", "scoc", "dds"):
        name = f"coop_session.{ext}"; data = open(os.path.join(d, name), "rb").read()
        nb = name.encode(); s.sendall(frame(FILE, struct.pack("<i", len(nb)) + nb + data))
    send_event("session_ready|faketoken")

def qvars(d):
    # coop_quests wire format for a table of strings
    out = "t" + "".join(f"s{len(k)}:{k}s{len(v)}:{v}" for k, v in d.items()) + "e"
    return out.replace("%", "%25").replace("|", "%7C")


def quest_steps(t):
    """v2.1: the fake partner's side of the task sync (ids from the testA save, Zaton)."""
    mech, medic = "zat_a2_stalker_mechanic_task_1", "zat_b22_stalker_medic_task_1"
    if mode == "guest" and t > 30 and "qreq" not in sent:
        sent.add("qreq")
        send_event("qinfo|coop_guest_info|1")
        send_event(f"qreq|{mech}|21552|" + qvars({mech + "_fetch": "itm_basickit"}))
    if mode == "guest" and t > 50 and "qdone" not in sent:
        sent.add("qdone"); send_event(f"qdone|{mech}|complete|1000|1")
    if mode == "host" and peer_pos and t > 65 and "qgive" not in sent:
        sent.add("qgive")
        send_event("qinfo|coop_host_info|1")
        send_event(f"qgive|{medic}|21552|0|" + qvars({medic + "_fetch": "itm_drugkit"}))
    if mode == "host" and t > 100 and "qinfo0" not in sent and "qgive" in sent:
        sent.add("qinfo0"); send_event("qinfo|coop_host_info|0")


def team_steps(t):
    """v2.2: revive, shared money, friendly fire, sleep; the fake host finally leaves the level."""
    if mode == "guest" and t > 30 and "revive" not in sent:
        sent.add("revive"); send_event("revive")
    if mode == "guest" and t > 45 and "money" not in sent:
        sent.add("money"); send_event("money|-200")
    if mode == "guest" and t > 50 and "ff" not in sent:
        sent.add("ff"); send_event("ffhit|0.200|7"); send_event("sleep|2")
    if mode == "host" and peer_pos and t > 60 and "mset" not in sent:
        sent.add("mset"); send_event("mset|77777")


reconnect = "reconnect" in sys.argv
spawn = "spawn" in sys.argv  # host mode: send a squad, a hit, a death and a removal with host ids
dropped = False
center = None; last_state = 0; last_ping = 0
delayed = []; last_release = 0  # fast mode: (release time, frame), delivered in order
npc, last_npc = None, 0  # npc mode: (id, section, x, y, z) picked by the guest
npc_path = sys.argv[sys.argv.index("npc") + 1] if "npc" in sys.argv else None
while time.time() - t0 < secs:
    if reconnect and mode == "guest" and not dropped and time.time() - t0 > secs / 2:
        # guest side: drop, reconnect and claim we are still in the host's world
        dropped = True
        tok = next((e.split("|", 1)[1] for e in got["events"] if e.startswith("session_ready|")), "")
        files_before = len(got["files"])
        s.close(); buf = b""
        time.sleep(2)
        s = socket.create_connection(("127.0.0.1", port)); s.sendall(frame(HELLO, PROTO + b"|BotGuest")); s.settimeout(0.05)
        s.sendall(frame(EVENT, ("resume|" + tok).encode(enc)))
        got["events"].append("--- reconnected, sent resume|" + tok)
        print("reconnected and sent resume with token", tok)
    if reconnect and mode == "host" and not dropped and time.time() - t0 > secs / 2:
        # simulate a network drop: close, wait for the guest launcher to come back, do NOT resend the session
        dropped = True
        s.close(); buf = b""
        print("dropped connection at", int(time.time() - t0), "s; waiting for reconnect")
        s, _ = srv.accept(); s.sendall(frame(HELLO, PROTO + b"|BotHost")); s.settimeout(0.05)
        got["events"].append("--- reconnected")
        print("guest reconnected at", int(time.time() - t0), "s")
    pump()
    if time.time() - last_ping > 2:
        last_ping = time.time(); s.sendall(frame(PING, b""))
    t = time.time() - t0
    # fast mode (v2.4): a state every 50 ms by the sender clock, each delivered 0-150 ms late, in order
    while delayed and delayed[0][0] <= time.time():
        s.sendall(delayed.pop(0)[1])
    interval = 0.05 if "fast" in sys.argv else 0.1 if "stamp" not in sys.argv else random.choice((0.05, 0.1, 0.1, 0.4))
    if peer_pos and time.time() - last_state > interval:
        last_state = time.time()
        lvl, x, y, z, lv, gv = peer_pos
        center = center or (x, y, z)
        a = t * (1.6 if "stamp" in sys.argv else 0.3)  # stamp mode: ~6.4 m/s, a sprinting player
        px, pz = center[0] + 4 * math.cos(a), center[2] + 4 * math.sin(a)
        if "team" in sys.argv and mode == "host" and t > 85:
            lvl = "jupiter"  # the fake host "leaves" (vertex ids stay Zaton's, so the follow reloads Zaton)
        st = f"s|p|{lvl}|{px:.2f}|{center[1]:.2f}|{pz:.2f}|{a:.2f}|{lv}|{gv}|s|1|wpn_ak74|||1.00|Bot"
        if "stamp" in sys.argv:
            # v2.3.3: sender clock in field 20; states sent in uneven bursts like a laggy link
            # actor mode (engine r5): a 2 s burst every 10 s (~10 shots/s), a reload 3 s after each burst
            shots = int(t // 10) * 20 + min(int((t % 10) * 10), 20) if "actor" in sys.argv else 0
            reloads = int((t + 5) // 10) if "actor" in sys.argv else 0
            st += f"|{shots}||{reloads}|0|0|{int(t * 1000)}"
            if "actor" in sys.argv:
                # v2.7 (engine r5): movement flags and angles of the sender's actor; it runs the circle
                # facing its centre, which makes it strafe (mcLStrafe | mcFwd = 5) like a player circling a target
                st += f"|5|{-a:.4f}:{-a:.4f}:0.0000"
        if "live" in sys.argv:
            # v2.3: stand still in front of the real player; reload, torch, healing, then a knockdown
            px, pz = center[0] + 3, center[2]
            reloads = int(t // 10) if t > 30 else 0
            torch = 1 if t > 40 else 0
            heal = 1 if 55 < t < 62 else 0
            hp = "0.35" if t < 70 else "0.10"
            st = f"s|p|{lvl}|{px:.2f}|{center[1]:.2f}|{pz:.2f}|0.00|{lv}|{gv}|s|0|wpn_ak74|||{hp}|Bot|0||{reloads}|{torch}|{heal}"
        lines = [st]
        if "live" in sys.argv and t > 70:
            lines.append("s|dn|1|45")
        if mode == "host":
            lines.append("s|w|0|||0")
            if npc_path and npc is None and os.path.exists(npc_path):
                f = open(npc_path).read().strip().split("|")
                if len(f) == 5:
                    npc = (f[0], f[1], float(f[2]), float(f[3]), float(f[4]))
                    print("npc picked", npc)
            if npc and time.time() - last_npc >= 0.2:
                # 5 times per second, like the host mod; running in a 3 m circle at ~2.4 m/s
                last_npc = time.time()
                na = (time.time() - t0) * 0.8
                nx, nz = npc[2] + 3 * math.cos(na), npc[4] + 3 * math.sin(na)
                lines.append(f"s|n|{int((time.time() - t0) * 1000)}|{npc[0]}:{npc[1]}:{nx:.2f}:{npc[3]:.2f}:{nz:.2f}:1.00:0")
            if "team" in sys.argv:
                lines.append("s|o|1|1|1")  # world state with game time 0: guest must not rewind
        f = frame(STATE, "\n".join(lines).encode(enc))
        if "fast" in sys.argv:
            last_release = max(last_release, time.time() + random.uniform(0, 0.15))
            delayed.append((last_release, f))
        else:
            s.sendall(f)
    if spawn and mode == "host" and peer_pos and t > 60 and "sqm" not in sent:
        sent.add("sqm")
        lvl, x, y, z, lv, gv = peer_pos
        # host ids far above anything in the guest's world, so they must be mapped
        send_event(f"sqm|60001|stalker_sim_squad_novice|60002|sim_default_stalker_0|{x + 3:.2f}|{y:.2f}|{z:.2f}|{lv}|{gv}|")
        send_event(f"sqm|60001|stalker_sim_squad_novice|60003|sim_default_stalker_0|{x + 4:.2f}|{y:.2f}|{z:.2f}|{lv}|{gv}|")
        send_event(f"art|60010|af_medusa|{x + 2:.2f}|{y:.2f}|{z:.2f}|{lv}|{gv}")
    if spawn and mode == "host" and t > 75 and "sdie" not in sent and "sqm" in sent:
        sent.add("sdie"); send_event("hit|60002|sim_default_stalker_0|0.400"); send_event("die|60002|sim_default_stalker_0")
    if spawn and mode == "host" and t > 85 and "sunreg" not in sent and "sqm" in sent:
        sent.add("sunreg"); send_event("unreg|60003|sim_default_stalker_0"); send_event("unreg|60001|stalker_sim_squad_novice")
    if "world" in sys.argv and mode == "host" and peer_pos and t > 65 and "world" not in sent:
        # ids from the shared session save (testA, Zaton / Skadovsk)
        sent.add("world")
        lvl, x, y, z, lv, gv = peer_pos
        send_event("door|13696|1")
        send_event("die|21564|sim_default_stalker_1")
        send_event("tstock|21552|zat_b22_stalker_medic|777|medkit:1.000:0;bandage:1.000:0;antirad:1.000:0")
        send_event(f"wspawn|60020|bandage|{x + 1:.2f}|{y:.2f}|{z:.2f}|{lv}|{gv}")
    if "world" in sys.argv and mode == "host" and peer_pos and t > 80 and "rest" not in sent:
        sent.add("rest")
        lvl, x, y, z, lv, gv = peer_pos
        send_event(f"pstash|70001|Тайник бота|{x + 1:.2f}|{y:.2f}|{z:.2f}|{lv}|{gv}|itm_backpack")
        send_event("tmark|13569|1|тест|bandage,medkit")
        send_event("surr|41763|sim_default_monolith_4")
    if "world" in sys.argv and mode == "host" and t > 72 and "loot" not in sent and "world" in sent:
        sent.add("loot"); send_event("loot|21564|sim_default_stalker_1|wpn_pm:0.500:0;ammo_9x18_fmj:1.000:16;bread:1.000:0")
    if "world" in sys.argv and mode == "guest" and t > 26 and "treq" not in sent and npcs:
        sent.add("treq")
        for e in npcs[:3]:
            nid, sec = e.split(":")[0], e.split(":")[1]
            if "stalker" in sec or "trader" in sec or "barmen" in sec or sec.startswith("zat"): send_event(f"treq|{nid}|{sec}")
    if "quest" in sys.argv:
        quest_steps(t)
    if "team" in sys.argv:
        team_steps(t)
    if "live" in sys.argv and center and t > 35 and "boom" not in sent:
        sent.add("boom"); send_event(f"boom|grenade_f1|{center[0] + 6:.2f}|{center[1]:.2f}|{center[2] + 2:.2f}")
    if "trade" in sys.argv and mode == "guest" and t > 40 and "gift" not in sent:
        sent.add("gift")
        send_event("gift|wpn_ak74:0.800:20:0:up_firsta_ak74+up_fiftha_ak74;ammo_5.45x39_fmj:1.000:15:0:;medkit:1.000:0:0:")
    if t > 20 and "chat" not in sent:
        sent.add("chat"); send_event("chat|Привет от бота|Bot")
    if t > 24 and "drop" not in sent and center:
        sent.add("drop"); send_event(f"drop|1|bandage|1.000|0|{center[0]:.2f}|{center[1]:.2f}|{center[2]:.2f}|{peer_pos[4]}|{peer_pos[5]}")
    if t > 28 and "kill" not in sent and npcs and "quest" not in sys.argv and "trade" not in sys.argv and "live" not in sys.argv:
        sent.add("kill")
        nid, sec = npcs[0].split(":")[0], npcs[0].split(":")[1]
        send_event(f"hit|{nid}|{sec}|0.300")
        send_event(f"die|{nid}|{sec}")

print("files:", got["files"])
if reconnect and mode == "guest":
    print("files after reconnect:", len(got["files"]) - files_before, "(0 = host did not resend the world)")
print("events:", got["events"])
print("state counts:", got["states"], "npcs listed:", len(npcs))
print("push doors moved:", got.get("doors", []))
print("dn states:", sorted(got.get("last_dn", [])), "o states:", sorted(got.get("last_o", [])))
