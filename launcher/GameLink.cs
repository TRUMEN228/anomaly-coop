using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace CoopLauncher
{
    /// <summary>
    /// Shared memory between the launcher and the game's co-op script, which opens it by name through LuaJIT FFI
    /// (the name goes to the game in coop_in.txt). Frames are the same text as the files, without the disk and the polling.
    /// Layout, little endian: a 64-byte header ("ACO1", slot size), then channel 0 (game -> launcher) and channel 1
    /// (launcher -> game). A channel is a 64-byte head (u32 seq, u32 length of slot 0, u32 length of slot 1) and two slots:
    /// frame n goes to slot n % 2 and its seq is written last, so a reader never takes a half-written frame.
    /// The game sets the event "&lt;name&gt;_out" after each frame it writes.
    /// </summary>
    sealed class GameLink : IDisposable
    {
        public const int Slot = 512 * 1024;
        const uint Magic = 0x314F4341; // "ACO1"
        const long Head = 64, Chan = 64 + 2L * Slot;

        public readonly string Name;
        public readonly EventWaitHandle GameWrote;
        readonly MemoryMappedFile map;
        readonly MemoryMappedViewAccessor view;
        readonly Encoding enc;
        uint readSeq, writeSeq;

        GameLink(string name, Encoding enc)
        {
            Name = name;
            this.enc = enc;
            map = MemoryMappedFile.CreateNew(name, Head + 2 * Chan);
            view = map.CreateViewAccessor();
            view.Write(4, (uint)Slot);
            view.Write(0, Magic);
            GameWrote = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_out");
        }

        /// <summary>A new block for this run, or null if Windows refuses one (then the files stay).</summary>
        public static GameLink TryCreate(Encoding enc)
        {
            try { return new GameLink("AnomalyCoop_" + Guid.NewGuid().ToString("N").Substring(0, 12), enc); }
            catch { return null; }
        }

        /// <summary>The game's newest frame, or null when it wrote nothing new.</summary>
        public string Read()
        {
            uint seq = view.ReadUInt32(Head);
            if (seq == readSeq) return null;
            int slot = (int)(seq % 2);
            int len = view.ReadInt32(Head + 4 + 4 * slot);
            if (len < 0 || len > Slot) return null;
            var buf = new byte[len];
            view.ReadArray(Head + 64 + slot * (long)Slot, buf, 0, len);
            Thread.MemoryBarrier();
            if (view.ReadUInt32(Head) != seq) return null; // the game wrote twice meanwhile: take the next one
            readSeq = seq;
            return enc.GetString(buf);
        }

        /// <summary>A frame for the game; false if it does not fit (then it goes by file).</summary>
        public bool Write(string text)
        {
            var bytes = enc.GetBytes(text);
            if (bytes.Length > Slot) return false;
            long b = Head + Chan;
            uint seq = writeSeq + 1;
            int slot = (int)(seq % 2);
            view.WriteArray(b + 64 + slot * (long)Slot, bytes, 0, bytes.Length);
            view.Write(b + 4 + 4 * slot, bytes.Length);
            Thread.MemoryBarrier();
            view.Write(b, seq);
            writeSeq = seq;
            return true;
        }

        public void Dispose()
        {
            view.Dispose();
            map.Dispose();
            GameWrote.Dispose();
        }
    }
}
