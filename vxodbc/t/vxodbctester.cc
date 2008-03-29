#include "vxodbctester.h"
#include "table.h"

#include "wvistreamlist.h"
#include "wvtest.h"
#include "common.h"

#include <vector>
#include "odbcinst.h"

#include "../wvlogger.h"

int VxOdbcTester::num_names_registered = 0;

bool VxOdbcTester::name_request_cb(WvDBusMsg &msg)
{
    WvLog log("name_request_cb", WvLog::Debug1);
    num_names_registered++;
    // FIXME: Sensible logging
    // FIXME: Do something useful if the name was already registered
    log("*** A name was registered: %s\n", (WvString)msg);
    return true;
}
    
VxOdbcTester::VxOdbcTester(bool always_create_server) :
    dbus_server(),
    vxserver_conn(dbus_server.moniker),
    t(NULL),
    log("Fake Versaplex", WvLog::Debug1)
{
    dbus_moniker = dbus_server.moniker;

    WvString use_real(getenv("USE_REAL_VERSAPLEX"));
    if (always_create_server || !use_real || use_real == "0") 
    {
        WvIStreamList::globallist.append(&vxserver_conn, false);

        log("*** Registering vx.versaplexd\n");
        vxserver_conn.request_name("vx.versaplexd", &name_request_cb);
        while (num_names_registered < 1)
            WvIStreamList::globallist.runonce();

        WvDBusCallback cb(wv::bind(
            &VxOdbcTester::msg_received, this, _1));
        vxserver_conn.add_callback(WvDBusConn::PriNormal, cb, this);
    }
    else
        dbus_moniker = "dbus:session";

    set_odbcini_info("localhost", "", "pmccurdy", "pmccurdy", "scs", 
        dbus_moniker);

    Connect();
}

VxOdbcTester::~VxOdbcTester()
{
    Disconnect();

#ifndef WIN32
    // Dirty hack: Close any WvLog files VxODBC opened.  This keeps the WvTest
    // open file detector from freaking out, since the log files are opened
    // lazily after the open file detector does its initial check.  
    wvlog_close();
#endif
}

bool VxOdbcTester::msg_received(WvDBusMsg &msg)
{
    if (msg.get_dest() != "vx.versaplexd")
        return false;

    if (msg.get_path() != "/db") 
        return false;

    if (msg.get_interface() != "vx.db") 
        return false;

    // The message was for us

    log("*** Received message %s\n", (WvString)msg);
    log("*** Got argstr '%s'\n", msg.get_argstr());

    log("sender:%s\ndest:%s\npath:%s\niface:%s\nmember:%s\n",
        msg.get_sender(), msg.get_dest(), msg.get_path(), 
        msg.get_interface(), msg.get_member());

    if (msg.get_member() == "ExecRecordset")
    {
        log("Processing ExecRecordSet\n");
        WvString query(msg.get_argstr());
        if (query == expected_query)
        {
            log("*** Sending reply\n");
            WvDBusMsg reply = msg.reply();
            std::vector<Column>::iterator it;

            reply.array_start(WvString("(%s)", ColumnInfo::getDBusSignature()));
            for (it = t->cols.begin(); it != t->cols.end(); ++it)
                it->info.writeHeader(reply);
            reply.array_end();

            // Write the body signature
            if (t->cols.size() > 0)
            {
                WvString sig(t->getDBusTypeSignature());
                log("Body signature is %s\n", sig);
                reply.varray_start(WvString("(%s)", sig));
                if (t->cols[0].data.size() > 0) {
                    reply.struct_start(sig);
                    // Write the body
                    for (it = t->cols.begin(); it != t->cols.end(); ++it)
                    {
                        it->addDataTo(reply);
                    }
                    reply.struct_end();
                } 
                reply.varray_end();
            }

            // Nullity
            // FIXME: Need to send one copy per row, and properly reflect 
            // the data (not the column's overall nullability)
            reply.array_start("ay");
            if (t->cols.size() > 0 && t->cols[0].data.size() > 0)
            {
                reply.array_start("y");
                for (it = t->cols.begin(); it != t->cols.end(); ++it)
                    reply.append(it->info.nullable);
                reply.array_end();
            }
            reply.array_end();

            reply.send(vxserver_conn);
        }
        else
        {
            WvDBusError(msg, "System.NotImplemented", 
                "Not yet implemented.  Try again later.").send(vxserver_conn);
        }
    }
    else
    {
        WvDBusError(msg, "System.NotImplemented", 
            "Not yet implemented.  Try again later.").send(vxserver_conn);
    }
    return true;
}
