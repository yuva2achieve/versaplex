using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Wv;
using Wv.Extensions;

// An ISchemaBackend that uses a DBus connection to Versaplex as a backing
// store.
[WvMoniker]
internal class VxDbusSchema : ISchemaBackend
{
    Bus bus;

    public static void wvmoniker_register()
    {
	WvMoniker<ISchemaBackend>.register("vx",
		  (string m, object o) => new VxDbusSchema(m));
    }
	
    public VxDbusSchema()
    {
        Connect(Address.Session);
    }

    public VxDbusSchema(string bus_moniker)
    {
	if (bus_moniker.e())
	    bus_moniker = Address.Session;
        if (bus_moniker.e())
            throw new Exception ("DBUS_SESSION_BUS_ADDRESS not set");
        Connect(bus_moniker);
    }

    // If you've already got a Bus you'd like to use.
    public VxDbusSchema(Bus _bus)
    {
        bus = _bus;
    }

    private void Connect(string bus_moniker)
    {
        AddressEntry aent = AddressEntry.Parse(bus_moniker);
        DodgyTransport trans = new DodgyTransport();
        trans.Open(aent);
        bus = new Bus(trans);
    }

    // 
    // The ISchema interface
    //

    // Note: this implementation ignores the sums.
    public VxSchemaErrors Put(VxSchema schema, VxSchemaChecksums sums, 
        VxPutOpts opts)
    {
        Message call = CreateMethodCall("PutSchema", 
            String.Format("{0}i", VxSchema.GetDbusSignature()));

        MessageWriter writer = new MessageWriter(Connection.NativeEndianness);

        schema.WriteSchema(writer);
        writer.Write(typeof(int), (int)opts);
        call.Body = writer.ToArray();

        Message reply = call.Connection.SendWithReplyAndBlock(call);

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        case MessageType.Error:
        {
            object replysig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had no signature.");

            if (replysig == null)
                throw new Exception("D-Bus reply had null signature");

            // Some unexpected error
            if (replysig.ToString() == "s")
                throw VxDbusUtils.GetDbusException(reply);

            if (replysig.ToString() != VxSchemaErrors.GetDbusSignature())
                throw new Exception("D-Bus reply had invalid signature: " +
                    replysig);

            VxSchemaErrors errors = new VxSchemaErrors(reply.iter().pop());
            return errors;
        }
        default:
            throw new Exception("D-Bus response was not a method return or "
                    +"error");
        }
    }

    // Utility API so you can say Get("foo").
    public VxSchema Get(params string[] keys)
    {
        Message call = CreateMethodCall("GetSchema", "as");

        MessageWriter writer = new MessageWriter(Connection.NativeEndianness);

        if (keys == null)
            keys = new string[0];

        writer.Write(typeof(string[]), (Array)keys);
        call.Body = writer.ToArray();

        Message reply = call.Connection.SendWithReplyAndBlock(call);

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        {
            object replysig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had no signature");

            if (replysig == null || replysig.ToString() != "a(sssy)")
                throw new Exception("D-Bus reply had invalid signature: " +
                    replysig);

            MessageReader reader = new MessageReader(reply);
            VxSchema schema = new VxSchema(reader);
            return schema;
        }
        case MessageType.Error:
            throw VxDbusUtils.GetDbusException(reply);
        default:
            throw new Exception("D-Bus response was not a method return or "
                    +"error");
        }
    }

    public VxSchema Get(IEnumerable<string> keys)
    {
        if (keys == null)
            keys = new string[0];
        return Get(keys.ToArray());
    }

    public VxSchemaChecksums GetChecksums()
    {
        Message call = CreateMethodCall("GetSchemaChecksums", "");

        Message reply = call.Connection.SendWithReplyAndBlock(call);

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        {
            object replysig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had no signature");

            if (replysig == null || replysig.ToString() != "a(sat)")
                throw new Exception("D-Bus reply had invalid signature: " +
                    replysig);

            VxSchemaChecksums sums = new VxSchemaChecksums(reply);
            return sums;
        }
        case MessageType.Error:
            throw VxDbusUtils.GetDbusException(reply);
        default:
            throw new Exception("D-Bus response was not a method return or " +
                    "error");
        }
    }

    public VxSchemaErrors DropSchema(IEnumerable<string> keys)
    {
        if (keys == null)
            keys = new string[0];
        return DropSchema(keys.ToArray());
    }

    // A method exported over DBus but not exposed in ISchemaBackend
    public VxSchemaErrors DropSchema(params string[] keys)
    {
        Message call = CreateMethodCall("DropSchema", "as");

        MessageWriter writer = new MessageWriter(Connection.NativeEndianness);

        writer.Write(typeof(string[]), keys);
        call.Body = writer.ToArray();

        Message reply = call.Connection.SendWithReplyAndBlock(call);

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        case MessageType.Error:
        {
            object replysig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had no signature.");

            if (replysig == null)
                throw new Exception("D-Bus reply had null signature");

            if (replysig.ToString() == "s")
                throw VxDbusUtils.GetDbusException(reply);

            if (replysig.ToString() != VxSchemaErrors.GetDbusSignature())
                throw new Exception("D-Bus reply had invalid signature: " +
                    replysig);

            VxSchemaErrors errors = new VxSchemaErrors(reply.iter().pop());
            return errors;
        }
        default:
            throw new Exception("D-Bus response was not a method return or "
                    + "error");
        }
    }
    
    public string GetSchemaData(string tablename, int seqnum, string where)
    {
        Message call = CreateMethodCall("GetSchemaData", "ss");

        MessageWriter writer = new MessageWriter(Connection.NativeEndianness);

        if (where == null)
            where = "";

        writer.Write(typeof(string), tablename);
        writer.Write(typeof(string), where);
        call.Body = writer.ToArray();

        Message reply = call.Connection.SendWithReplyAndBlock(call);

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        {
            object replysig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had no signature");

            if (replysig == null || replysig.ToString() != "s")
                throw new Exception("D-Bus reply had invalid signature: " +
                    replysig);

            return reply.iter().pop();
        }
        case MessageType.Error:
            throw VxDbusUtils.GetDbusException(reply);
        default:
            throw new Exception("D-Bus response was not a method return or "
                    +"error");
        }
    }

    public void PutSchemaData(string tablename, string text, int seqnum)
    {
        Message call = CreateMethodCall("PutSchemaData", "ss");

        MessageWriter writer = new MessageWriter(Connection.NativeEndianness);

        writer.Write(tablename);
        writer.Write(text);
        call.Body = writer.ToArray();

        Message reply = call.Connection.SendWithReplyAndBlock(call);

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        {
            object replysig;
            if (reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had unexpected signature" + 
                    replysig);

            return;
        }
        case MessageType.Error:
            throw VxDbusUtils.GetDbusException(reply);
        default:
            throw new Exception("D-Bus response was not a method return or "
                    +"error");
        }
    }

    //
    // Non-ISchemaBackend methods
    //

    // Use our Bus object to create a method call.
    public Message CreateMethodCall(string member, string signature)
    {
        return VxDbusUtils.CreateMethodCall(bus, member, signature);
    }
}


