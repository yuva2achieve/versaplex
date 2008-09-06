using System;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.SqlTypes;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Wv;
using Wv.Extensions;

namespace Wv
{
    public class WvDbi: IDisposable
    {
	static WvIni settings = new WvIni("wvodbc.ini");
	    
	protected static WvLog log = new WvLog("WvDbi", WvLog.L.Debug1);
	IDbConnection _db;
	protected IDbConnection db 
	    { get { return _db; } }
	public IDbConnection fixme_db 
	    { get { return _db; } }
	
        // MSSQL freaks out if there are more than 100 connections open at a
        // time.  Give ourselves a safety margin.
        static int num_active = 0;
        static int max_active = 50;
	
	public static WvDbi create(string moniker)
	{
	    log.print("Creating '{0}'\n", moniker);

	    if (!moniker.Contains(":") && settings[moniker].Count > 0)
	    {
		var sect = settings[moniker];
		
		if (sect["driver"] == "SqlClient")
		    return create(wv.fmt("mssql:"
					 + "server={0};database={1};"
					 + "User ID={2};Password={3};",
					 sect["server"],
					 sect["database"],
					 sect["user"], sect["password"]));
		else
		    return create(wv.fmt("ado:"
					 + "driver={0};server={1};database={2};"
					 + "uid={3};pwd={4};",
					 sect["driver"], sect["server"],
					 sect["database"],
					 sect["user"], sect["password"]));
	    }
	    
	    if (moniker.StartsWith("dsn=") || moniker.StartsWith("driver="))
		return create("ado:" + moniker);
	    
	    WvDbi dbi = WvMoniker<WvDbi>.create(moniker);
	    if (dbi == null)
		throw new Exception
		           (wv.fmt("No moniker found for '{0}'", moniker));
	    return dbi;
	}
	
	protected WvDbi()
	{
            wv.assert(num_active < max_active, "Too many open connections");
            num_active++;
	}

	~WvDbi()
	{
	    wv.assert(false, "A WvDbi object was not Dispose()d");
	}
	
	protected void opendb(IDbConnection db)
	{
	    this._db = db;
            if ((db.State & System.Data.ConnectionState.Open) == 0)
                db.Open();
	}

        public IDbConnection Conn
        {
            get { return db; }
        }
	
	// Implement IDisposable.
	public void Dispose() 
	{
            num_active--;
	    db.Dispose();
	    GC.SuppressFinalize(this); 
	}
	
	protected virtual IDbCommand prepare(string sql, int nargs)
	{
	    IDbCommand cmd = db.CreateCommand();
	    cmd.CommandText = sql;
	    if (nargs == 0)
	       cmd.Prepare();
	    return cmd;
	}
	
	protected virtual void bind(IDbCommand cmd, params object[] args)
	{
	    bool need_add = (cmd.Parameters.Count < args.Length);
	    
	    for (int i = 0; i < args.Length; i++)
	    {
		object a = args[i];
		IDbDataParameter param;
		if (cmd.Parameters.Count <= i)
		    cmd.Parameters.Add(param = cmd.CreateParameter());
		else
		    param = (IDbDataParameter)cmd.Parameters[i];
		if (a is DateTime)
		{
		    param.DbType = DbType.DateTime;
		    param.Value = a;
		}
		else if (a is int)
		{
		    param.DbType = DbType.Int32;
		    param.Value = a;
		}
		else
		{
		    string s = a.ToString();
		    param.DbType = DbType.String; // I sure hope so...
		    param.Value = s;
		    param.Size = s.Length;
		}
	    }
	    
	    if (need_add)
		cmd.Prepare();
	}

	public WvSqlRows select(string sql, params object[] args)
	{
	    return select(prepare(sql, args.Length), args);
	}
	
	protected WvSqlRows select(IDbCommand cmd, params object[] args)
	{
            if (args.Count() > 0)
                bind(cmd, args);
	    return new WvSqlRows_IDataReader(cmd.ExecuteReader());
	}
	
	public WvSqlRow select_onerow(string sql, params object[] args)
	{
	    // only iterates a single row, if it exists
	    foreach (WvSqlRow r in select(sql, args))
		return r; // only return the first one
	    return null;
	}
	
	public WvAutoCast select_one(string sql, params object[] args)
	{
	    var a = select_onerow(sql, args);
	    if (a != null && a.Length > 0)
		return a[0];
	    else
		return WvAutoCast._null;
	}
	
	public int execute(string sql, params object[] args)
	{
	    return execute(prepare(sql, args.Length), args);
	}
	
	public int exec(string sql, params object[] args)
	{
	    return execute(prepare(sql, args.Length), args);
	}
	
	protected int execute(IDbCommand cmd, params object[] args)
	{
            if (args.Count() > 0)
                bind(cmd, args);
	    return cmd.ExecuteNonQuery();
	}
	
	public int try_execute(string sql, params object[] args)
	{
	    try
	    {
		return execute(sql, args);
	    }
	    catch (DbException)
	    {
		// well, I guess no rows were affected...
		return 0;
	    }
	}

	public int try_exec(string sql, params object[] args)
	{
	    return try_execute(sql, args);
	}
    }
    
    [WvMoniker]
    public class WvDbi_ODBC : WvDbi
    {
	public static void wvmoniker_register()
	{
	    WvMoniker<WvDbi>.register("ado",
		 (string m, object o) => new WvDbi_ODBC(m));
	    WvMoniker<WvDbi>.register("odbc",
		 (string m, object o) => new WvDbi_ODBC(m));
	    WvMoniker<WvDbi>.register("mysql",
		 (string m, object o) => new WvDbi_ODBC("MySQL:" + m));
	}
	
	public WvDbi_ODBC(string moniker)
	{
	    string real;
	    if (moniker.StartsWith("dsn=") || moniker.StartsWith("driver="))
		real = moniker;
	    else
	    {
		// try to parse it as an URL
		WvUrl url = new WvUrl(moniker);
		if (url.path.StartsWith("/"))
		    url.path = url.path.Substring(1);
		if (url.method == "file") // method not provided
		    real = wv.fmt("dsn={0};database={1};"
				  + "User ID={2};uid={2};Password={3};pwd={3}",
				  url.host, url.path, url.user, url.password);
		else
		    real = wv.fmt("driver={0};server={1};database={2};"
				  + "User ID={3};uid={3};Password={4};pwd={4}",
				  url.method, url.host, url.path,
				  url.user, url.password);
	    }
	    
	    log.print("ODBC create: '{0}'\n", real);
	    opendb(new OdbcConnection(real));
	}
	
    }
    
    [WvMoniker]
    public class WvDbi_MSSQL : WvDbi
    {
	public static void wvmoniker_register()
	{
	    WvMoniker<WvDbi>.register("mssql",
		 (string m, object o) => new WvDbi_MSSQL(m));
	}
	
        public WvDbi_MSSQL(SqlConnection conn)
        {
	    opendb(conn);
        }
	
	public WvDbi_MSSQL(string moniker)
	{
	    string real;
	    if (!moniker.StartsWith("//"))
		real = moniker;
	    else
	    {
		// try to parse it as an URL
		WvUrl url = new WvUrl(moniker);
		if (url.path.StartsWith("/"))
		    url.path = url.path.Substring(1);
		real = wv.fmt("server={0};database={1};"
			      + "User ID={2};Password={3};",
			      url.host, url.path, url.user, url.password);
	    }
	    
	    log.print("MSSQL create: '{0}'\n", real);
	    opendb(new SqlConnection(real));
	}
	
	protected override IDbCommand prepare(string sql, int nargs)
	{
	    IDbCommand cmd = db.CreateCommand();
	    cmd.CommandText = sql;
	    return cmd;
	}

	protected override void bind(IDbCommand _cmd, params object[] args)
	{
	    SqlCommand cmd = (SqlCommand)_cmd;
	    for (int i = 0; i < args.Length; i++)
	    {
		object a = args[i];
		if (cmd.Parameters.Count <= i)
		{
		    string name = wv.fmt("@col{0}", i);
		    cmd.Parameters.Add(new SqlParameter(name, a));
		}
		cmd.Parameters[i].Value = a;
	    }
	}
    }
}

