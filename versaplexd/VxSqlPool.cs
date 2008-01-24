using System.Data.SqlClient;
using System.Collections;
using System.Collections.Generic;
using System;
// For the exception types
using versabanq.Versaplex.Dbus.Db;
using Wv.Utils;

namespace versabanq.Versaplex.Server {

public static class VxSqlPool
{
    private static Ini inifile = new Ini("versaplexd.ini");

    private static SqlConnectionStringBuilder GetConnInfoFromConnId(
        string connid)
    {
        SqlConnectionStringBuilder conStr = new SqlConnectionStringBuilder();

        // Mono doesn't support this
        //conStr.Enlist = false;

        // At the moment, a connection ID is just a username
        string dbname = inifile["User Map"][connid];
        if (dbname == null)
            throw new VxConfigException(String.Format("No user '{0}' found.",
                connid));

        string cfgval = inifile["Connections"][dbname];
        if (cfgval == null)
            throw new VxConfigException(String.Format(
                "No connection found for user {0}", connid));

        string moniker_name = "mssql:";
        if (cfgval.IndexOf(moniker_name) == 0)
            conStr.ConnectionString = cfgval.Substring(moniker_name.Length);
        else
            throw new VxConfigException(String.Format(
                "Malformed connection string '{0}'.", moniker_name));

        System.Console.Write("Connection string: {0}", conStr.ConnectionString);

        return conStr;
    }

    public static SqlConnection TakeConnection(string connid)
    {
        System.Console.WriteLine("TakeConnection {0}, starting", connid);
        
        SqlConnectionStringBuilder conStr = GetConnInfoFromConnId(connid);
        SqlConnection con = new SqlConnection(conStr.ConnectionString);

        // FIXME: Exceptions
        con.Open();

        return con;
    }

    public static void ReleaseConnection(SqlConnection c)
    {
        c.Close();
    }
}

}
