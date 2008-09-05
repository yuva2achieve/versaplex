using System;
using System.Data;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using Wv;
using Wv.Extensions;

namespace Wv
{
    public class WvDbi: IDisposable
    {
	static WvIni settings = new WvIni("wvodbc.ini");
	IDbConnection db;
	WvLog log = new WvLog("WvDbi");
	bool fake_bind = false;
	
        // MSSQL freaks out if there are more than 100 connections open at a
        // time.  Give ourselves a safety margin.
        static int num_active = 0;
        static int max_active = 50;

	public WvDbi(string odbcstr)
	{
            wv.assert(num_active < max_active, "Too many open connections");
            num_active++;
	    string real;
	    bool use_mssql = false;
	    
            string mssql_moniker_name = "mssql:";
	    if (settings[odbcstr].Count > 0)
	    {
		StringDictionary sect = settings[odbcstr];
		
		if (sect["driver"] == "SqlClient")
		{
		    use_mssql = true;
		    fake_bind = true;
		    real = wv.fmt("server={0};database={1};"
				  + "User ID={2};Password={3};",
				  sect["server"],
				  sect["database"],
				  sect["user"], sect["password"]);
		}
		else
		    real = wv.fmt("driver={0};server={1};database={2};"
				  + "uid={3};pwd={4};",
				  sect["driver"], sect["server"],
				  sect["database"],
				  sect["user"], sect["password"]);
		log.print("Generated ODBC string: {0}\n", real);
	    }
            else if (odbcstr.StartsWith(mssql_moniker_name))
            {
                use_mssql = true;
                fake_bind = true;
                real = odbcstr.Substring(mssql_moniker_name.Length);
            }
	    else if (String.Compare(odbcstr, 0, "dsn=", 0, 4, true) == 0)
		real = odbcstr;
	    else if (String.Compare(odbcstr, 0, "driver=", 0, 7, true) == 0)
		real = odbcstr;
	    else
		throw new ArgumentException
		   ("unrecognized odbc string '" + odbcstr + "'");

	    if (use_mssql)
		db = new SqlConnection(real);
	    else
		db = new OdbcConnection(real);
	    db.Open();
	}

        public WvDbi(SqlConnection conn)
        {
            db = conn;
            fake_bind = true;
            if ((db.State & System.Data.ConnectionState.Open) == 0)
                db.Open();
        }
	
	~WvDbi()
	{
	    wv.assert(false, "A WvDbi object was not Dispose()d");
	}

        public IDbConnection Conn
        {
            get { return db; }
        }
	
	IDbCommand prepare(string sql, int nargs)
	{
	    IDbCommand cmd = db.CreateCommand();
	    cmd.CommandText = sql;
	    if (!fake_bind && nargs == 0)
	       cmd.Prepare();
	    return cmd;
	}
	
	// Implement IDisposable.
	public void Dispose() 
	{
            num_active--;
	    db.Dispose();
	    GC.SuppressFinalize(this); 
	}
	
	// FIXME: if fake_bind, this only works the first time for a given
	// IDBCommand object!  Don't try to recycle them.
	void bind(IDbCommand cmd, params object[] args)
	{
	    if (fake_bind)
	    {
		object[] list = new object[args.Length];
		for (int i = 0; i < args.Length; i++)
		{
		    // FIXME!!!  This doesn't escape SQL strings!!
		    if (args[i] == null)
			list[i] = "null";
		    else if (args[i] is int)
			list[i] = (int)args[i];
		    else
			list[i] = wv.fmt("'{0}'", args[i].ToString());
		}
		cmd.CommandText = wv.fmt(cmd.CommandText, list);
		log.print("fake_bind: '{0}'\n", cmd.CommandText);
		return;
	    }
	    
	    bool need_add = (cmd.Parameters.Count < args.Length);
	    
	    // This is the safe one, because we use normal bind() and thus
	    // the database layer does our escaping for us.
	    for (int i = 0; i < args.Length; i++)
	    {
		object a = args[i];
		IDataParameter param;
		if (cmd.Parameters.Count <= i)
		    cmd.Parameters.Add(param = cmd.CreateParameter());
		else
		    param = (IDataParameter)cmd.Parameters[i];
		if (a is DateTime)
		{
		    param.DbType = DbType.DateTime;
		    param.Value = a;
		}
		else
		{
		    param.DbType = DbType.String; // I sure hope so...
		    param.Value = a.ToString();
		}
	    }
	    
	    if (need_add)
		cmd.Prepare();
	}

	// WvSqlRows know their schema.  But, what if you get no rows back,
	// and you REALLY need that schema information?  THEN you call this.
	public DataTable statement_schema(string sql, params object[] args)
	{
	    IDbCommand cmd = prepare(sql, args.Length);
	    if (args.Count() > 0)
		bind(cmd, args);

	    DataTable ret;
	    // Kill that data reader in case it tries to stick around
	    using (IDataReader e = cmd.ExecuteReader())
	    {
		ret = e.GetSchemaTable();
	    }

	    return ret;
	}
	
	public IEnumerable<WvSqlRow> select(string sql,
					       params object[] args)
	{
	    return select(prepare(sql, args.Length), args);
	}
	
	public IEnumerable<WvSqlRow> select(IDbCommand cmd,
						params object[] args)
	{
            if (args.Count() > 0)
                bind(cmd, args);
	    return cmd.ExecuteToWvAutoReader();
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
	
	public int execute(IDbCommand cmd, params object[] args)
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
	    catch (OdbcException)
	    {
		// well, I guess no rows were affected...
		return 0;
	    }
	}
    }
}
