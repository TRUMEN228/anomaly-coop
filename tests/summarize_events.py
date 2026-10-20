"""Print the replication events and spawn log lines from a QA run's captured output."""
import re, sys
raw = open(sys.argv[1], "rb").read()
text = raw.decode("utf-16") if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else raw.decode("utf-8", "replace")
for line in text.splitlines():
    if line.startswith("events:"):
        for e in re.findall(r"'([^']*)'", line):
            if re.match(r"(sqm|art|bspawn|wspawn|unreg|loot|tstock|door|pbreak|pstash|tmark|surr)\|", e):
                print("  ev", "|".join(e.split("|")[:4]))
    elif any(k in line for k in ("SPAWNTEST", "spawn:", "objects:", "loot ", "failed")):
        print(line)
