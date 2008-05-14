#include "wvtest.cs.h"
// Test the Schemamatic functions that live in the Versaplex daemon.

using System;
using System.Collections.Generic;
using Wv;
using Wv.Test;
using NDesk.DBus;

[TestFixture]
class SchemamaticTests : VersaplexTester
{
    VxSchemaChecksums VxGetSchemaChecksums()
    {
	Console.WriteLine(" + VxGetSchemaChecksums");

        Message call = CreateMethodCall("GetSchemaChecksums", "");
        //Message call = CreateMethodCall("GetRecordSet", "s");

/*
        MessageWriter mw = new MessageWriter(Connection.NativeEndianness);

        call.Body = mw.ToArray();
        */

        Message reply = call.Connection.SendWithReplyAndBlock(call);
        Console.WriteLine("Got reply");

        switch (reply.Header.MessageType) {
        case MessageType.MethodReturn:
        {
            object replysig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out replysig))
                throw new Exception("D-Bus reply had no signature");

            if (replysig == null || replysig.ToString() != "a(si)")
                throw new Exception("D-Bus reply had invalid signature: " +
                    replysig);

            Console.WriteLine("Got valid signature");

            MessageReader reader = new MessageReader(reply);

            int arraysz;
            reader.GetValue(out arraysz);
            Console.WriteLine("Arraysz is {0}", arraysz);

            // The header is 8-byte aligned
            reader.ReadPad(8);
            int endpos = reader.Position + arraysz;
            Console.WriteLine("Endpos is {0}", endpos);

            string key;
            int checksum;

            VxSchemaChecksums sums = new VxSchemaChecksums();
            while (reader.Position < endpos) {
                // Each structure element is 8-byte aligned
                reader.ReadPad(8);

                reader.GetValue(out key);
                reader.GetValue(out checksum);
                Console.WriteLine("Read checksum {0}={1}", key, checksum);
                sums.Add(key, checksum);
            }

            WVPASSEQ(reader.Position, endpos);
            if (reader.Position != endpos)
                throw new Exception("Position mismatch after reading data");
 
            return sums;
        }
        case MessageType.Error:
        {
            object errname;
            if (!reply.Header.Fields.TryGetValue(FieldCode.ErrorName,
                        out errname))
                throw new Exception("D-Bus error received but no error name "
                        +"given");

            object errsig;
            if (!reply.Header.Fields.TryGetValue(FieldCode.Signature,
                        out errsig) || errsig.ToString() != "s")
                throw new DbusError(errname.ToString());

            MessageReader mr = new MessageReader(reply);

            object errmsg;
            mr.GetValue(typeof(string), out errmsg);

            throw new DbusError(errname.ToString() + ": " + errmsg.ToString());
        }
        default:
            throw new Exception("D-Bus response was not a method return or "
                    +"error");
        }
    }

    [Test, Category("Schemamatic"), Category("GetSchemaChecksums")]
    public void TestProcedureChecksums()
    {
        try { VxExec("drop procedure Func1"); } catch { }

        WVASSERT(VxExec("create procedure Func1 as " +
            "select 'Hello, world, this is Func1!'"));

        VxSchemaChecksums sums = VxGetSchemaChecksums();

        WVASSERT(sums.ContainsKey("Procedure/Func1"));
        WVPASSEQ(sums["Procedure/Func1"].checksum, 0x55F9D9E3);
    }

    public static void Main()
    {
        WvTest.DoMain();
    }
}
