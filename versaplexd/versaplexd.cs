using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NDesk.DBus;
using org.freedesktop.DBus;
using Wv;
using Wv.NDesk.Options;

public static class VersaMain
{
    static WvLog log = new WvLog("Versaplex");
    static Connection.MessageHandler oldhandler = null;
    static VxMethodCallRouter msgrouter = new VxMethodCallRouter();
    static WvDBusServer dbusserver;
    static Thread dbusserver_thread = null;
    static ManualResetEvent thread_ready = new ManualResetEvent(false);
    public static bool want_to_die = false;
    
    public static Bus conn;

    private static void DataReady(object sender, object cookie)
    {
        // FIXME: This may require special handling for padding between
        // messages: it hasn't been a problem so far, but should be addressed

        VxBufferStream vxbs = (VxBufferStream)sender;

        Connection conn = (Connection)cookie;

        if (vxbs.BufferPending == 0) {
            log.print("??? DataReady but nothing to read\n");
            return;
        }

        // XXX: Ew.
        byte[] buf = new byte[vxbs.BufferPending];
        vxbs.Read(buf, 0, buf.Length);
        vxbs.BufferAmount = conn.ReceiveBuffer(buf, 0, buf.Length);
    }

    private static void NoMoreData(object sender, object cookie)
    {
        log.print(
                "***********************************************************\n"+
                "************ D-bus connection closed by server ************\n"+
                "***********************************************************\n");

        VxBufferStream vxbs = (VxBufferStream)sender;
        vxbs.Close();

        VxEventLoop.Shutdown();
    }

    private static void MessageReady(Message msg)
    {
        // FIXME: This should really queue things to be run from the thread
        // pool and then the response would be sent back through the action
        // queue
        log.print(WvLog.L.Debug4, "MessageReady\n");

        VxDbus.MessageDump("<<  ", msg);

        switch (msg.Header.MessageType) {
            case MessageType.MethodCall:
            {
                Message reply;
                if (msgrouter.RouteMessage(msg, out reply)) {
                    if (reply == null) {
                        // FIXME: Do something if this happens, maybe?
                        log.print("Empty reply from RouteMessage\n");
                    } else {
                        // XXX: Should this be done further down rather than
                        // passing the reply out here?
                        msg.Connection.Send(reply);
                    }
                    return;
                }
            
                break;
            }
        }

        // FIXME: This is hacky. But it covers stuff I don't want to deal with
        // yet.
        oldhandler(msg);
    }
    
    static void _StartDBusServerThread(string[] monikers)
    {
	using (dbusserver = new WvDBusServer())
	{
	    foreach (string m in monikers)
		dbusserver.listen(m);
	    thread_ready.Set();
	    while (!want_to_die)
		dbusserver.runonce();
	}
    }
    
    static void StartDBusServerThread(string[] monikers)
    {
	if (monikers.Length == 0) return;
	thread_ready.Reset();
	dbusserver_thread = new Thread(() => _StartDBusServerThread(monikers));
	dbusserver_thread.Start();
	thread_ready.WaitOne();
    }
    
    static void StopDBusServerThread()
    {
	want_to_die = true;
	if (dbusserver_thread != null)
	    dbusserver_thread.Join();
    }
    
    static void ShowHelp()
    {
	Console.Error.WriteLine
	    ("Usage: versaplexd [-v] [-b dbus-moniker]\n" +
	     "                  [-l listen-moniker]\n" +
	     "                  [-c config-file]");
	Environment.Exit(1);
    }
    
    public static int Main(string[] args)
    {
	WvLog.L verbose = WvLog.L.Info;
	string bus = null;
	string cfgfile = "versaplexd.ini";
	var listeners = new List<string>();
	new OptionSet()
	    .Add("v|verbose", delegate(string v) { ++verbose; })
	    .Add("b=|bus=", delegate(string v) { bus = v; })
		.Add("c=|config=", delegate(string v) { cfgfile = v; })
	    .Add("l=|listen=", delegate(string v) { listeners.Add(v); })
	    .Add("?|h|help", delegate(string v) { ShowHelp(); })
	    .Parse(args);
	
	WvLog.maxlevel = (WvLog.L)verbose;
	
	StartDBusServerThread(listeners.ToArray());

	msgrouter.AddInterface(VxDbInterfaceRouter.Instance);

	bool cfgfound = false;

	if (File.Exists(cfgfile))
		cfgfound = true;
        else if (File.Exists("/etc/versaplexd.ini"))
	{
	    log.print("Using /etc/versaplexd.ini for configuration.\n");
	    cfgfound = true;
	    cfgfile = "/etc/versaplexd.ini";
	}

	if (cfgfound == true) {
		VxSqlPool.SetIniFile(cfgfile);
	} else {
		throw new Exception(wv.fmt(
			"Could not find config file '{0}',\n" +
			"and /etc/versaplexd.ini does not exist",
					   cfgfile));
	}
	
	if (bus == null)
	    bus = Address.Session;

	if (bus == null)
	{
	    log.print
		("DBUS_SESSION_BUS_ADDRESS not set and no -b option given.\n");
	    ShowHelp();
	}
	
        log.print("Connecting to '{0}'\n", bus);
        AddressEntry aent = AddressEntry.Parse(bus);

        DodgyTransport trans = new DodgyTransport();
        trans.Open(aent);

        conn = new Bus(trans);

        string myNameReq = "vx.versaplexd";
        RequestNameReply rnr = conn.RequestName(myNameReq,
                NameFlag.DoNotQueue);

        switch (rnr) {
            case RequestNameReply.PrimaryOwner:
                log.print("Name registered, ready\n");
                break;
            default:
                log.print("Register name result: \n" + rnr.ToString());
                return 2;
        }

        VxBufferStream vxbs = new VxBufferStream(trans.Socket);
        conn.Transport.Stream = vxbs;
        conn.ns = conn.Transport.Stream;
        vxbs.Cookie = conn;
        vxbs.DataReady += DataReady;
        vxbs.NoMoreData += NoMoreData;
        vxbs.BufferAmount = 16;

        oldhandler = conn.OnMessage;
        conn.OnMessage = MessageReady;

        VxEventLoop.Run();

	StopDBusServerThread();
        log.print("Done!\n");
	return 0;
    }
}
