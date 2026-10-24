using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CoopLauncher
{
    /// <summary>
    /// Steam networking through Valve's free "Spacewar" app (AppID 480): friends-only lobbies,
    /// joins from the friends list and relayed P2P messages. No IPs, no port forwarding.
    /// Uses the flat C API of steam_api64.dll (Steamworks.NET standalone 2025.164.1, SHA-256
    /// eb17909a...fb75) with manual callback dispatch, as described in coop-anything's
    /// playbook/networking-steam-spacewar.md. x64 struct offsets.
    /// </summary>
    public static class SteamNet
    {
        const string Dll = "steam_api64.dll";
        const int CbLobbyJoinRequested = 333, CbRichPresenceJoinRequested = 337, CbLobbyEnter = 504,
            CbLobbyChatUpdate = 506, CbLobbyCreated = 513, CbCallCompleted = 703, CbSessionRequest = 1251, CbSessionFailed = 1252;
        const int SendReliableNoNagle = 8 | 1 | 32; // reliable, no Nagle, restart a broken session instead of failing
        const int Channel = 0;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_IsSteamRunning();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_InitFlat(byte[] err);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_GetHSteamPipe();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ManualDispatch_Init();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ManualDispatch_RunFrame(int pipe);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ManualDispatch_GetNextCallback(int pipe, IntPtr msg);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ManualDispatch_FreeLastCallback(int pipe);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ManualDispatch_GetAPICallResult(int pipe, ulong call, IntPtr buf, int size, int expected, out bool failed);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamUser_v023();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamMatchmaking_v009();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamMatchmaking_CreateLobby(IntPtr self, int type, int max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamMatchmaking_JoinLobby(IntPtr self, ulong lobby);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ISteamMatchmaking_LeaveLobby(IntPtr self, ulong lobby);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamMatchmaking_SetLobbyData(IntPtr self, ulong lobby, byte[] key, byte[] value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyOwner(IntPtr self, ulong lobby);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamFriends_v018();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamFriends_SetRichPresence(IntPtr self, byte[] key, byte[] value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ISteamFriends_ClearRichPresence(IntPtr self);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int i, int flags);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendRichPresence(IntPtr self, ulong id, byte[] key);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamNetworkingMessages_SendMessageToUser(IntPtr self, IntPtr identity, byte[] data, uint size, int flags, int channel);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(IntPtr self, int channel, IntPtr[] msgs, int max);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(IntPtr self, IntPtr identity);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern bool SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(IntPtr self, IntPtr identity);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_SteamNetworkingIdentity_SetSteamID64(IntPtr self, ulong id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern ulong SteamAPI_SteamNetworkingIdentity_GetSteamID64(IntPtr self);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr msg);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus(IntPtr self, IntPtr details);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(IntPtr self);

        static IntPtr user, mm, friends, msgs, utils;
        static int pipe;
        static Thread pump;
        public static bool Ready { get; private set; }
        public static ulong MySteamId { get; private set; }
        public static string MyName { get; private set; } = "";

        public static event Action<string> Log;
        public static event Action<ulong> LobbyCreated;
        public static event Action<ulong, bool> LobbyEntered;          // lobby, success
        public static event Action<ulong> JoinRequested;               // friend invited us / Join Game clicked
        public static event Action<ulong> PeerLeft;                    // a lobby member left

        // incoming frames by sender, completed after chunk reassembly
        static readonly ConcurrentDictionary<ulong, SteamLink> links = new ConcurrentDictionary<ulong, SteamLink>();
        public static event Action<SteamLink> NewPeer;                 // host: an unknown peer sent its first message
        static ulong acceptOnlyFrom;                                   // guest: 0 = accept anyone

        /// <summary>Extract the bundled DLL next to the exe, set AppID 480 and start Steam API.</summary>
        public static bool Init(out string error)
        {
            error = null;
            if (Ready) return true;
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                var dll = Path.Combine(dir, Dll);
                using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("steam\\" + Dll))
                using (var ms = new MemoryStream())
                {
                    res.CopyTo(ms);
                    var bytes = ms.ToArray();
                    if (!File.Exists(dll) || new FileInfo(dll).Length != bytes.Length) File.WriteAllBytes(dll, bytes);
                }
                File.WriteAllText(Path.Combine(dir, "steam_appid.txt"), "480");
                Environment.SetEnvironmentVariable("SteamAppId", "480");
                Environment.SetEnvironmentVariable("SteamGameId", "480");
                if (!SteamAPI_IsSteamRunning()) { error = "Steam не запущен. Запустите Steam и войдите в аккаунт."; return false; }
                // Steam binds its callback queue to the thread that initialises it: init on the pump thread itself,
                // or callbacks (incoming P2P sessions among them) get lost
                string fail = null;
                var done = new ManualResetEventSlim();
                pump = new Thread(() =>
                {
                    try
                    {
                        var err = new byte[1024];
                        int r = SteamAPI_InitFlat(err);
                        if (r != 0)
                        {
                            fail = r == 2 ? "Steam не запущен" : r == 3 ? "версия Steam не подходит, обновите Steam" : "Steam не инициализировался: " + Utf8(err);
                            return;
                        }
                        SteamAPI_ManualDispatch_Init();
                        pipe = SteamAPI_GetHSteamPipe();
                        user = SteamAPI_SteamUser_v023();
                        mm = SteamAPI_SteamMatchmaking_v009();
                        friends = SteamAPI_SteamFriends_v018();
                        msgs = SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
                        utils = SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
                        SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(utils);
                        MySteamId = SteamAPI_ISteamUser_GetSteamID(user);
                        MyName = Utf8(SteamAPI_ISteamFriends_GetPersonaName(friends));
                        Ready = true;
                    }
                    catch (Exception e) { fail = e is DllNotFoundException ? "не найден steam_api64.dll" : e.Message; return; }
                    finally { done.Set(); }
                    Pump();
                }) { IsBackground = true, Name = "steam" };
                pump.Start();
                if (!done.Wait(20000)) { error = "Steam не ответил за 20 с"; return false; }
                error = fail;
                return fail == null;
            }
            catch (DllNotFoundException) { error = "не найден steam_api64.dll"; return false; }
            catch (Exception e) { error = e.Message; return false; }
        }

        // ------------------------------------------------------------------ lobbies
        static ulong currentLobby;
        public static ulong CurrentLobby => currentLobby;

        // matchmaking and friends calls run on the pump thread too (Steam checks which thread uses its queue)
        static readonly System.Collections.Concurrent.ConcurrentQueue<Action> calls = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        static T OnPump<T>(Func<T> f)
        {
            if (Thread.CurrentThread == pump) return f();
            T result = default(T);
            Exception error = null;
            var done = new ManualResetEventSlim();
            calls.Enqueue(() => { try { result = f(); } catch (Exception e) { error = e; } finally { done.Set(); } });
            if (!done.Wait(10000)) throw new TimeoutException("Steam не ответил");
            if (error != null) throw error;
            return result;
        }

        static void OnPump(Action a) => OnPump(() => { a(); return 0; });

        public static void HostLobby()
        {
            // room for stale members: a guest that dropped stays listed for a while (the bridge takes one partner)
            OnPump(() => { SteamAPI_ISteamMatchmaking_CreateLobby(mm, 1 /* friends only */, 4); });
        }

        /// <summary>Steam Datagram Relay status: 100 = ready; lower values are still starting or failing.</summary>
        public static int RelayStatus()
        {
            if (!Ready) return -1;
            try { return OnPump(() => SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus(utils, IntPtr.Zero)); } catch { return -1; }
        }

        /// <summary>Guest: leave the friend's lobby before trying again, or the old membership fills it.</summary>
        public static void LeaveLobby(ulong lobby)
        {
            if (!Ready || lobby == 0) return;
            OnPump(() => SteamAPI_ISteamMatchmaking_LeaveLobby(mm, lobby));
            if (currentLobby == lobby) currentLobby = 0;
        }

        public static void JoinLobby(ulong lobby)
        {
            OnPump(() => { SteamAPI_ISteamMatchmaking_JoinLobby(mm, lobby); });
        }

        public static void Leave()
        {
            if (!Ready) return;
            
            try
            {
                var lobby = currentLobby;
                OnPump(() =>
                {
                    if (lobby != 0) SteamAPI_ISteamMatchmaking_LeaveLobby(mm, lobby);
                    SteamAPI_ISteamFriends_ClearRichPresence(friends);
                });
            }
            catch { }
            currentLobby = 0;
            foreach (var l in links.Values) l.Close();
            links.Clear();
            acceptOnlyFrom = 0;
        }

        public static ulong LobbyOwner(ulong lobby)
        {
            return OnPump(() => SteamAPI_ISteamMatchmaking_GetLobbyOwner(mm, lobby));
        }

        public static void SetPresence(string status)
        {
            
            OnPump(() =>
            {
                SteamAPI_ISteamFriends_SetRichPresence(friends, Z("status"), Z(status));
                if (currentLobby != 0) SteamAPI_ISteamFriends_SetRichPresence(friends, Z("connect"), Z("+connect_lobby " + currentLobby));
            });
        }

        public sealed class FriendLobby
        {
            public string Name; public ulong Lobby;
            public override string ToString() => Name;
        }

        /// <summary>Friends whose launcher is hosting a co-op lobby right now.</summary>
        public static List<FriendLobby> FriendLobbies()
        {
            var list = new List<FriendLobby>();
            if (!Ready) return list;
            OnPump(() =>
            {
                int n = SteamAPI_ISteamFriends_GetFriendCount(friends, 4 /* immediate */);
                for (int i = 0; i < n; i++)
                {
                    var id = SteamAPI_ISteamFriends_GetFriendByIndex(friends, i, 4);
                    var connect = Utf8(SteamAPI_ISteamFriends_GetFriendRichPresence(friends, id, Z("connect")));
                    if (!connect.StartsWith("+connect_lobby ")) continue;
                    if (!ulong.TryParse(connect.Substring(15).Trim(), out var lobby)) continue;
                    list.Add(new FriendLobby { Name = Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, id)), Lobby = lobby });
                }
            });
            return list;
        }

        // ------------------------------------------------------------------ messaging
        public static SteamLink Connect(ulong peer)
        {
            acceptOnlyFrom = peer;
            var l = new SteamLink(peer);
            links[peer] = l;
            return l;
        }

        internal static bool Send(ulong peer, byte[] data)
        {
            var id = Marshal.AllocHGlobal(160);
            try
            {
                for (int i = 0; i < 160; i++) Marshal.WriteByte(id, i, 0);
                SteamAPI_SteamNetworkingIdentity_SetSteamID64(id, peer);
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    int r;
                    r = SteamAPI_ISteamNetworkingMessages_SendMessageToUser(msgs, id, data, (uint)data.Length, SendReliableNoNagle, Channel);
                    if (r == 1) return true;              // k_EResultOK
                    if (r != 25 && r != 3) return false;  // LimitExceeded / NoConnection yet: retry
                    Thread.Sleep(5);
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(id); }
        }

        internal static void CloseSession(ulong peer, SteamLink who = null)
        {
            // an old link closing must not drop a newer one to the same peer
            if (who != null && links.TryGetValue(peer, out var current) && current != who) return;
            var id = Marshal.AllocHGlobal(160);
            try
            {
                for (int i = 0; i < 160; i++) Marshal.WriteByte(id, i, 0);
                SteamAPI_SteamNetworkingIdentity_SetSteamID64(id, peer);
                SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(msgs, id);
            }
            finally { Marshal.FreeHGlobal(id); }
            links.TryRemove(peer, out _);
        }

        // ------------------------------------------------------------------ callback pump
        static void Pump()
        {
            var cb = Marshal.AllocHGlobal(32);
            var inbox = new IntPtr[32];
            while (true)
            {
                try
                {
            
                    {
                        while (calls.TryDequeue(out var call)) call();
                        SteamAPI_ManualDispatch_RunFrame(pipe);
                        while (SteamAPI_ManualDispatch_GetNextCallback(pipe, cb))
                        {
                            int id = Marshal.ReadInt32(cb, 4);
                            IntPtr param = Marshal.ReadIntPtr(cb, 8);
                            int size = Marshal.ReadInt32(cb, 16);
                            if (id == CbCallCompleted)
                            {
                                ulong call = (ulong)Marshal.ReadInt64(param, 0);
                                int expected = Marshal.ReadInt32(param, 8);
                                int psize = Marshal.ReadInt32(param, 12);
                                var buf = Marshal.AllocHGlobal(Math.Max(psize, 8));
                                if (SteamAPI_ManualDispatch_GetAPICallResult(pipe, call, buf, psize, expected, out bool failed) && !failed)
                                    Handle(expected, buf);
                                Marshal.FreeHGlobal(buf);
                            }
                            else Handle(id, param);
                            SteamAPI_ManualDispatch_FreeLastCallback(pipe);
                        }

                        int n = SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(msgs, Channel, inbox, inbox.Length);
                        for (int i = 0; i < n; i++)
                        {
                            var m = inbox[i];
                            var data = Marshal.ReadIntPtr(m, 0);
                            int len = Marshal.ReadInt32(m, 8);
                            ulong from = SteamAPI_SteamNetworkingIdentity_GetSteamID64(IntPtr.Add(m, 16));
                            var bytes = new byte[len];
                            Marshal.Copy(data, bytes, 0, len);
                            SteamAPI_SteamNetworkingMessage_t_Release(m);
                            Deliver(from, bytes);
                        }
                    }
                }
                catch (Exception e) { Log?.Invoke("Steam: " + e.Message); Thread.Sleep(500); }
                Thread.Sleep(5);
            }
        }

        static void Deliver(ulong from, byte[] bytes)
        {
            if (!links.TryGetValue(from, out var link))
            {
                if (acceptOnlyFrom != 0 && from != acceptOnlyFrom) return;
                link = new SteamLink(from);
                links[from] = link;
                ThreadPool.QueueUserWorkItem(_ => NewPeer?.Invoke(link));
            }
            link.Feed(bytes);
        }

        static ulong lastCreated;
        static void Handle(int id, IntPtr p)
        {
            switch (id)
            {
                case CbLobbyCreated:
                {
                    int result = Marshal.ReadInt32(p, 0);
                    ulong lobby = (ulong)Marshal.ReadInt64(p, 8);
                    // arrives twice on real Steam (call result + broadcast): handle once
                    if (result != 1 || lobby == lastCreated) return;
                    lastCreated = lobby;
                    currentLobby = lobby;
                    SteamAPI_ISteamMatchmaking_SetLobbyData(mm, lobby, Z("anomaly_coop"), Z(Bridge.ProtocolVersion));
                    SteamAPI_ISteamFriends_SetRichPresence(friends, Z("connect"), Z("+connect_lobby " + lobby));
                    ThreadPool.QueueUserWorkItem(_ => LobbyCreated?.Invoke(lobby));
                    break;
                }
                case CbLobbyEnter:
                {
                    ulong lobby = (ulong)Marshal.ReadInt64(p, 0);
                    int response = Marshal.ReadInt32(p, 16);
                    if (lobby == lastCreated) return; // the host enters its own lobby too
                    if (response == 1) currentLobby = lobby;
                    ThreadPool.QueueUserWorkItem(_ => LobbyEntered?.Invoke(lobby, response == 1));
                    break;
                }
                case CbLobbyChatUpdate:
                {
                    ulong changed = (ulong)Marshal.ReadInt64(p, 8);
                    int state = Marshal.ReadInt32(p, 24);
                    if ((state & (0x2 | 0x4 | 0x8 | 0x10)) != 0)
                    {
                        if (links.TryGetValue(changed, out var l)) l.Close();
                        ThreadPool.QueueUserWorkItem(_ => PeerLeft?.Invoke(changed));
                    }
                    break;
                }
                case CbLobbyJoinRequested:
                {
                    ulong lobby = (ulong)Marshal.ReadInt64(p, 0);
                    ThreadPool.QueueUserWorkItem(_ => JoinRequested?.Invoke(lobby));
                    break;
                }
                case CbRichPresenceJoinRequested:
                {
                    var bytes = new byte[256];
                    Marshal.Copy(IntPtr.Add(p, 8), bytes, 0, 256);
                    var connect = Utf8(bytes);
                    if (connect.StartsWith("+connect_lobby ") && ulong.TryParse(connect.Substring(15).Trim(), out var lobby))
                        ThreadPool.QueueUserWorkItem(_ => JoinRequested?.Invoke(lobby));
                    break;
                }
                case CbSessionRequest:
                {
                    ulong from = SteamAPI_SteamNetworkingIdentity_GetSteamID64(p);
                    // host accepts anyone (the protocol hello decides); a guest accepts only its host
                    if (acceptOnlyFrom == 0 || from == acceptOnlyFrom)
                    {
                        bool ok = SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(msgs, p);
                        Log?.Invoke($"Steam: входящее соединение от {FriendName(from)}{(ok ? "" : " — не принято")}");
                    }
                    break;
                }
                case CbSessionFailed:
                {
                    // SteamNetConnectionInfo_t: identity (136), user data, listen socket, address, POPs, state @176, end reason @180, debug @184
                    ulong from = SteamAPI_SteamNetworkingIdentity_GetSteamID64(p);
                    int reason = Marshal.ReadInt32(p, 180);
                    var dbg = new byte[128];
                    Marshal.Copy(IntPtr.Add(p, 184), dbg, 0, 128);
                    Log?.Invoke($"Steam: связь с {FriendName(from)} не установлена (код {reason}: {Utf8(dbg)}). Сеть Steam: {RelayStatus()}");
                    if (links.TryGetValue(from, out var l)) l.Close();
                    break;
                }
            }
        }

        static string FriendName(ulong id) { try { var n = Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, id)); return n == "" ? "друга" : n; } catch { return "друга"; } }

        static byte[] Z(string s) { var b = Encoding.UTF8.GetBytes(s); Array.Resize(ref b, b.Length + 1); return b; }
        static string Utf8(byte[] z) { int n = Array.IndexOf(z, (byte)0); return Encoding.UTF8.GetString(z, 0, n < 0 ? z.Length : n); }
        static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0 && n < 4096) n++;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }
    }

    /// <summary>One Steam peer. Splits frames into 60000-byte pieces (header: 0 whole, 1 part, 2 last).</summary>
    public sealed class SteamLink : ILink
    {
        const int Piece = 60000;
        public readonly ulong Peer;
        readonly BlockingCollection<byte[]> frames = new BlockingCollection<byte[]>();
        readonly List<byte> partial = new List<byte>();
        volatile bool closed;
        DateTime lastHeard = DateTime.Now;

        readonly Func<ulong, byte[], bool> sender;
        readonly Action<ulong, SteamLink> closer;
        bool heard;                  // first contact through the relays can take a while

        public SteamLink(ulong peer) : this(peer, SteamNet.Send, (p, l) => SteamNet.CloseSession(p, l)) { }

        /// <summary>For tests: route messages somewhere other than Steam.</summary>
        public SteamLink(ulong peer, Func<ulong, byte[], bool> send, Action<ulong> close) : this(peer, send, (p, l) => close(p)) { }

        SteamLink(ulong peer, Func<ulong, byte[], bool> send, Action<ulong, SteamLink> close) { Peer = peer; sender = send; closer = close; }

        internal void Feed(byte[] msg)
        {
            if (msg.Length < 1) return;
            lastHeard = DateTime.Now;
            heard = true;
            if (msg[0] == 0) { frames.Add(Slice(msg)); return; }
            for (int i = 1; i < msg.Length; i++) partial.Add(msg[i]);
            if (msg[0] == 2) { frames.Add(partial.ToArray()); partial.Clear(); }
        }

        static byte[] Slice(byte[] m) { var b = new byte[m.Length - 1]; Buffer.BlockCopy(m, 1, b, 0, b.Length); return b; }

        public void Send(byte[] frame)
        {
            if (closed) throw new IOException("соединение закрыто");
            if (frame.Length <= Piece)
            {
                var m = new byte[frame.Length + 1];
                Buffer.BlockCopy(frame, 0, m, 1, frame.Length);
                if (!sender(Peer, m)) throw new IOException("Steam не доставил сообщение");
                return;
            }
            for (int off = 0; off < frame.Length; off += Piece)
            {
                int n = Math.Min(Piece, frame.Length - off);
                var m = new byte[n + 1];
                m[0] = (byte)(off + n >= frame.Length ? 2 : 1);
                Buffer.BlockCopy(frame, off, m, 1, n);
                if (!sender(Peer, m)) throw new IOException("Steam не доставил сообщение");
            }
        }

        public byte[] Receive()
        {
            while (true)
            {
                if (closed) throw new IOException("соединение закрыто");
                if (frames.TryTake(out var f, 500)) return f;
                // no disconnect event in SteamNetworkingMessages: silence means gone (pings come every 3 s)
                int limit = heard ? 20 : 45;
                if ((DateTime.Now - lastHeard).TotalSeconds > limit)
                    throw new IOException(heard ? "напарник не отвечает 20 с" : $"нет ответа от хоста за {limit} с (сеть Steam: {SteamNet.RelayStatus()})");
            }
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            try { closer(Peer, this); } catch { }
        }

        public void Dispose() => Close();
    }
}
