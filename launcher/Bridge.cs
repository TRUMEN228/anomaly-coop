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
    /// Relays between the local game (coop_in.txt / coop_out.txt in appdata) and the partner (TCP).
    /// Frames on the wire: int32 length, byte type, payload.
    /// </summary>
    public sealed class Bridge : IDisposable
    {
        public const string ProtocolVersion = "1";
        const byte MsgHello = 1, MsgState = 2, MsgEvent = 3, MsgFile = 4, MsgPing = 5;

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
        TcpClient client;
        NetworkStream stream;
        Thread netThread, ipcThread;
        volatile bool running = true;

        public bool Connected { get; private set; }
        public string PeerName { get; private set; } = "";

        // game -> partner
        string gameSid;
        long lastGameEvent;              // highest event id read from coop_out.txt
        readonly Dictionary<string, string> lastSentState = new Dictionary<string, string>();
        DateTime lastStateSend = DateTime.MinValue;

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
                        if (Connected) { c.Close(); continue; }
                        Attach(c);
                        ReadLoop();
                    }
                    catch (Exception e) when (running) { Log?.Invoke("Сеть: " + e.Message); }
                    catch { return; }
                    Detach();
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
                        Attach(c);
                        ReadLoop();
                    }
                    catch (Exception e) when (running) { Log?.Invoke("Сеть: " + e.Message); }
                    catch { return; }
                    Detach();
                    for (int i = 0; i < 30 && running; i++) Thread.Sleep(100);
                }
            }) { IsBackground = true, Name = "net" };
            netThread.Start();
        }

        void Attach(TcpClient c)
        {
            c.NoDelay = true;
            client = c;
            stream = c.GetStream();
            resumeSent = false;
            Send(MsgHello, Encoding.UTF8.GetBytes(ProtocolVersion + "|" + myName));
        }

        void Detach()
        {
            bool was = Connected;
            Connected = false;
            try { client?.Close(); } catch { }
            client = null; stream = null;
            lock (sync) { peerState.Clear(); lastSentState.Clear(); }
            if (was)
            {
                Log?.Invoke($"{PeerName} отключился");
                ConnectionChanged?.Invoke(false, PeerName);
            }
        }

        void ReadLoop()
        {
            var s = stream;
            var lenBuf = new byte[4];
            var lastPing = DateTime.Now;
            client.ReceiveTimeout = 15000;
            var pinger = new Thread(() =>
            {
                while (running && s == stream && client != null)
                {
                    try { Send(MsgPing, new byte[0]); } catch { return; }
                    Thread.Sleep(3000);
                }
            }) { IsBackground = true };
            pinger.Start();

            while (running)
            {
                ReadExact(s, lenBuf, 4);
                int len = BitConverter.ToInt32(lenBuf, 0);
                if (len < 1 || len > 64 * 1024 * 1024) throw new IOException("bad frame");
                var buf = new byte[len];
                ReadExact(s, buf, len);
                Handle(buf[0], buf, 1, len - 1);
            }
        }

        static void ReadExact(Stream s, byte[] buf, int n)
        {
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) throw new IOException("соединение закрыто");
                off += r;
            }
        }

        void Send(byte type, byte[] payload)
        {
            var s = stream;
            if (s == null) return;
            var frame = new byte[5 + payload.Length];
            BitConverter.GetBytes(payload.Length + 1).CopyTo(frame, 0);
            frame[4] = type;
            payload.CopyTo(frame, 5);
            lock (s) s.Write(frame, 0, frame.Length);
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
                    ConnectionChanged?.Invoke(true, PeerName);
                    break;
                }
                case MsgState:
                {
                    var text = GameEnc.GetString(buf, off, len);
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
                        sb.Append($"b|{bridgeSid}|{lastGameEvent}|{role}|{(Connected ? 1 : 0)}|{Clean(PeerName)}|{Clean(myName)}\n");
                        foreach (var line in peerState.Values) sb.Append(line).Append('\n');
                        foreach (var e in toGame) sb.Append("e|").Append(e.Key).Append('|').Append(e.Value).Append('\n');
                    }
                    sb.Append("end\n");
                    File.WriteAllText(inTmp, sb.ToString(), GameEnc);
                    if (File.Exists(inPath)) File.Replace(inTmp, inPath, null);
                    else File.Move(inTmp, inPath);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                catch (Exception e) { Log?.Invoke("IPC: " + e.Message); }
                Thread.Sleep(50);
            }
        }

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
                // send states that changed, at most 10 times per second
                if ((DateTime.Now - lastStateSend).TotalMilliseconds >= 90)
                {
                    var changed = new StringBuilder();
                    foreach (var line in states)
                    {
                        var tag = line.Split('|')[1];
                        if (!lastSentState.TryGetValue(tag, out var prev) || prev != line)
                        {
                            lastSentState[tag] = line;
                            changed.Append(line).Append('\n');
                        }
                    }
                    if (changed.Length > 0)
                    {
                        Send(MsgState, GameEnc.GetBytes(changed.ToString()));
                        lastStateSend = DateTime.Now;
                    }
                }
            }
            return ack;
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { client?.Close(); } catch { }
        }
    }
}
