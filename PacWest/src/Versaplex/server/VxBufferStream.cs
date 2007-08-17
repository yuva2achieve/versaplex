using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace versabanq.Versaplex.Server {

public class VxBufferStream : Stream
{
    private static int streamcount = 0;
    public readonly int streamno = System.Threading.Interlocked.Increment(ref streamcount);

    private bool closed = false;

    private bool eof = false;
    public bool Eof {
        get { return eof; }
    }

    public override bool CanRead { get { return true; } }
    public override bool CanWrite { get { return true; } }
    public override bool CanSeek { get { return false; } }

    public override long Length {
        get { throw new NotSupportedException(); }
    }
    public override long Position {
        get { throw new NotSupportedException(); }
        set { throw new NotSupportedException(); }
    }

    private object cookie = null;
    public object Cookie {
        get { return cookie; }
        set { cookie = value; }
    }

    public delegate void DataReadyHandler(object sender, object cookie);
    public event DataReadyHandler DataReady;
    public event DataReadyHandler NoMoreData;

    protected VxNotifySocket sock;

    // Maximum value for rbuf_used to take; run the DataReady event when this
    // has filled
    private long rbuf_size = 0;

    protected Buffer rbuf = new Buffer();
    protected Buffer wbuf = new Buffer();

    public VxBufferStream(VxNotifySocket sock)
    {
        sock.Blocking = false;
        sock.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.KeepAlive, true);
        sock.ReadReady += OnReadable;
        sock.WriteReady += OnWritable;

        this.sock = sock;
    }

    public long BufferAmount {
        get { return rbuf_size; }
        set {
            if (value < 0)
                throw new ArgumentOutOfRangeException(
                        "BufferAmount must be nonnegative");

            rbuf_size = value;

            if (rbuf.Size >= rbuf_size) {
                ReadWaiting = false;

                if (rbuf.Size > 0) {
                    VxEventLoop.AddAction(new VxEvent(
                                delegate() {
                                    DataReady(this, cookie);
                                }));
                }
            } else {
                ReadWaiting = true;
            }
        }
    }

    public int BufferPending {
        get { return rbuf.Size; }
    }

    public bool IsDataReady {
        get { return rbuf_size <= rbuf.Size; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            Flush();

            rbuf.Clear();
            ReadWaiting = false;

            wbuf.Clear();
            WriteWaiting = false;

            if (sock != null) {
                sock.Close();
                sock = null;
            }

            cookie = null;
        }

        closed = true;

        base.Dispose(disposing);
    }

    public override void Flush()
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        if (wbuf.Size == 0)
            return;

        try {
            sock.Blocking = true;

            byte[] buf = new byte[wbuf.Size];
            wbuf.Retrieve(buf, 0, buf.Length);

            int sofar = 0;

            do {
                int amt = sock.Send(buf, sofar, buf.Length-sofar, SocketFlags.None);

                // This shouldn't happen, but checking for it guarantees that
                // this method will complete eventually
                if (amt <= 0)
                    throw new Exception("Send returned " + amt);

                sofar += amt;
            } while (sofar < buf.Length);
        } finally {
            sock.Blocking = false;
        }
    }

    public override int Read(
            [InAttribute] [OutAttribute] byte[] buffer,
            int offset,
            int count)
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        return rbuf.Retrieve(buffer, offset, count);
    }

    public override int ReadByte()
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        return rbuf.RetrieveByte();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long len)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        Console.WriteLine("{0} Write", streamno);
        wbuf.Append(buffer, offset, count);
        WriteWaiting = true;
        Console.WriteLine("{1} Written {0}", wbuf.Size, streamno);
    }

    public override void WriteByte(byte value)
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        wbuf.AppendByte(value);
        WriteWaiting = true;
    }

    protected bool read_waiting = false;
    protected bool ReadWaiting {
        get { return read_waiting; }
        set {
            if (!read_waiting && value) {
                VxEventLoop.RegisterRead(sock);
            } else if (read_waiting && !value) {
                VxEventLoop.UnregisterRead(sock);
            }

            read_waiting = value;
        }
    }

    protected bool write_waiting = false;
    protected bool WriteWaiting {
        get { return write_waiting; }
        set {
            if (!write_waiting && value) {
                VxEventLoop.RegisterWrite(sock);
            } else if (write_waiting && !value) {
                VxEventLoop.UnregisterWrite(sock);
            }

            write_waiting = value;
        }
    }

    protected virtual bool OnReadable(object sender)
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        const int READSZ = 16384;

        try {
            byte[] data = new byte[READSZ];

            while (rbuf.Size < rbuf_size) {
                Console.WriteLine("{1} Attempting receive (available = {0})", sock.Available, streamno);

                int amt = sock.Receive(data);

                if (amt == 0) {
                    eof = true;
                    break;
                }

                rbuf.Append(data, 0, amt);
            }
        } catch (SocketException e) {
            if (e.ErrorCode != (int)SocketError.WouldBlock) {
                throw e;
            }
        }

        if (rbuf.Size >= rbuf_size) {
            DataReady(this, cookie);
        }

        if (eof) {
            NoMoreData(this, cookie);
        }

        // Don't use ReadWaiting to change this since the return value will
        // determine the fate in the event loop, far more efficiently than
        // generic removal
        read_waiting = !eof && rbuf.Size < rbuf_size;
        return read_waiting;
    }

    protected virtual bool OnWritable(object sender)
    {
        if (closed)
            throw new ObjectDisposedException("Stream is closed");

        try {
            Console.WriteLine("{1} Writable {0}", wbuf.Size, streamno);

            int offset, length;
            byte[] buf = wbuf.RetrieveBuf(out offset, out length);

            if (buf == null)
                throw new Exception("Writable handler called with nothing to write");

            int amt = sock.Send(buf, offset, length, SocketFlags.None);

            wbuf.Discard(amt);
        } catch (SocketException e) {
            if (e.ErrorCode != (int)SocketError.WouldBlock) {
                throw e;
            }
        }

        // Don't use WriteWaiting to change this since the return value will
        // determine the fate in the event loop, far more efficiently than
        // generic removal
        write_waiting = wbuf.Size > 0;
        return write_waiting;
    }

    protected class Buffer {
        private const int BUFCHUNKSZ = 16384;

        private int buf_start = 0;
        private int buf_end = BUFCHUNKSZ;

        private LinkedList<byte[]> buf = new LinkedList<byte[]>();

        // Number of bytes that can be retrieved
        public int Size
        {
            get {
                switch (buf.Count) {
                    case 0:
                        return 0;
                    default:
                        return buf.Count * BUFCHUNKSZ + buf_end - buf_start - BUFCHUNKSZ;
                }
            }
        }

        // Number of bytes between buf_start and end of first buffer
        private int FirstUsed
        {
            get {
                switch (buf.Count) {
                    case 0:
                        return 0;
                    case 1:
                        return buf_end - buf_start;
                    default:
                        return BUFCHUNKSZ - buf_start;
                }
            }
        }

        // Number of bytes between buf_end and end of last buffer
        private int LastLeft
        {
            get {
                switch (buf.Count) {
                    case 0:
                        return 0;
                    default:
                        return BUFCHUNKSZ - buf_end;
                }
            }
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            if (offset+count > buffer.Length)
                throw new ArgumentException(
                        "offset+count is larger than buffer length");
            if (buffer == null)
                throw new ArgumentNullException("buffer is null");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset is negative");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count is negative");
            

            int sofar = 0;

            while (sofar < count) {
                while (LastLeft == 0)
                    Expand();

                int amt = Math.Min(count - sofar, LastLeft);

                Console.WriteLine("Copy buf({3}), {0}, internal, {1}, {2}, count {4}", sofar+offset, buf_end, amt, buffer.Length, buf.Count);
                Array.Copy(buffer, sofar+offset, buf.Last.Value,
                        buf_end, amt);

                buf_end += amt;
                sofar += amt;
            }
        }

        public void AppendByte(byte data)
        {
            while (LastLeft == 0)
                Expand();

            buf.Last.Value[buf_end] = data;

            buf_end++;
        }

        public int Retrieve(byte[] buffer, int offset, int count)
        {
            if (offset+count > buffer.Length)
                throw new ArgumentException(
                        "offset + count larger than buffer length");
            if (buffer == null)
                throw new ArgumentNullException("buffer is null");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset is negative");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count is negative");

            if (count > Size) {
                return -1;
            }

            int sofar = 0;

            while (sofar < count) {
                while (FirstUsed == 0) {
                    // buf.Count > 0 will be true because we know from above
                    // that buffer.Length <= original size. Thus if buf becomes
                    // completely emptied, then sofar >= buffer.Length and
                    // the loop would not have repeated.
                    Contract();
                }

                int amt = Math.Min(count - sofar, FirstUsed);

                Array.ConstrainedCopy(buf.First.Value, buf_start, buffer,
                        sofar+offset, amt);

                buf_start += amt;
                sofar += amt;
            }

            OptimizeBuf();

            return sofar;
        }

        public int RetrieveByte()
        {
            if (Size == 0)
                return -1;

            while (FirstUsed == 0)
                Contract();

            int result = buf.First.Value[buf_start];

            buf_start++;
            OptimizeBuf();

            return result;
        }

        public byte[] RetrieveBuf(out int offset, out int count)
        {
            while (FirstUsed == 0) {
                if (buf.Count == 0) {
                    offset = 0;
                    count = 0;
                    return null;
                }

                Contract();
            }

            if (buf.Count == 0) {
                offset = 0;
                count = 0;
                return null;
            }

            offset = buf_start;
            count = FirstUsed;

            return buf.First.Value;
        }

        public void Discard(int amt)
        {
            if (amt < 0)
                throw new ArgumentOutOfRangeException("amount is negative");

            int cursize = FirstUsed;

            while (amt > cursize) {
                Contract();
                amt -= cursize;

                cursize = FirstUsed;
            }

            buf_start += amt;

            OptimizeBuf();
        }

        public void Clear()
        {
            buf.Clear();
            buf_start = 0;
            buf_end = BUFCHUNKSZ;
        }

        private void OptimizeBuf()
        {
            // If we finished reading the first buffer, contract it
            while (buf.Count > 1 && FirstUsed == 0) {
                Contract();
            }

            // Slight optimization for if the buffer is completely drained
            // to possibly avoid extra expansions later on
            if (buf_start == buf_end && buf.Count == 1) {
                buf_start = 0;
                buf_end = 0;
            }
        }
        
        private void Expand()
        {
            buf.AddLast(new byte[BUFCHUNKSZ]);
            buf_end = 0;
        }

        private void Contract()
        {
            buf.RemoveFirst();
            buf_start = 0;
        }
    }
}

}
