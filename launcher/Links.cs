using System;
using System.IO;
using System.Net.Sockets;

namespace CoopLauncher
{
    /// <summary>A connection to the partner that carries whole frames (type byte + payload).</summary>
    public interface ILink : IDisposable
    {
        void Send(byte[] frame);
        /// <summary>Blocks until a frame arrives; throws IOException when the link is gone.</summary>
        byte[] Receive();
    }

    /// <summary>Direct TCP: frames are prefixed with an int32 length.</summary>
    public sealed class TcpLink : ILink
    {
        readonly TcpClient client;
        readonly NetworkStream stream;
        readonly byte[] lenBuf = new byte[4];

        public TcpLink(TcpClient c)
        {
            client = c;
            c.NoDelay = true;
            c.ReceiveTimeout = 15000; // pings arrive every 3 s
            stream = c.GetStream();
        }

        public void Send(byte[] frame)
        {
            var buf = new byte[4 + frame.Length];
            BitConverter.GetBytes(frame.Length).CopyTo(buf, 0);
            frame.CopyTo(buf, 4);
            lock (stream) stream.Write(buf, 0, buf.Length);
        }

        public byte[] Receive()
        {
            ReadExact(lenBuf, 4);
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len < 1 || len > 64 * 1024 * 1024) throw new IOException("bad frame");
            var buf = new byte[len];
            ReadExact(buf, len);
            return buf;
        }

        void ReadExact(byte[] buf, int n)
        {
            int off = 0;
            while (off < n)
            {
                int r = stream.Read(buf, off, n - off);
                if (r <= 0) throw new IOException("соединение закрыто");
                off += r;
            }
        }

        public void Dispose() { try { client.Close(); } catch { } }
    }
}
