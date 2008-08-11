using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Wv;
using Wv.Extensions;

// An ISchemaBackend that uses a direct database connection as a backing
// store.
internal class VxDbSchema : ISchemaBackend
{
    static WvLog log = new WvLog("VxDbSchema", WvLog.L.Debug2);

    internal static string[] ProcedureTypes = new string[] { 
//            "CheckCnst", 
//            "Constraint",
//            "Default",
//            "DefaultCnst",
//            "Executed",
            "ScalarFunction",
            "TableFunction",
//            "InlineFunction",
//            "ExtendedProc",
//            "ForeignKey",
//            "MSShipped",
//            "PrimaryKey",
            "Procedure",
            "ReplProc",
//            "Rule",
//            "SystemTable",
//            "Table",
            "Trigger",
//            "UniqueCnst",
            "View",
//            "OwnerId"
        };

    string connstr;

    public VxDbSchema(string _connstr)
    {
        connstr = _connstr;
    }

    //
    // The ISchema interface
    //

    public VxSchemaErrors Put(VxSchema schema, VxSchemaChecksums sums, 
        VxPutOpts opts)
    {
        bool no_retry = (opts & VxPutOpts.NoRetry) != 0;
        int old_err_count = -1;
        IEnumerable<string> keys = schema.Keys;
        VxSchemaErrors errs = new VxSchemaErrors();

        // Sometimes we'll get schema elements in the wrong order, so retry
        // until the number of errors stops decreasing.
        while (errs.Count != old_err_count)
        {
            log.print("Calling Put on {0} entries\n", 
                old_err_count == -1 ? schema.Count : errs.Count);
            old_err_count = errs.Count;
            errs.Clear();
            foreach (string key in keys)
            {
                try {
                    log.print("Calling PutSchema on {0}\n", key);
                    PutSchemaElement(schema[key], opts);
                } catch (VxSqlException e) {
                    log.print("Got error from {0}\n", key);
                    errs.Add(key, new VxSchemaError(key, e.Message, 
                        e.GetFirstSqlErrno()));
                }
            }
            // If we only had one schema element, retrying it isn't going to
            // fix anything.  We retry to fix ordering problems.
            if (no_retry || errs.Count == 0 || schema.Count == 1)
                break;

            log.print("Got {0} errors, old_errs={1}, retrying\n", 
                errs.Count, old_err_count);

            keys = errs.Keys.ToList();
        }
        return errs.Count > 0 ? errs : null;
    }

    // Escape the schema element names supplied, to make sure they don't have
    // evil characters.
    private static string EscapeSchemaElementName(string name)
    {
        // Replace any nasty non-ASCII characters with an !
        string escaped = Regex.Replace(name, "[^\\p{IsBasicLatin}]", "!");

        // Escape quote marks
        return escaped.Replace("'", "''");
    }

    public VxSchema Get(IEnumerable<string> keys)
    {
        List<string> all_names = new List<string>();
        List<string> proc_names = new List<string>();
        List<string> xml_names = new List<string>();
        List<string> tab_names = new List<string>();
        List<string> idx_names = new List<string>();

        foreach (string key in keys)
        {
            string fullname = EscapeSchemaElementName(key);
            Console.WriteLine("CallGetSchema: Read name " + fullname);
            all_names.Add(fullname);

            string[] parts = fullname.Split(new char[] {'/'}, 2);
            if (parts.Length == 2)
            {
                string type = parts[0];
                string name = parts[1];
                if (type == "Table")
                    tab_names.Add(name);
                else if (type == "Index")
                    idx_names.Add(name);
                else if (type == "XMLSchema")
                    xml_names.Add(name);
                else
                    proc_names.Add(name);
            }
            else
            {
                // No type given, just try them all
                proc_names.Add(fullname);
                xml_names.Add(fullname);
                tab_names.Add(fullname);
                idx_names.Add(fullname);
            }
        }

        VxSchema schema = new VxSchema();

        if (proc_names.Count > 0 || all_names.Count == 0)
        {
            foreach (string type in ProcedureTypes)
            {
                RetrieveProcSchemas(schema, proc_names, type, 0);
                RetrieveProcSchemas(schema, proc_names, type, 1);
            }
        }

        if (idx_names.Count > 0 || all_names.Count == 0)
            RetrieveIndexSchemas(schema, idx_names);

        if (xml_names.Count > 0 || all_names.Count == 0)
            RetrieveXmlSchemas(schema, xml_names);

        if (tab_names.Count > 0 || all_names.Count == 0)
            RetrieveTableColumns(schema, tab_names);

        return schema;
    }

    public VxSchemaChecksums GetChecksums()
    {
        VxSchemaChecksums sums = new VxSchemaChecksums();

        foreach (string type in ProcedureTypes)
        {
            if (type == "Procedure")
            {
                // Set up self test
                SqlExecScalar("create procedure schemamatic_checksum_test " + 
                    "as print 'hello' ");
            }

            GetProcChecksums(sums, type, 0);

            if (type == "Procedure")
            {
                SqlExecScalar("drop procedure schemamatic_checksum_test");

                // Self-test the checksum feature.  If mssql's checksum
                // algorithm changes, we don't want to pretend our checksum
                // list makes any sense!
                string test_csum_label = "Procedure/schemamatic_checksum_test";
                ulong got_csum = 0;
                if (sums.ContainsKey(test_csum_label))
                    got_csum = sums[test_csum_label].checksums[0];
                ulong want_csum = 0x173d6ee8;
                if (want_csum != got_csum)
                {
                    throw new Exception(String.Format("checksum_test_mismatch!"
                        + " {0} != {1}", got_csum, want_csum));
                }
                sums.Remove(test_csum_label);
            }

            GetProcChecksums(sums, type, 1);
        }

        // Do tables separately
        GetTableChecksums(sums);

        // Do indexes separately
        GetIndexChecksums(sums);

        // Do XML schema collections separately (FIXME: only if SQL2005)
        GetXmlSchemaChecksums(sums);

        return sums;
    }

    // Deletes the named object in the database.
    public void DropSchema(string type, string name)
    {
        if (type == null || name == null)
            return;

        string query = GetDropCommand(type, name);

        SqlExecScalar(query);
    }

    // 
    // Non-ISchemaBackend methods
    //

    // Note: You must call Close() or Finalize() on the returned object.  The
    // easiest and most reliable way is to put it in a using() block, e.g. 
    // using (SqlConnection conn = GetConnection()) { ... }
    private SqlConnection GetConnection()
    {
        SqlConnection con = new SqlConnection(connstr);
        con.Open();
        return con;
    }

    // FIXME: An alarming duplicate of VxDb.ExecScalar.
    private object SqlExecScalar(string query)
    {
        try
        {
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = query;
                return cmd.ExecuteScalar();
            }
        }
        catch (SqlException e)
        {
            throw new VxSqlException(e.Message, e);
        }
    }

    // Like VxDb.ExecRecordset, except we don't care about colinfo or nullity
    private object[][] SqlExecRecordset(string query)
    {
        List<object[]> result = new List<object[]>();
        try
        {
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    object[] row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    for (int i = 0; i < reader.FieldCount; i++)
                        if (reader.IsDBNull(i))
                            row[i] = null;
                    result.Add(row);
                }
            }
        }
        catch (SqlException e)
        {
            throw new VxSqlException(e.Message, e);
        }

        return result.ToArray();
    }

    private static string GetDropCommand(string type, string name)
    {
        if (type.StartsWith("Function"))
            type = "Function";
        else if (type == "XMLSchema")
            type = "XML Schema Collection";
        else if (type == "Index")
        {
            string[] parts = name.Split(new char[] {'/'}, 2);
            if (parts.Length == 2)
            {
                string tabname = parts[0];
                string idxname = parts[1];
                return String.Format(
                    @"declare @x int;
                      select @x = is_primary_key 
                          from sys.indexes 
                          where object_name(object_id) = '{0}' 
                            and name = '{1}';
                      if @x = 1 
                          ALTER TABLE [{0}] DROP CONSTRAINT [{1}]; 
                      else 
                          DROP {2} [{0}].[{1}]", 
                    tabname, idxname, type);
            } 
            else 
                throw new ArgumentException(String.Format(
                    "Invalid index name '{0}'!", name));
        }

        return String.Format("DROP {0} [{1}]", type, name);
    }

    // Replaces the named object in the database.  elem.text is a verbatim
    // hunk of text returned earlier by GetSchema.  'destructive' says whether
    // or not to perform potentially destructive operations while making the
    // change, e.g. dropping a table so we can re-add it with the right
    // columns.
    private void PutSchemaElement(VxSchemaElement elem, VxPutOpts opts)
    {
        bool destructive = (opts & VxPutOpts.Destructive) != 0;
        if (destructive || !elem.type.StartsWith("Table"))
        {
            try { 
                DropSchema(elem.type, elem.name); 
            } catch (VxSqlException e) {
                // Check if it's a "didn't exist" error, rethrow if not.
                // SQL Error 3701 means "can't drop sensible item because
                // it doesn't exist or you don't have permission."
                // SQL Error 15151 means "can't drop XML Schema collection 
                // because it doesn't exist or you don't have permission."
                if (!e.ContainsSqlError(3701) && !e.ContainsSqlError(15151))
                    throw e;
            }
        }

        if (elem.text != null && elem.text != "")
            SqlExecScalar(elem.text);
    }

    // Functions used for GetSchemaChecksums

    internal void GetProcChecksums(VxSchemaChecksums sums, 
            string type, int encrypted)
    {
        string encrypt_str = encrypted > 0 ? "-Encrypted" : "";

        log.print("Indexing: {0}{1}\n", type, encrypt_str);

        string query = @"
            select convert(varchar(128), object_name(id)) name,
                     convert(int, colid) colid,
                     convert(varchar(3900), text) text
                into #checksum_calc
                from syscomments
                where objectproperty(id, 'Is" + type + @"') = 1
                    and encrypted = " + encrypted + @"
                    and object_name(id) like '%'
            select name, convert(varbinary(8), getchecksum(text))
                from #checksum_calc
                order by name, colid
            drop table #checksum_calc";

        object[][] data = SqlExecRecordset(query);

        foreach (object[] row in data)
        {
            string name = (string)row[0];
            ulong checksum = 0;
            foreach (byte b in (byte[])row[1])
            {
                checksum <<= 8;
                checksum |= b;
            }

            // Ignore dt_* functions and sys* views
            if (name.StartsWith("dt_") || name.StartsWith("sys"))
                continue;

            // Fix characters not allowed in filenames
            name.Replace('/', '!');
            name.Replace('\n', '!');
            string key = String.Format("{0}{1}/{2}", type, encrypt_str, name);

            log.print("name={0}, checksum={1}, key={2}\n", name, checksum, key);
            sums.Add(key, checksum);
        }
    }

    internal void GetTableChecksums(VxSchemaChecksums sums)
    {
        log.print("Indexing: Tables\n");

        // The weird "replace" in defval is because different versions of
        // mssql (SQL7 vs. SQL2005, at least) add different numbers of parens
        // around the default values.  Weird, but it messes up the checksums,
        // so we just remove all the parens altogether.
        string query = @"
            select convert(varchar(128), t.name) tabname,
               convert(varchar(128), c.name) colname,
               convert(varchar(64), typ.name) typename,
               convert(int, c.length) len,
               convert(int, c.xprec) xprec,
               convert(int, c.xscale) xscale,
               convert(varchar(128),
                   replace(replace(def.text, '(', ''), ')', ''))
                   defval,
               convert(int, c.isnullable) nullable,
               convert(int, columnproperty(t.id, c.name, 'IsIdentity')) isident,
               convert(int, ident_seed(t.name)) ident_seed,
               convert(int, ident_incr(t.name)) ident_incr
              into #checksum_calc
              from sysobjects t
              join syscolumns c on t.id = c.id 
              join systypes typ on c.xtype = typ.xtype
              left join syscomments def on def.id = c.cdefault
              where t.xtype = 'U'
                and typ.name <> 'sysname'
              order by tabname, c.colorder, colname, typ.status
           select tabname, convert(varbinary(8), getchecksum(tabname))
               from #checksum_calc
           drop table #checksum_calc";

        object[][] data = SqlExecRecordset(query);

        foreach (object[] row in data)
        {
            string name = (string)row[0];
            ulong checksum = 0;
            foreach (byte b in (byte[])row[1])
            {
                checksum <<= 8;
                checksum |= b;
            }

            // Tasks_#* should be ignored
            if (name.StartsWith("Tasks_#")) 
                continue;

            string key = String.Format("Table/{0}", name);

            log.print("name={0}, checksum={1}, key={2}\n", name, checksum, key);
            sums.Add(key, checksum);
        }
    }

    internal void GetIndexChecksums(VxSchemaChecksums sums)
    {
        string query = @"
            select 
               convert(varchar(128), object_name(i.object_id)) tabname,
               convert(varchar(128), i.name) idxname,
               convert(int, i.type) idxtype,
               convert(int, i.is_unique) idxunique,
               convert(int, i.is_primary_key) idxprimary,
               convert(varchar(128), c.name) colname,
               convert(int, ic.index_column_id) colid,
               convert(int, ic.is_descending_key) coldesc
              into #checksum_calc
              from sys.indexes i
              join sys.index_columns ic
                 on ic.object_id = i.object_id
                 and ic.index_id = i.index_id
              join sys.columns c
                 on c.object_id = i.object_id
                 and c.column_id = ic.column_id
              where object_name(i.object_id) not like 'sys%' 
                and object_name(i.object_id) not like 'queue_%'
              order by i.name, i.object_id, ic.index_column_id
              
            select
               tabname, idxname, colid, 
               convert(varbinary(8), getchecksum(idxname))
              from #checksum_calc
            drop table #checksum_calc";

        object[][] data = SqlExecRecordset(query);

        foreach (object[] row in data)
        {
            string tablename = (string)row[0];
            string indexname = (string)row[1];
            ulong checksum = 0;
            foreach (byte b in (byte[])row[3])
            {
                checksum <<= 8;
                checksum |= b;
            }

            string key = String.Format("Index/{0}/{1}", tablename, indexname);

            log.print("tablename={0}, indexname={1}, checksum={2}, key={3}, colid={4}\n", 
                tablename, indexname, checksum, key, (int)row[2]);
            sums.Add(key, checksum);
        }
    }

    internal void GetXmlSchemaChecksums(VxSchemaChecksums sums)
    {
        string query = @"
            select sch.name owner,
               xsc.name sch,
               cast(XML_Schema_Namespace(sch.name,xsc.name) 
                    as varchar(max)) contents
              into #checksum_calc
              from sys.xml_schema_collections xsc 
              join sys.schemas sch on xsc.schema_id = sch.schema_id
              where sch.name <> 'sys'
              order by sch.name, xsc.name

            select sch, convert(varbinary(8), checksum(contents))
                from #checksum_calc
            drop table #checksum_calc";

        object[][] data = SqlExecRecordset(query);

        foreach (object[] row in data)
        {
            string schemaname = (string)row[0];
            ulong checksum = 0;
            foreach (byte b in (byte[])row[1])
            {
                checksum <<= 8;
                checksum |= b;
            }

            string key = String.Format("XMLSchema/{0}", schemaname);

            log.print("schemaname={0}, checksum={1}, key={2}\n", 
                schemaname, checksum, key);
            sums.Add(key, checksum);
        }
    }

    // Functions used for GetSchema

    internal static string RetrieveProcSchemasQuery(string type, int encrypted, 
        bool countonly, List<string> names)
    {
        string name_q = names.Count > 0 
            ? " and object_name(id) in ('" + names.Join("','") + "')"
            : "";

        string textcol = encrypted > 0 ? "ctext" : "text";
        string cols = countonly 
            ? "count(*)"
            : "object_name(id), colid, " + textcol + " ";

        return "select " + cols + " from syscomments " + 
            "where objectproperty(id, 'Is" + type + "') = 1 " + 
                "and encrypted = " + encrypted + name_q;
    }

    internal void RetrieveProcSchemas(VxSchema schema, List<string> names, 
        string type, int encrypted)
    {
        string query = RetrieveProcSchemasQuery(type, encrypted, false, names);
        log.print("Query={0}\n", query);

        object[][] data = SqlExecRecordset(query);

        int num = 0;
        int total = data.Length;
        foreach (object[] row in data)
        {
            num++;
            string name = (string)row[0];
            short colid = (short)row[1];
            string text;
            if (encrypted > 0)
            {
                byte[] bytes = row[2] == null ?  new byte[0] : (byte[])row[2];
                // BitConverter.ToString formats the bytes as "01-23-cd-ef", 
                // but we want to have them as just straight "0123cdef".
                text = System.BitConverter.ToString(bytes);
                text = text.Replace("-", "");
                log.print("bytes.Length = {0}, text={1}\n", bytes.Length, text);
            }
            else
                text = (string)row[2];


            // Skip dt_* functions and sys_* views
            if (name.StartsWith("dt_") || name.StartsWith("sys_"))
                continue;

            log.print("{0}/{1} {2}{3}/{4} #{5}\n", num, total, type, 
                encrypted > 0 ? "-Encrypted" : "", name, colid);
            // Fix characters not allowed in filenames
            name.Replace('/', '!');
            name.Replace('\n', '!');

            schema.Add(name, type, text, encrypted > 0);
        }
        log.print("{0}/{1} {2}{3} done\n", num, total, type, 
            encrypted > 0 ? "-Encrypted" : "");
    }

    internal void RetrieveIndexSchemas(VxSchema schema, List<string> names)
    {
        string idxnames = (names.Count > 0) ? 
            "and ((object_name(i.object_id)+'/'+i.name) in ('" + 
                names.Join("','") + "'))"
            : "";

        string query = @"
          select 
           convert(varchar(128), object_name(i.object_id)) tabname,
           convert(varchar(128), i.name) idxname,
           convert(int, i.type) idxtype,
           convert(int, i.is_unique) idxunique,
           convert(int, i.is_primary_key) idxprimary,
           convert(varchar(128), c.name) colname,
           convert(int, ic.index_column_id) colid,
           convert(int, ic.is_descending_key) coldesc
          from sys.indexes i
          join sys.index_columns ic
             on ic.object_id = i.object_id
             and ic.index_id = i.index_id
          join sys.columns c
             on c.object_id = i.object_id
             and c.column_id = ic.column_id
          where object_name(i.object_id) not like 'sys%' 
            and object_name(i.object_id) not like 'queue_%' " + 
            idxnames + 
          @" order by i.name, i.object_id, ic.index_column_id";

        object[][] data = SqlExecRecordset(query);

        int old_colid = 0;
        List<string> cols = new List<string>();
        for (int ii = 0; ii < data.Length; ii++)
        {
            object[] row = data[ii];

            string tabname = (string)row[0];
            string idxname = (string)row[1];
            int idxtype = (int)row[2];
            int idxunique = (int)row[3];
            int idxprimary = (int)row[4];
            string colname = (string)row[5];
            int colid = (int)row[6];
            int coldesc = (int)row[7];

            // Check that we're getting the rows in order.
            wv.assert(colid == old_colid + 1 || colid == 1);
            old_colid = colid;

            cols.Add(coldesc == 0 ? colname : colname + " DESC");

            object[] nextrow = ((ii+1) < data.Length) ? data[ii+1] : null;
            string next_tabname = (nextrow != null) ? (string)nextrow[0] : null;
            string next_idxname = (nextrow != null) ? (string)nextrow[1] : null;
            
            // If we've finished reading the columns for this index, add the
            // index to the schema.  Note: depends on the statement's ORDER BY.
            if (tabname != next_tabname || idxname != next_idxname)
            {
                string colstr = cols.Join(",");
                string indexstr;
                if (idxprimary != 0)
                {
                    indexstr = String.Format(
                        "ALTER TABLE [{0}] ADD CONSTRAINT [{1}] PRIMARY KEY{2}\n" + 
                        "\t({3});\n\n", 
                        tabname,
                        idxname,
                        (idxtype == 1 ? " CLUSTERED" : " NONCLUSTERED"),
                        colstr);
                }
                else
                {
                    indexstr = String.Format(
                        "CREATE {0}{1}INDEX [{2}] ON [{3}] \n\t({4});\n\n",
                        (idxunique != 0 ? "UNIQUE " : ""),
                        (idxtype == 1 ? "CLUSTERED " : ""),
                        idxname,
                        tabname,
                        colstr);
                }
                schema.Add(tabname + "/" + idxname, "Index", indexstr, false);
                cols.Clear();
            }
        }
    }

    internal static string XmlSchemasQuery(int count, List<string> names)
    {
        int start = count * 4000;

        string namestr = (names.Count > 0) ? 
            "and xsc.name in ('" + names.Join("','") + "')"
            : "";

        string query = @"select sch.name owner,
           xsc.name sch, 
           cast(substring(
                 cast(XML_Schema_Namespace(sch.name,xsc.name) as varchar(max)), 
                 " + start + @", 4000) 
            as varchar(4000)) contents
          from sys.xml_schema_collections xsc 
          join sys.schemas sch on xsc.schema_id = sch.schema_id
          where sch.name <> 'sys'" + 
            namestr + 
          @" order by sch.name, xsc.name";

        return query;
    }

    internal void RetrieveXmlSchemas(VxSchema schema, List<string> names)
    {
        bool do_again = true;
        for (int count = 0; do_again; count++)
        {
            do_again = false;
            string query = XmlSchemasQuery(count, names);

            object[][] data = SqlExecRecordset(query);

            foreach (object[] row in data)
            {
                string owner = (string)row[0];
                string name = (string)row[1];
                string contents = (string)row[2];

                if (contents == "")
                    continue;

                do_again = true;

                if (count == 0)
                    schema.Add(name, "XMLSchema", String.Format(
                        "CREATE XML SCHEMA COLLECTION [{0}].[{1}] AS '", 
                        owner, name), false);

                schema.Add(name, "XMLSchema", contents, false);
            }
        }

        // Close the quotes on all the XMLSchemas
        foreach (KeyValuePair<string, VxSchemaElement> p in schema)
        {
            if (p.Value.type == "XMLSchema")
                p.Value.text += "'\n";
        }
    }

    internal void RetrieveTableColumns(VxSchema schema, List<string> names)
    {
        string tablenames = (names.Count > 0 
            ? "and t.name in ('" + names.Join("','") + "')"
            : "");

        string query = @"select t.name tabname,
	   c.name colname,
	   typ.name typename,
	   c.length len,
	   c.xprec xprec,
	   c.xscale xscale,
	   def.text defval,
	   c.isnullable nullable,
	   columnproperty(t.id, c.name, 'IsIdentity') isident,
	   ident_seed(t.name) ident_seed, ident_incr(t.name) ident_incr
	  from sysobjects t
	  join syscolumns c on t.id = c.id 
	  join systypes typ on c.xtype = typ.xtype
	  left join syscomments def on def.id = c.cdefault
	  where t.xtype = 'U'
	    and typ.name <> 'sysname' " + 
	    tablenames + @"
	  order by tabname, c.colorder, typ.status";

        object[][] data = SqlExecRecordset(query);

        List<string> cols = new List<string>();
        for (int ii = 0; ii < data.Length; ii++)
        {
            object[] row = data[ii];

            string tabname = (string)row[0];
            string colname = (string)row[1];
            string typename = (string)row[2];
            short len = (short)row[3];
            byte xprec = (byte)row[4];
            byte xscale = (byte)row[5];
            string defval = row[6] == null ? "" : (string)row[6];
            int isnullable = (int)row[7];
            int isident = (int)row[8];
            string ident_seed = row[9] == null ? null : 
                ((Decimal)row[9]).ToString();
            string ident_incr = row[10] == null ? null : 
                ((Decimal)row[10]).ToString();

            if (isident == 0)
                ident_seed = ident_incr = null;

            string lenstr = "";
            if (typename.EndsWith("nvarchar") || typename.EndsWith("nchar"))
            {
                if (len == -1)
                    lenstr = "(max)";
                else
                {
                    len /= 2;
                    lenstr = String.Format("({0})", len);
                }
            }
            else if (typename.EndsWith("char") || typename.EndsWith("binary"))
            {
                lenstr = (len == -1 ? "(max)" : String.Format("({0})", len));
            }
            else if (typename.EndsWith("decimal") || 
                typename.EndsWith("numeric") || typename.EndsWith("real"))
            {
                lenstr = String.Format("({0},{1})", xprec,xscale);
            }

            if (defval != null && defval != "")
            {
                // MSSQL returns default values wrapped in ()s
                if (defval[0] == '(' && defval[defval.Length - 1] == ')')
                    defval = defval.Substring(1, defval.Length - 2);
            }

            cols.Add(String.Format("[{0}] [{1}]{2}{3}{4}{5}",
                colname, typename, 
                ((lenstr != "") ? " " + lenstr : ""),
                ((defval != "") ? " DEFAULT " + defval : ""),
                ((isnullable != 0) ? " NULL" : " NOT NULL"),
                ((isident != 0) ?  String.Format(
                    " IDENTITY({0},{1})", ident_seed, ident_incr) :
                    "")));

            string next_tabname = ((ii+1) < data.Length ? 
                (string)data[ii+1][0] : null);
            if (tabname != next_tabname)
            {
                string tablestr = String.Format(
                    "CREATE TABLE [{0}] (\n\t{1});\n\n",
                    tabname, cols.Join(",\n\t"));
                schema.Add(tabname, "Table", tablestr, false);

                cols.Clear();
            }
        }
    }

    // Returns a blob of text that can be used with PutSchemaData to fill 
    // the given table.
    internal string GetSchemaData(string tablename)
    {
        string query = "SELECT * FROM " + tablename;

        StringBuilder result = new StringBuilder();
        List<string> values = new List<string>();
        try
        {
            // We need to get type information too, so we can't just call
            // SqlExecRecordset.  This duplicates some of VxDb.ExecRecordset.
            using (SqlConnection conn = GetConnection())
            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                List<string> cols = new List<string>();
                System.Type[] types = new System.Type[reader.FieldCount];
                using (DataTable schema = reader.GetSchemaTable())
                {
                    for (int ii = 0; ii < schema.DefaultView.Count; ii++)
                    {
                        DataRowView col = schema.DefaultView[ii];
                        cols.Add("[" + col["ColumnName"].ToString() + "]");
                        types[ii] = (System.Type)col["DataType"];
                    }
                }

                string prefix = String.Format("INSERT INTO {0} ({1}) VALUES (", 
                    tablename, cols.Join(","));

                while (reader.Read())
                {
                    values.Clear();
                    for (int ii = 0; ii < reader.FieldCount; ii++)
                    {
                        bool isnull = reader.IsDBNull(ii);

                        string elem = reader.GetValue(ii).ToString();
                        if (isnull)
                            values.Add("NULL");
                        else if (types[ii] == typeof(System.String) || 
                            types[ii] == typeof(System.DateTime))
                        {
                            // Double-quote chars for SQL safety
                            string esc = elem.Replace("'", "''");
                            values.Add("'" + esc + "'");
                        }
                        else
                            values.Add(elem);
                    }
                    result.Append(prefix + values.Join(",") + ");\n");
                }
            }
        }
        catch (SqlException e)
        {
            throw new VxSqlException(e.Message, e);
        }

        return result.ToString();
    }

    // Delete all rows from the given table and replace them with the given
    // data.  text is an opaque hunk of text returned from GetSchemaData.
    internal void PutSchemaData(string tablename, string text)
    {
        SqlExecScalar(String.Format("DELETE FROM [{0}]", tablename));
        SqlExecScalar(text);
    }
}

