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
    Connection bus;

    public static void wvmoniker_register()
    {
	WvMoniker<ISchemaBackend>.register("vx",
		  (string m, object o) => new VxDbusSchema(m));
    }
	
    public VxDbusSchema()
	: this((string)null)
    {
    }

    public VxDbusSchema(string bus_moniker)
    {
	if (bus_moniker.e())
	    bus = Connection.session_bus;
	else
	    bus = new Connection(bus_moniker);
    }

    // If you've already got a Bus you'd like to use.
    public VxDbusSchema(Connection _bus)
    {
        bus = _bus;
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

        MessageWriter writer = new MessageWriter();

        schema.WriteSchema(writer);
        writer.Write((int)opts);
        call.Body = writer.ToArray();

        Message reply = bus.send_and_wait(call);

        switch (reply.type)
	{
        case MessageType.MethodReturn:
        case MessageType.Error:
        {
	    if (reply.signature.e())
                throw new Exception("D-Bus reply had no signature.");

            // Some unexpected error
            if (reply.signature == "s")
                throw VxDbusUtils.GetDbusException(reply);

            if (reply.signature != VxSchemaErrors.GetDbusSignature())
                throw new Exception("D-Bus reply had invalid signature: " +
                    reply.signature);

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

        MessageWriter writer = new MessageWriter();

        if (keys == null)
            keys = new string[0];

        writer.WriteArray(4, keys, (w2, k) => {
	    w2.Write(k);
	});
        call.Body = writer.ToArray();

        Message reply = bus.send_and_wait(call);

        switch (reply.type) {
        case MessageType.MethodReturn:
        {
            if (reply.signature.e())
                throw new Exception("D-Bus reply had no signature");

            if (reply.signature != "a(sssy)")
                throw new Exception("D-Bus reply had invalid signature: " +
				    reply.signature);

            VxSchema schema = new VxSchema(reply.iter().pop());
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

        Message reply = bus.send_and_wait(call);

        switch (reply.type) {
        case MessageType.MethodReturn:
        {
            if (reply.signature.e())
                throw new Exception("D-Bus reply had no signature");

            if (reply.signature != "a(sat)")
                throw new Exception("D-Bus reply had invalid signature: " +
				    reply.signature);

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

        MessageWriter writer = new MessageWriter();

	writer.WriteArray(4, keys, (w2, k) => {
	    w2.Write(k);
	});
        call.Body = writer.ToArray();

        Message reply = bus.send_and_wait(call);

        switch (reply.type) {
        case MessageType.MethodReturn:
        case MessageType.Error:
        {
            if (reply.signature.e())
                throw new Exception("D-Bus reply had no signature.");

            if (reply.signature == "s")
                throw VxDbusUtils.GetDbusException(reply);

            if (reply.signature != VxSchemaErrors.GetDbusSignature())
                throw new Exception("D-Bus reply had invalid signature: " +
                    reply.signature);

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

        MessageWriter writer = new MessageWriter();

        if (where == null)
            where = "";

        writer.Write(tablename);
        writer.Write(where);
        call.Body = writer.ToArray();

        Message reply = bus.send_and_wait(call);

        switch (reply.type) {
        case MessageType.MethodReturn:
        {
            if (reply.signature.e())
                throw new Exception("D-Bus reply had no signature");

            if (reply.signature != "s")
                throw new Exception("D-Bus reply had invalid signature: " +
                    reply.signature);

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

        MessageWriter writer = new MessageWriter();

        writer.Write(tablename);
        writer.Write(text);
        call.Body = writer.ToArray();

        Message reply = bus.send_and_wait(call);

        switch (reply.type) {
        case MessageType.MethodReturn:
        {
            if (reply.signature.ne())
                throw new Exception("D-Bus reply had unexpected signature" + 
                    reply.signature);

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


