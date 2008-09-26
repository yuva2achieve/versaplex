using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using Wv.Mono.Terminal;
using Wv;
using Wv.Extensions;
using NDesk.DBus;

namespace Wv
{
    public class VxDbException : DbException
    {
        public VxDbException(string msg) : base(msg)
	{
	}
    }
    
    [WvMoniker]
    public class WvDbi_Versaplex : WvDbi
    {
	Bus bus;
	
	struct ColInfo
	{
	    public int size;
	    public string name;
	    public string type;
	    public short precision;
	    public short scale;
	    public byte nullable;
	}
	
	struct Stupid
	{
	    public string s;
	}
	
	public static void wvmoniker_register()
	{
	    WvMoniker<WvDbi>.register("vx",
		 (string m, object o) => new WvDbi_Versaplex());
	}
	
	public WvDbi_Versaplex()
	{
	    if (Address.Session == null)
		throw new Exception ("DBUS_SESSION_BUS_ADDRESS not set");
	    AddressEntry aent = AddressEntry.Parse(Address.Session);
	    DodgyTransport trans = new DodgyTransport();
	    trans.Open(aent);
	    bus = new Bus(trans);
	}
	
	public override WvSqlRows select(string sql, params object[] args)
	{
	    Message call 
		= VxDbusUtils.CreateMethodCall(bus, "ExecRecordset", "s");
	    MessageWriter writer 
		= new MessageWriter(Connection.NativeEndianness);

	    writer.Write(typeof(string), sql);
	    call.Body = writer.ToArray();
	    
	    log.print("Sending!\n");
	    
	    Message reply = call.Connection.SendWithReplyAndBlock(call);
	    
	    log.print("Answer came back!\n");

	    switch (reply.Header.MessageType) 
	    {
	    case MessageType.MethodReturn:
	    case MessageType.Error:
		{
		    object replysig;
		    if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
							 out replysig))
			throw new Exception("D-Bus reply had no signature.");
		    
		    if (replysig == null)
			throw new Exception("D-Bus reply had null signature");
		    
		    MessageReader reader = new MessageReader(reply);
		    
		    // Some unexpected error
		    if (replysig.ToString() == "s")
			throw new VxDbException(reader.ReadString());
		    
		    if (replysig.ToString() != "a(issnny)vaay")
			throw new 
			  Exception("D-Bus reply had invalid signature: " +
				    replysig);
		    
		    // decode the raw column info
		    ColInfo[] x = (ColInfo[])reader.ReadArray(typeof(ColInfo));
		    WvColInfo[] colinfo
			= (from c in x
			   select new WvColInfo(c.name, typeof(string),
						(c.nullable & 1) != 0,
						c.size, c.precision, c.scale))
			    .ToArray();
		    
		    Signature sig = reader.ReadSignature();
		    log.print("Variant signature: '{0}'\n", sig);
		    
		    WvSqlRow[] rows;
		    if (sig.ToString() == "a(s)")
		    {
			Stupid[] a = (Stupid[])reader.ReadArray(typeof(Stupid));
			rows = (from r in a
				select new WvSqlRow(new object[] { r.s },
						    colinfo))
			    .ToArray();
		    }
		    else
			rows = new WvSqlRow[0];
		    
		    return new WvSqlRows_Versaplex(rows, colinfo);
		}
	    default:
		throw new Exception("D-Bus response was not a method "
				    + "return or error");
	    }
	}
	
	public override int execute(string sql, params object[] args)
	{
	    using (select(sql, args))
		return 0;
	}
    }
    
    class WvSqlRows_Versaplex : WvSqlRows, IEnumerable<WvSqlRow>
    {
	WvSqlRow[] rows;
	WvColInfo[] schema;
	
	public WvSqlRows_Versaplex(WvSqlRow[] rows, WvColInfo[] schema)
	{
	    this.rows = rows;
	    this.schema = schema;
	}
	
	public override IEnumerable<WvColInfo> columns
	    { get { return schema; } }

	public override IEnumerator<WvSqlRow> GetEnumerator()
	{
	    foreach (var row in rows)
		yield return row;
	}
    }
}

public static class VxCli
{
    public static int Main(string[] args)
    {
	WvLog.maxlevel = WvLog.L.Debug;
	WvLog log = new WvLog("vxcli");

	if (args.Length != 1)
	{
	    Console.Error.WriteLine("Usage: vxcli <db-connection-string>");
	    return 1;
	}
	
	string moniker = args[0];
	
	WvIni vxini = new WvIni("versaplexd.ini");
	if (vxini.get("Connections", moniker) != null)
	    moniker = vxini.get("Connections", moniker);
	
	WvIni bookmarks = new WvIni(
		    wv.PathCombine(wv.getenv("HOME"), ".wvdbi.ini"));
	if (!moniker.Contains(":")
	    && bookmarks["Bookmarks"].ContainsKey(moniker))
	{
	    moniker = bookmarks["Bookmarks"][moniker];
	}
	else
	{
	    // not found in existing bookmarks, so see if we can parse and
	    // save instead.
	    WvUrl url = new WvUrl(moniker);
	    string path = url.path;
	    if (path.StartsWith("/"))
		path = path.Substring(1);
	    if (path != "" && url.host != null)
	    {
		log.print("Creating bookmark '{0}'\n", path);
		bookmarks.set("Bookmarks", path, moniker);
		try {
		    bookmarks.save();
		} catch (IOException) {
		    // not a big deal if we can't save our bookmarks.
		}
	    }
	}
	    
	using (var dbi = WvDbi.create(moniker))
	{
	    LineEditor le = new LineEditor("VxCli");
	    string inp;
	    
	    while (true)
	    {
		Console.WriteLine();
		inp = le.Edit("vx> ", "");
		if (inp == null) break;
		if (inp == "") continue;
		try
		{
		    using (var result = dbi.select(inp))
		    {
			var colnames =
			    from c in result.columns
			    select c.name.ToUpper();
			Console.Write(wv.fmt("{0}\n\n",
					     colnames.Join(",")));
			
			foreach (var row in result)
			    Console.Write(wv.fmt("{0}\n", row.Join(",")));
		    }
		}
		catch (DbException e)
		{
		    Console.Write(wv.fmt("ERROR: {0}\n", e.Short()));
		}
	    }
	}
	
	return 0;
    }
}