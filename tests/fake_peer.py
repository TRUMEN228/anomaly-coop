"""Protocol-level fake partner for CoopLauncher (one real game per PC is all Anomaly allows).

  python fake_peer.py guest <port> <secs>            connect to a real host launcher on 127.0.0.1
  python fake_peer.py host  <port> <secs> <save dir> serve <save dir>/coop_session.* to a real guest launcher

The fake player circles around the real player's position, then sends chat, a drop, a hit and a kill
for the nearest creature from the host's creature list. Everything received is summarised at the end.
"""
import math, os, socket, struct, sys, time, zlib

HELLO, STATE, EVENT, FILE, PING, STATEZ = 1, 2, 3, 4, 5, 6
PROTO = b"2"
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

reconnect = "reconnect" in sys.argv
dropped = False
center = None; last_state = 0; last_ping = 0
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
    if peer_pos and time.time() - last_state > 0.1:
        last_state = time.time()
        lvl, x, y, z, lv, gv = peer_pos
        center = center or (x, y, z)
        a = t * 0.3
        px, pz = center[0] + 4 * math.cos(a), center[2] + 4 * math.sin(a)
        st = f"s|p|{lvl}|{px:.2f}|{center[1]:.2f}|{pz:.2f}|{a:.2f}|{lv}|{gv}|s|1|wpn_ak74|||1.00|Bot"
        lines = [st]
        if mode == "host":
            lines.append("s|w|0|||0")  # world state with game time 0: guest must not rewind
        s.sendall(frame(STATE, "\n".join(lines).encode(enc)))
    if t > 20 and "chat" not in sent:
        sent.add("chat"); send_event("chat|Привет от бота|Bot")
    if t > 24 and "drop" not in sent and center:
        sent.add("drop"); send_event(f"drop|1|bandage|1.000|0|{center[0]:.2f}|{center[1]:.2f}|{center[2]:.2f}|{peer_pos[4]}|{peer_pos[5]}")
    if t > 28 and "kill" not in sent and npcs:
        sent.add("kill")
        nid, sec = npcs[0].split(":")[0], npcs[0].split(":")[1]
        send_event(f"hit|{nid}|{sec}|0.300")
        send_event(f"die|{nid}|{sec}")

print("files:", got["files"])
if reconnect and mode == "guest":
    print("files after reconnect:", len(got["files"]) - files_before, "(0 = host did not resend the world)")
print("events:", got["events"][-10:])
print("state counts:", got["states"], "npcs listed:", len(npcs))
