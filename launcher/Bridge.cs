using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CoopLauncher
{
    /// <summary>
    /// Relays between the local game (coop_in.txt / coop_out.txt in appdata) and the partner,
    /// over direct TCP or Steam (see ILink). A frame is a type byte followed by the payload.
    /// State frames larger than 512 bytes are deflate-compressed (MsgStateZ).
    /// </summary>
    public sealed class Bridge : IDisposable
    {
        public const string ProtocolVersion = "6";
        const byte MsgHello = 1, MsgState = 2, MsgEvent = 3, MsgFile = 4, MsgPing = 5, MsgStateZ = 6, MsgPong = 7;

        public static readonly Encoding GameEnc = Encoding.GetEncoding(1251);

        readonly string appdata;
        readonly string role;          // "host" | "client"
        readonly string myName;
        readonly object sync = new object();

        public event Action<string> Log;
        public event Action<string, string> Chat;          // name, text
        public event Action<bool, string> ConnectionChanged;
        public event Action<string> FileReceived;          // file name in savedgames

        TcpListener listener;
        volatile ILink link;
        Thread netThread, ipcThread;
        public long BytesSent, BytesReceived;
        volatile bool running = true;

        public bool Connected { get; private set; }
        /// <summary>Round trip to the partner in ms (0 until measured; older launchers do not answer pings).</summary>
        public int RttMs { get; private set; }

        // Outgoing frames go through a sender thread: the game's file exchange must never wait for the network.
        // States are coalesced (only the newest line per tag is sent); events and files keep their order.
        System.Collections.Concurrent.BlockingCollection<byte[]> outbox;
        readonly Dictionary<string, string> pendingState = new Dictionary<string, string>();
        public string PeerName { get; private set; } = "";
        /// <summary>Co-op engine revision this player runs, 0 for stock exes (see Engine.cs).</summary>
        public int EngineVersion { get; set; }

        // game -> partner
        string gameSid;
        long lastGameEvent;              // highest event id read from coop_out.txt
        readonly Dictionary<string, string> lastSentState = new Dictionary<string, string>();

        // partner/bridge -> game
        readonly string bridgeSid = new Random().Next(100000, 999999).ToString();
        readonly Dictionary<string, string> peerState = new Dictionary<string, string>();
        readonly List<KeyValuePair<long, string>> toGame = new List<KeyValuePair<long, string>>();
        long toGameSeq;
        DateTime lastGameOutWrite = DateTime.MinValue;
        public bool GameSeen => (DateTime.Now - lastGameOutWrite).TotalSeconds < 3;

        public Bridge(string gameDir, string role, string myName)
        {
            appdata = Path.Combine(gameDir, "appdata");
            Directory.CreateDirectory(appdata);
            this.role = role;
            this.myName = myName;
            ipcThread = new Thread(IpcLoop) { IsBackground = true, Name = "ipc" };
            ipcThread.Start();
        }

        // ------------------------------------------------------------------ network
        public void Host(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Log?.Invoke($"Ожидание напарника на порту {port}...");
            netThread = new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        var c = listener.AcceptTcpClient();
                        // the old link may not have timed out yet: the newest connection wins
                        var old = link;
                        if (old != null) { Log?.Invoke("Напарник подключился заново, старое соединение закрыто"); old.Dispose(); }
                        var l = new TcpLink(c);
                        new Thread(() => { try { Run(l); } catch (Exception e) when (running) { Log?.Invoke("Сеть: " + e.Message); } catch { } })
                            { IsBackground = true, Name = "peer" }.Start();
                    }
                    catch (Exception e) when (running) { Log?.Invoke("Сеть: " + e.Message); }
                    catch { return; }
                }
            }) { IsBackground = true, Name = "net" };
            netThread.Start();
        }

        public void Join(string host, int port)
        {
            netThread = new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        Log?.Invoke($"Подключение к {host}:{port}...");
                        var c = new TcpClient();
                        c.Connect(host, port);
                        Run(new TcpLink(c));
                    }
                    catch (Exception e) when (running) { Log?.Invoke("Сеть: " + e.Message); }
                    catch { return; }
                    for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
                }
            }) { IsBackground = true, Name = "net" };
            netThread.Start();
        }

        // ---- Steam: host makes a friends-only lobby; each new peer that talks to us becomes the partner
        public void HostSteam()
        {
            SteamNet.NewPeer += OnSteamPeer;
            SteamNet.LobbyCreated += lobby =>
            {
                Log?.Invoke("Steam-лобби создано. Друг увидит вас в списке «Друзья в Steam» в своём лаунчере.");
                SteamNet.SetPresence("Anomaly Co-op: ждёт напарника");
            };
            SteamNet.HostLobby();
        }

        void OnSteamPeer(SteamLink l)
        {
            if (!running) return;
            if (Connected) { l.Close(); return; }
            new Thread(() =>
            {
                try { Run(l); }
                catch (Exception e) when (running) { Log?.Invoke("Steam: " + e.Message); }
                catch { }
                l.Close();
            }) { IsBackground = true, Name = "steam-peer" }.Start();
        }

        // ---- Steam: guest joins the friend's lobby, then talks to the lobby owner; rejoins on loss
        public void JoinSteam(ulong lobby)
        {
            netThread = new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        // a connection through Steam's relays times out until the relay network is ready (status 100)
                        int relay = SteamNet.RelayStatus();
                        if (relay != 100)
                        {
                            Log?.Invoke($"Сеть Steam готовится (статус {relay}), подождите...");
                            for (int i = 0; i < 600 && running && (relay = SteamNet.RelayStatus()) != 100 && relay > -100; i++) Thread.Sleep(100);
                            Log?.Invoke(relay == 100 ? "Сеть Steam готова" : $"Сеть Steam не готова (статус {relay}), пробую всё равно");
                        }
                        Log?.Invoke("Вход в Steam-лобби друга...");
                        var entered = new ManualResetEventSlim();
                        bool ok = false;
                        Action<ulong, bool> onEnter = (l, success) => { if (l == lobby) { ok = success; entered.Set(); } };
                        SteamNet.LobbyEntered += onEnter;
                        SteamNet.JoinLobby(lobby);
                        entered.Wait(15000);
                        SteamNet.LobbyEntered -= onEnter;
                        if (!ok) throw new IOException("не удалось войти в лобби (друг закрыл лаунчер или лобби полное)");
                        var owner = SteamNet.LobbyOwner(lobby);
                        Log?.Invoke($"В лобби. Связываюсь с хостом через Steam (сеть Steam: {SteamNet.RelayStatus()})...");
                        Run(SteamNet.Connect(owner));
                    }
                    catch (Exception e) when (running) { Log?.Invoke("Steam: " + e.Message); }
                    catch { return; }
                    try { SteamNet.LeaveLobby(lobby); } catch (Exception e) { Log?.Invoke("Steam: " + e.Message); }
                    for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
                }
            }) { IsBackground = true, Name = "net" };
            netThread.Start();
        }

        /// <summary>Serve a ready link on a background thread (tests, custom transports).</summary>
        public void RunLink(ILink l)
        {
            new Thread(() => { try { Run(l); } catch { } }) { IsBackground = true, Name = "link" }.Start();
        }

        /// <summary>Serve one partner over a link until it drops.</summary>
        void Run(ILink l)
        {
            var q = new System.Collections.Concurrent.BlockingCollection<byte[]>();
            lock (pendingState) pendingState.Clear();
            outbox = q;
            link = l;
            resumeSent = false;
            RttMs = 0;
            try
            {
                l.Send(Frame(MsgHello, Encoding.UTF8.GetBytes(ProtocolVersion + "|" + myName + "|" + Updater.CurrentText + "|" + EngineVersion)));
                new Thread(() => SendLoop(l, q)) { IsBackground = true, Name = "send" }.Start();
                var pinger = new Thread(() =>
                {
                    while (running && link == l)
                    {
                        Send(MsgPing, BitConverter.GetBytes(DateTime.UtcNow.Ticks));
                        Thread.Sleep(3000);
                    }
                }) { IsBackground = true, Name = "ping" };
                pinger.Start();
                while (running)
                {
                    var f = l.Receive();
                    Interlocked.Add(ref BytesReceived, f.Length);
                    Handle(f[0], f, 1, f.Length - 1);
                }
            }
            finally
            {
                if (link == l) link = null;
                q.CompleteAdding();
                l.Dispose();
                Detach();
            }
        }

        static byte[] Frame(byte type, byte[] payload)
        {
            var frame = new byte[1 + payload.Length];
            frame[0] = type;
            payload.CopyTo(frame, 1);
            return frame;
        }

        // the only thread that writes to the link: a slow network delays it, not the game
        void SendLoop(ILink l, System.Collections.Concurrent.BlockingCollection<byte[]> q)
        {
            var lastState = DateTime.MinValue;
            try
            {
                while (running && link == l && !q.IsCompleted)
                {
                    if (q.TryTake(out var f, 15))
                    {
                        l.Send(f);
                        Interlocked.Add(ref BytesSent, f.Length);
                        continue;
                    }
                    // the game writes the partner's position every 50 ms, everything else every 100 ms
                    if ((DateTime.Now - lastState).TotalMilliseconds < 45) continue;
                    string text = null;
                    lock (pendingState)
                        if (pendingState.Count > 0)
                        {
                            text = string.Join("\n", pendingState.Values) + "\n";
                            pendingState.Clear();
                        }
                    if (text == null) continue;
                    lastState = DateTime.Now;
                    var frame = StateFrame(text);
                    l.Send(frame);
                    Interlocked.Add(ref BytesSent, frame.Length);
                }
            }
            catch (Exception e) when (running)
            {
                Log?.Invoke("Сеть: " + e.Message);
                l.Dispose(); // wakes the receive loop, which then cleans up
            }
            catch { }
        }

        void Detach()
        {
            bool was = Connected;
            Connected = false;
            lock (sync) { peerState.Clear(); lastSentState.Clear(); }
            lock (pendingState) pendingState.Clear();
            RttMs = 0;
            if (was)
            {
                Log?.Invoke($"{PeerName} отключился");
                ConnectionChanged?.Invoke(false, PeerName);
            }
        }

        void Send(byte type, byte[] payload)
        {
            var q = outbox;
            if (link == null || q == null) return;
            try { q.Add(Frame(type, payload)); } catch (InvalidOperationException) { } // link just closed
        }

        static byte[] StateFrame(string text)
        {
            var raw = GameEnc.GetBytes(text);
            if (raw.Length < 512) return Frame(MsgState, raw);
            using (var ms = new MemoryStream())
            {
                using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                    z.Write(raw, 0, raw.Length);
                return Frame(MsgStateZ, ms.ToArray());
            }
        }

        void Handle(byte type, byte[] buf, int off, int len)
        {
            switch (type)
            {
                case MsgHello:
                {
                    var parts = Encoding.UTF8.GetString(buf, off, len).Split('|');
                    if (parts[0] != ProtocolVersion)
                    {
                        Log?.Invoke($"Версии не совпадают: у вас {ProtocolVersion}, у напарника {parts[0]}. Обновите лаунчер.");
                        throw new IOException("version mismatch");
                    }
                    PeerName = parts.Length > 1 ? parts[1] : "Напарник";
                    Connected = true;
                    Log?.Invoke($"Подключён: {PeerName}");
                    // same protocol but different builds still play, yet fixes only work when both have them
                    var peerVer = parts.Length > 2 ? parts[2] : "старее 2.3.5";
                    if (peerVer != Updater.CurrentText)
                        Log?.Invoke($"У {PeerName} лаунчер {peerVer}, у вас {Updater.CurrentText}. Обновитесь оба до последней версии, иначе часть исправлений не работает.");
                    // the engine smooths the partner only on the side that runs it: both should match
                    int peerEngine = parts.Length > 3 && int.TryParse(parts[3], out var pe) ? pe : 0;
                    if (peerEngine != EngineVersion)
                        Log?.Invoke(peerEngine == 0 ? $"У {PeerName} обычный движок, у вас улучшенный: у него ваш персонаж будет двигаться по-старому. Пусть включит «Улучшенный движок» в опциях."
                            : EngineVersion == 0 ? $"У {PeerName} улучшенный движок, у вас обычный. Включите «Улучшенный движок» в опциях, чтобы напарник двигался плавно."
                            : $"У {PeerName} улучшенный движок r{peerEngine}, у вас r{EngineVersion}. Перезапустите лаунчер, чтобы обновиться.");
                    ConnectionChanged?.Invoke(true, PeerName);
                    break;
                }
                case MsgState:
                case MsgStateZ:
                {
                    string text;
                    if (type == MsgStateZ)
                    {
                        using (var z = new System.IO.Compression.DeflateStream(new MemoryStream(buf, off, len), System.IO.Compression.CompressionMode.Decompress))
                        using (var r = new StreamReader(z, GameEnc)) text = r.ReadToEnd();
                    }
                    else text = GameEnc.GetString(buf, off, len);
                    lock (sync)
                        foreach (var line in text.Replace("\r", "").Split('\n'))
                        {
                            var p = line.Split('|');
                            if (p.Length >= 2 && p[0] == "s") peerState[p[1]] = line;
                        }
                    break;
                }
                case MsgEvent:
                {
                    var text = GameEnc.GetString(buf, off, len);
                    if (text.StartsWith("resume"))
                    {
                        // only a guest still in the world we sent this run may skip the resend
                        var token = text.Length > 7 ? text.Substring(7) : "";
                        if (sessionToken != null && token == sessionToken) PeerResumed?.Invoke();
                        break;
                    }
                    if (text.StartsWith("session_ready"))
                    {
                        receivedToken = text.Length > 14 ? text.Substring(14) : null;
                        ToGame("session_ready");
                        SessionReady?.Invoke();
                        break;
                    }
                    if (text.StartsWith("chat|"))
                    {
                        var p = text.Split('|');
                        Chat?.Invoke(p.Length > 2 ? p[2] : PeerName, p.Length > 1 ? p[1] : "");
                    }
                    ToGame(text);
                    break;
                }
                case MsgPing:
                    if (len == 8) Send(MsgPong, Slice(buf, off, len));
                    break;
                case MsgPong:
                    if (len == 8)
                    {
                        var ms = (DateTime.UtcNow.Ticks - BitConverter.ToInt64(buf, off)) / TimeSpan.TicksPerMillisecond;
                        if (ms >= 0 && ms < 60000) RttMs = (int)ms;
                    }
                    break;
                case MsgFile:
                {
                    int nameLen = BitConverter.ToInt32(buf, off);
                    var name = Path.GetFileName(Encoding.UTF8.GetString(buf, off + 4, nameLen));
                    var dir = Path.Combine(appdata, "savedgames");
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, name);
                    using (var f = File.Create(path))
                        f.Write(buf, off + 4 + nameLen, len - 4 - nameLen);
                    FileReceived?.Invoke(name);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ API for the UI
        /// <summary>Queue an event for the local game (format "type|arg|arg").</summary>
        public void ToGame(string evt)
        {
            lock (sync) toGame.Add(new KeyValuePair<long, string>(++toGameSeq, evt));
        }

        public void SendChat(string text)
        {
            text = text.Replace('|', '/').Replace('\n', ' ');
            var evt = "chat|" + text + "|" + myName;
            ToGame(evt);
            if (Connected) Send(MsgEvent, GameEnc.GetBytes(evt));
        }

        public void SendSaveFiles(string saveName)
        {
            var dir = Path.Combine(appdata, "savedgames");
            foreach (var ext in new[] { ".scop", ".scoc", ".dds" })
            {
                var path = Path.Combine(dir, saveName + ext);
                if (!File.Exists(path)) continue;
                byte[] data = null;
                for (int i = 0; i < 50 && data == null; i++)
                {
                    try { data = File.ReadAllBytes(path); }
                    catch (IOException) { Thread.Sleep(100); }
                }
                if (data == null) { Log?.Invoke("Не удалось прочитать " + path); return; }
                var name = Encoding.UTF8.GetBytes(saveName + ext);
                var payload = new byte[4 + name.Length + data.Length];
                BitConverter.GetBytes(name.Length).CopyTo(payload, 0);
                name.CopyTo(payload, 4);
                data.CopyTo(payload, 4 + name.Length);
                Send(MsgFile, payload);
            }
            sessionToken = Guid.NewGuid().ToString("N").Substring(0, 12);
            Send(MsgEvent, GameEnc.GetBytes("session_ready|" + sessionToken));
            Log?.Invoke($"Сейв «{saveName}» отправлен напарнику");
        }

        public event Action<string> GameSaved;
        public event Action SessionReady;
        public event Action PeerResumed;          // host: a reconnecting guest is still in our world
        volatile bool resumeSent;
        volatile string sessionToken;             // host: id of the last world sent this run
        volatile string receivedToken;            // guest: id of the world we received this run                 // guest: told the host once per connection
        volatile bool gameInGuestSession;         // guest: the game reports it runs the host's session

        // ------------------------------------------------------------------ game IPC
        void IpcLoop()
        {
            var outPath = Path.Combine(appdata, "coop_out.txt");
            var inPath = Path.Combine(appdata, "coop_in.txt");
            var inTmp = inPath + ".tmp";
            DateTime lastOutStamp = DateTime.MinValue;
            long gameAck = 0;

            while (running)
            {
                try
                {
                    // read game output
                    var stamp = File.Exists(outPath) ? File.GetLastWriteTimeUtc(outPath) : DateTime.MinValue;
                    if (stamp != lastOutStamp)
                    {
                        string raw = null;
                        try { raw = File.ReadAllText(outPath, GameEnc); } catch (IOException) { }
                        if (raw != null && raw.Contains("\nend"))
                        {
                            lastOutStamp = stamp;
                            lastGameOutWrite = DateTime.Now;
                            gameAck = ParseGameOut(raw);
                        }
                    }

                    // write game input
                    var sb = new StringBuilder();
                    lock (sync)
                    {
                        toGame.RemoveAll(e => e.Key <= gameAck);
                        // the last field changes on every write: the game takes an unchanged file for a closed launcher,
                        // and with a partner standing still nothing else would change
                        sb.Append($"b|{bridgeSid}|{lastGameEvent}|{role}|{(Connected ? 1 : 0)}|{Clean(PeerName)}|{Clean(myName)}|{++heartbeat}\n");
                        foreach (var line in peerState.Values) sb.Append(line).Append('\n');
                        foreach (var e in toGame) sb.Append("e|").Append(e.Key).Append('|').Append(e.Value).Append('\n');
                    }
                    sb.Append("end\n");
                    // polled every 25 ms for the partner's position, but written only when something came in
                    // (or every 100 ms, to show the game the launcher is alive)
                    var body = sb.ToString();
                    var content = body.Substring(body.IndexOf('\n'));
                    if (content != lastInContent || (DateTime.Now - lastInWrite).TotalMilliseconds >= 100)
                    {
                        lastInContent = content;
                        lastInWrite = DateTime.Now;
                        File.WriteAllText(inTmp, body, GameEnc);
                        if (File.Exists(inPath)) File.Replace(inTmp, inPath, null);
                        else File.Move(inTmp, inPath);
                    }
                    else --heartbeat;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                catch (Exception e) { Log?.Invoke("IPC: " + e.Message); }
                Thread.Sleep(25);
            }
        }

        long heartbeat;
        string lastInContent;
        DateTime lastInWrite;

        static byte[] Slice(byte[] b, int off, int len) { var r = new byte[len]; Buffer.BlockCopy(b, off, r, 0, len); return r; }

        static string Clean(string s) => (s ?? "").Replace('|', ' ').Replace('\n', ' ');

        long ParseGameOut(string raw)
        {
            long ack = 0;
            var states = new List<string>();
            var events = new List<string>();
            foreach (var line in raw.Replace("\r", "").Split('\n'))
            {
                var p = line.Split('|');
                if (p[0] == "g" && p.Length >= 3)
                {
                    if (p[1] != gameSid)
                    {
                        // game (re)loaded: its event counter restarted, and it forgot our events
                        gameSid = p[1];
                        lastGameEvent = 0;
                        lock (sync) toGame.Clear();
                    }
                    long.TryParse(p[2], out ack);
                }
                else if (p[0] == "s" && p.Length >= 2)
                {
                    if (p[1] == "g") gameInGuestSession = p.Length > 2 && p[2] == "1";
                    else states.Add(line);
                }
                else if (p[0] == "e" && p.Length >= 3 && long.TryParse(p[1], out var id) && id > lastGameEvent)
                {
                    lastGameEvent = id;
                    events.Add(string.Join("|", p.Skip(2)));
                }
            }

            foreach (var e in events)
            {
                if (e.StartsWith("@"))
                {
                    var p = e.Split('|');
                    if (p[0] == "@saved" && p.Length > 1) GameSaved?.Invoke(p[1]);
                }
                else if (Connected) Send(MsgEvent, GameEnc.GetBytes(e));
            }

            if (Connected && role == "client" && gameInGuestSession && !resumeSent && receivedToken != null)
            {
                resumeSent = true;
                Send(MsgEvent, GameEnc.GetBytes("resume|" + receivedToken));
            }

            if (Connected)
            {
                // states that changed go to the sender, which sends the newest of each at most 20 times per second
                foreach (var line in states)
                {
                    var tag = line.Split('|')[1];
                    if (!lastSentState.TryGetValue(tag, out var prev) || prev != line)
                    {
                        lastSentState[tag] = line;
                        lock (pendingState) pendingState[tag] = line;
                    }
                }
            }
            return ack;
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { link?.Dispose(); } catch { }
            SteamNet.NewPeer -= OnSteamPeer;
            if (SteamNet.Ready) SteamNet.Leave();
        }
    }
}
