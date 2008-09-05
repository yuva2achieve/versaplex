using System;
using System.Collections.Generic;
using System.Linq;
using Wv.Extensions;

namespace Wv
{
    public class WvMiniBuf
    {
	byte[] bytes;
	int first, next;

	public WvMiniBuf(int size)
	{
	    bytes = new byte[size];
	    first = 0;
	    next = 0;
	}

	public int size { get { return (int)bytes.Length; } }

	public int used { get { return next-first; } }

	public int avail { get { return (int)bytes.Length-next; } }

	public void put(byte[] bytes, int offset, int len)
	{
	    wv.assert(len <= avail);
	    Array.Copy(bytes, offset, this.bytes, next, len);
	    next += len;
	}

	public void put(byte[] bytes)
	{
	    put(bytes, 0, (int)bytes.Length);
	}

	public byte[] peek(int len)
	{
	    wv.assert(len <= used);
	    byte[] ret = new byte[len];
	    Array.Copy(this.bytes, first, ret, 0, len);
	    return ret;
	}

	public byte[] get(int len)
	{
	    byte[] ret = peek(len);
	    first += len;
	    return ret;
	}

	public void unget(int len)
	{
	    wv.assert(first >= len);
	    first -= len;
	}

	// Returns the number of bytes that would have to be read in order to
	// get the first instance of 'b', or 0 if 'b' is not in the buffer.
	public int strchr(byte b)
	{
	    for (int i = first; i < next; i++)
		if (bytes[i] == b)
		    return i-first+1;
	    return 0;
	}
    }

    public class WvBuf
    {
	List<WvMiniBuf> list = new List<WvMiniBuf>();

	public WvBuf()
	{
	    zap();
	}

	public int used {
	    get {
		return (int)list.Select(b => (long)b.used).Sum();
	    }
	}

	WvMiniBuf last { get { return list[list.Count-1]; } }

	public void zap()
	{
	    list.Clear();
	    list.Add(new WvMiniBuf(10));
	}

	void addbuf(int len)
	{
	    int s = last.size;
	    while (s < len*2)
		s *= 2;
	    list.Add(new WvMiniBuf(s));
	}

	public void put(byte[] bytes, int offset, int len)
	{
	    if (last.avail < len)
		addbuf(len);
	    last.put(bytes, offset, len);
	}
	
	public void put(byte[] bytes)
	{
	    put(bytes, 0, (int)bytes.Length);
	}
	
	public void put(char c)
	{
	    put(c.ToUTF8());
	}
	
	public void put(string s)
	{
	    put(s.ToUTF8());
	}
	
	public void put(string fmt, params object[] args)
	{
	    put(String.Format(fmt, args));
	}

	int min(int a, int b)
	{
	    return (a < b) ? a : b;
	}

	void coagulate(int len)
	{
	    if (list[0].used < len)
	    {
		WvMiniBuf n = new WvMiniBuf(len);
		while (len > 0)
		{
		    int got = min(len, list[0].used);
		    n.put(list[0].get(got));
		    len -= got;
		    if (list[0].used == 0)
			list.Remove(list[0]);
		}
		list.Insert(0, n);
	    }
	}

	public byte[] peek(int len)
	{
	    wv.assert(used >= len);
	    coagulate(len);
	    return list[0].peek(len);
	}

	public byte[] get(int len)
	{
	    wv.assert(used >= len);
	    coagulate(len);
	    return list[0].get(len);
	}

	public byte[] getall()
	{
	    return get(used);
	}
	
	public string getstr()
	{
	    return getall().FromUTF8();
	}

	public void unget(int len)
	{
	    list[0].unget(len);
	}

	// Returns the number of bytes that would have to be read in order to
	// get the first instance of 'b', or 0 if 'b' is not in the buffer.
	public int strchr(byte b)
	{
	    int i = 0;
	    foreach (WvMiniBuf mb in list)
	    {
		int r = mb.strchr(b);
		if (r > 0)
		    return i + r;
		else
		    i += mb.used;
	    }
	    return 0;
	}

	public int strchr(char b)
	{
	    return strchr(Convert.ToByte(b));
	}
    }
}