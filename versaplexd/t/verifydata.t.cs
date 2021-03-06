/*
 * Versaplex:
 *   Copyright (C)2007-2008 Versabanq Innovations Inc. and contributors.
 *       See the included file named LICENSE for license information.
 */
#include "wvtest.cs.h"

using System;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Linq;
using Wv.Test;
using Wv;

// Various tests to check that the data coming back from Versaplex is correct.
[TestFixture]
class VerifyData : VersaplexTester
{
    [Test, Category("Data")]
    public void VerifyIntegers()
    {
        // bigint, int, smallint, tinyint
        // Insert 6 rows: max, 10, 0, -10, min, nulls (except tinyint is
        // unsigned so it has 0 again instead of -10)
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)

        WVASSERT(Exec("CREATE TABLE #test1 (bi bigint, i int, si smallint, "
                    + "ti tinyint, roworder int not null)"));

        WVASSERT(Insert("#test1", Int64.MaxValue, Int32.MaxValue,
                    Int16.MaxValue, Byte.MaxValue, 1));
        WVASSERT(Insert("#test1", 10, 10, 10, 10, 2));
        WVASSERT(Insert("#test1", 0, 0, 0, 0, 3));
        WVASSERT(Insert("#test1", -10, -10, -10, 0, 4));
        WVASSERT(Insert("#test1", Int64.MinValue, Int32.MinValue,
                    Int16.MinValue, Byte.MinValue, 5));
        WVASSERT(Insert("#test1", DBNull.Value, DBNull.Value, DBNull.Value,
                    DBNull.Value, 6));

	using (var result = 
	       Reader("SELECT bi,i,si,ti FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, 6);
	    
            WVPASSEQ(rows[0][0], Int64.MaxValue);
            WVPASSEQ(rows[0][1], Int32.MaxValue);
            WVPASSEQ(rows[0][2], Int16.MaxValue);
            WVPASSEQ(rows[0][3], Byte.MaxValue);

            WVPASSEQ((Int64)rows[1][0], 10);
            WVPASSEQ((Int32)rows[1][1], 10);
            WVPASSEQ((Int16)rows[1][2], 10);
            WVPASSEQ((Byte) rows[1][3], 10);

            WVPASSEQ((Int64)rows[2][0], 0);
            WVPASSEQ((Int32)rows[2][1], 0);
            WVPASSEQ((Int16)rows[2][2], 0);
            WVPASSEQ((Byte) rows[2][3], 0);

            WVPASSEQ((Int64)rows[3][0], -10);
            WVPASSEQ((Int32)rows[3][1], -10);
            WVPASSEQ((Int16)rows[3][2], -10);
            WVPASSEQ((Byte) rows[3][3], 0);

            WVPASSEQ((Int64)rows[4][0], Int64.MinValue);
            WVPASSEQ((Int32)rows[4][1], Int32.MinValue);
            WVPASSEQ((Int16)rows[4][2], Int16.MinValue);
            WVPASSEQ((Byte) rows[4][3], Byte.MinValue);

	    WVPASS(!rows[3][0].IsNull);
	    WVPASS(!rows[3][1].IsNull);
	    WVPASS(!rows[3][2].IsNull);
	    WVPASS(!rows[3][3].IsNull);

	    WVPASS(rows[5][0].IsNull);
	    WVPASS(rows[5][1].IsNull);
	    WVPASS(rows[5][2].IsNull);
	    WVPASS(rows[5][3].IsNull);
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyBinary()
    {
        // binary, varbinary (not max)
        
        // This must be sorted
        int [] sizes = { 1, 10, 50, 255, 4000, 8000 };

        string [] types = { "varbinary", "binary" };
        int [] typemax = { 8000, 8000 };
        int [] charsize = { 1, 1 };
        bool [] varsize = { true, false };

        Byte [] binary_goop = read_goop();

        WVASSERT(binary_goop.Length >= sizes[sizes.Length-1]);

        for (int i=0; i < types.Length; i++) {
	    WVPASSEQ(types[i], types[i]);
	    
            for (int j=0; j < sizes.Length && sizes[j] <= typemax[i]; j++) {
		WVPASSEQ(sizes[j], sizes[j]);
                WVASSERT(Exec(string.Format("CREATE TABLE #test1 "
                                + "(data {0}({1}), roworder int not null)",
                                types[i], sizes[j])));

                for (int k=0; k <= j; k++) {
                    Byte[] data = new byte[sizes[k]];
                    Array.Copy(binary_goop, data, sizes[k]);

                    WVASSERT(Insert("#test1", new SqlBinary(data), k));
                }

                WVASSERT(Insert("#test1", DBNull.Value, j+1));

                using (var result = 
		       Reader("SELECT LEN(data), DATALENGTH(data), data FROM "
                            + "#test1 ORDER BY roworder"))
		{
		    var rows = result.ToArray();
		    WVPASSEQ(rows.Length, j+2);
		    
                    for (int k=0; k <= j; k++) {
                        Byte[] data = new byte[sizes[k]];
                        Array.Copy(binary_goop, data, sizes[k]);

                        int len = sizes[varsize[i] ? k : j];
                        WVPASSEQ((Int64)rows[k][0], len);

                        int datalen = sizes[varsize[i] ? k : j]*charsize[i];
                        WVPASSEQ((Int64)rows[k][1], datalen);

			Console.WriteLine(wv.fmt("{0} {1} {2} '{3}'",
						 rows[k][2].inner is SqlBinary,
						 rows[k][2].inner is byte[],
						 rows[k][2].inner is Array,
						 rows[k][2].ToString()));
			Console.WriteLine(wv.hexdump((byte[])rows[k][2]));
			Console.WriteLine(wv.hexdump(data));
                        WVPASSEQ((SqlBinary)rows[k][2], new SqlBinary(data));
                    }

                    WVPASS(rows[j+1][2].IsNull);
                }

                WVASSERT(Exec("DROP TABLE #test1"));
            }
        }
    }

    [Test, Category("Data")]
    public void VerifyBit()
    {
        // bit
        // Insert 3 rows: true, false, null
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)

        WVASSERT(Exec("CREATE TABLE #test1 (b bit, roworder int not null)"));

        WVASSERT(Insert("#test1", true, 1));
        WVASSERT(Insert("#test1", false, 2));
        WVASSERT(Insert("#test1", DBNull.Value, 3));

	using (var result = Reader("SELECT b FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, 3);
            WVPASSEQ(rows[0][0], true);
            WVPASSEQ(rows[1][0], false);
            WVPASS  (rows[2][0].IsNull);
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    // The output of LEN() or DATALENGTH() in MS SQL is an int for most types,
    // but is a BigNum for varchar(max), nvarchar(max), and varbinary(max).
    // We don't really care, so just do what it takes to get a sensible value.
    public long GetDataLength(object data)
    {
    	if (data.GetType() == typeof(Int64))
	    return (long)data;
        if (data.GetType() == typeof(Decimal))
	    return (long)(Decimal)data;
	return (int)data;
    }

    [Test, Category("Data")]
    public void VerifyChar()
    {
        try { VxExec("DROP TABLE test1"); } catch {}
        // char, nchar, varchar (in-row or max), nvarchar (in-row or max),
        // text, ntext
        // This doesn't try to use any non-ascii characters. There is a separate
        // test for that.
        
        // This must be sorted
        int [] sizes = { 1, 10, 50, 255, 4000, 8000, 8040, 8192, 16080, 16384,
            24120, 32160, 32767, 50157 };

        string [] types = { "char", "varchar", "nchar", "nvarchar", "text",
            "ntext", "varchar(max)", "nvarchar(max)" };
        int [] typemax = { 8000, 8000, 4000, 4000, Int32.MaxValue,
            Int32.MaxValue/2, Int32.MaxValue, Int32.MaxValue/2 };
        int [] charsize = { 1, 1, 2, 2, 1, 2, 1, 2 };
        bool [] varsize = { false, true, false, true, true, true, true, true };
        bool [] sizeparam = { true, true, true, true, false, false, false,
            false };
        bool [] lenok = { true, true, true, true, false, false, true, true };

        string lipsum_text = read_lipsum();

        WVASSERT(lipsum_text.Length >= sizes[sizes.Length-1]);

        for (int i=0; i < types.Length; i++) {
            for (int j=0; j < sizes.Length && sizes[j] <= typemax[i]; j++) {
                if (sizeparam[i]) {
                    WVASSERT(VxExec(string.Format("CREATE TABLE test1 "
                                    + "(data {0}({1}), roworder int not null)",
                                    types[i], sizes[j])));
                } else {
                    WVASSERT(VxExec(string.Format("CREATE TABLE test1 "
                                    + "(data {0}, roworder int not null)",
                                    types[i])));
                    j = sizes.Length-1;
                }

                for (int k=0; k <= j; k++) {
                    WVASSERT(VxExec(string.Format(
                                    "INSERT INTO test1 VALUES ('{0}', {1})",
                                    lipsum_text.Substring(0,
                                        sizes[k]).Replace("'", "''"), k)));
                    /* This doesn't work because it truncates to 4000 chars
                     * regardless of if it's a nchar/nvarchar or plain
                     * char/varchar.
                    WVASSERT(Insert("test1",
                                new SqlString(
                                    lipsum_text.Substring(0, sizes[k])), k));
                                    */
                }

                WVASSERT(Insert("test1", DBNull.Value, j+1));

                VxColumnInfo[] colinfo;
                object[][] data;
                bool[][] nullity;

                if (lenok[i]) {
                    WVASSERT(VxRecordset("SELECT LEN(data), DATALENGTH(data), "
                                +" data FROM test1 ORDER BY roworder",
                                out colinfo, out data, out nullity));
                } else {
                    WVASSERT(VxRecordset("SELECT -1, "
                                + "DATALENGTH(data), data FROM test1 "
                                + "ORDER BY roworder",
                                out colinfo, out data, out nullity));
                }

                WVPASSEQ(data.Length, j+2);

                for (int k=0; k <= j; k++) {
                    if (lenok[i])
                        WVPASSEQ(GetDataLength(data[k][0]), sizes[k]);

                    WVPASSEQ(GetDataLength(data[k][1]),
                            sizes[varsize[i] ? k : j]*charsize[i]);
                    WVPASSEQ(((string)data[k][2]).Substring(0, sizes[k]),
                            lipsum_text.Substring(0, sizes[k]));
                }

                WVPASS(nullity[j+1][2]);

                WVASSERT(Exec("DROP TABLE test1"));
            }
        }
    }

    [Test, Category("Data")]
    public void VerifyDateTime()
    {
        // datetime, smalldatetime
        // Insert 7 rows: max, a date in the future, now, a date in the past,
        // datetime epoch, min, null
        //
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)
        //
        // The actual dates don't really matter, but:
        // - The date in the past is adewhurst's birthday (approximately, PST)
        // - The date in the future is 1 second past the signed 32-bit overflow
        //   of seconds since the unix epoch (UTC)
        // - The datetime epoch is January 1 1900 at midnight
        //
        // Other notes:
        // - The min/max values of SqlDateTime are supposed to correspond to the
        //   min/max values of the SQL Server datetime type, except Mono doesn't
        //   quite have the semantics right, so the min/max values are
        //   hard-coded in instead. Bug filed with Mono.
        // - All smalldatetime values are rounded down to the nearest minute,
        //   since it only has per-minute granularity
        
        SqlDateTime epoch = new SqlDateTime(0, 0);
        SqlDateTime smallMin = epoch;
        SqlDateTime smallMax = new SqlDateTime(2079, 6, 6, 23, 59, 0, 0);

        SqlDateTime dtMin = new SqlDateTime(1753, 1, 1, 0, 0, 0, 0);
        // This is wrong, but mono seems to have trouble with the fractional
        // parts.
        SqlDateTime dtMax = new SqlDateTime(9999, 12, 31, 23, 59, 59, 0);

        SqlDateTime pastDate = new SqlDateTime(1984, 12, 2, 3, 0, 0, 0);
        SqlDateTime pastDateSmall = new SqlDateTime(1984, 12, 2, 3, 0, 0, 0);
        SqlDateTime futureDate = new SqlDateTime(2038, 6, 19, 3, 14, 8, 0);
        SqlDateTime futureDateSmall = new SqlDateTime(2038, 6, 19, 3, 14, 0, 0);

        // Mono has difficulties converting DateTime to SqlDateTime directly, so
        // take it down to per-second precision, which works reliably
        // Bug filed with Mono.
        DateTime now = DateTime.Now;
        SqlDateTime sqlNow = new SqlDateTime(now.Year, now.Month, now.Day,
                now.Hour, now.Minute, now.Second);
        SqlDateTime sqlNowSmall = new SqlDateTime(now.Year, now.Month, now.Day,
                now.Hour, now.Minute, 0);

        WVASSERT(Exec("CREATE TABLE #test1 (dt datetime, sdt smalldatetime, "
                    + "roworder int not null)"));

        WVASSERT(Insert("#test1", dtMin, smallMin, 1));
        WVASSERT(Insert("#test1", epoch, epoch, 2));
        WVASSERT(Insert("#test1", pastDate, pastDateSmall, 3));
        WVASSERT(Insert("#test1", sqlNow, sqlNowSmall, 4));
        WVASSERT(Insert("#test1", futureDate, futureDateSmall, 5));
        WVASSERT(Insert("#test1", dtMax, smallMax, 6));
        WVASSERT(Insert("#test1", DBNull.Value, DBNull.Value, 7));

	using (var result = 
	       Reader("SELECT dt, sdt FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, 7);
	    
            WVPASSEQ((SqlDateTime)rows[0][0], dtMin);
            WVPASSEQ((SqlDateTime)rows[0][1], smallMin);

            WVPASSEQ((SqlDateTime)rows[1][0], epoch);
            WVPASSEQ((SqlDateTime)rows[1][1], epoch);

            WVPASSEQ((SqlDateTime)rows[2][0], pastDate);
            WVPASSEQ((SqlDateTime)rows[2][1], pastDateSmall);

            WVPASSEQ((SqlDateTime)rows[3][0], sqlNow);
            WVPASSEQ((SqlDateTime)rows[3][1], sqlNowSmall);

            WVPASSEQ((SqlDateTime)rows[4][0], futureDate);
            WVPASSEQ((SqlDateTime)rows[4][1], futureDateSmall);

            WVPASSEQ((SqlDateTime)rows[5][0], dtMax);
            WVPASSEQ((SqlDateTime)rows[5][1], smallMax);

            WVPASS(rows[6][0].IsNull);
            WVPASS(rows[6][1].IsNull);
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyDecimal()
    {
        // decimal(38,0), decimal(38,38), decimal(18,0), decimal(1,0),
        // decimal(1,1), numeric as same types
        // Insert 6 rows: max, something positive, 0, something negative, min,
        // nulls
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)

        Byte [,] sizes = {
            // {precision, scale}
            {38, 0},
            {38, 38},
            {18, 0},
            {1, 0},
            {1, 1}
        };

        // Construct all of the things we will insert
        // These are all strings because attempting to use the SqlDecimal class
        // just leads to no end of problems. Even Microsoft's .NET
        // implementation seems to have issues with the max/min value ones.
        object [,] values = {
            {
                "99999999999999999999999999999999999999",
                "0.99999999999999999999999999999999999999",
                "999999999999999999",
                "9",
                "0.9"
            }, {
                "123456",
                "0.12345600000000000000000000000000000000",
                "123456",
                "1",
                "0.1"
            }, {
                /*
                 * The "zero" data set actually makes Mono's TDS library croak.
                 * But that's not a Versaplex bug. The other data sets should
                 * give reasonable confidence in Versaplex anyway.
                 * Bug filed with Mono.
                "0",
                "0.00000000000000000000000000000000000000",
                "0",
                "0",
                "0.0"
            }, {
                */
                "-654321",
                "-0.65432100000000000000000000000000000000",
                "-654321",
                "-1",
                "-0.1"
            }, {
                "-99999999999999999999999999999999999999",
                "-0.99999999999999999999999999999999999999",
                "-999999999999999999",
                "-9",
                "-0.9"
            }, {
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value,
                DBNull.Value
            }
        };

        // Make sure that the data is specified correctly here
        WVPASSEQ(sizes.GetLength(0), values.GetLength(1));

        // Make the table we're going to create
        System.Text.StringBuilder schema = new System.Text.StringBuilder(
                "CREATE TABLE #test1 (");

        // Make one of each decimal and numeric column. These are in fact
        // identical, but since either may show up in real-world tables, testing
        // both is a good plan
        for (int i=0; i < sizes.GetLength(0); i++) {
            schema.AppendFormat("d{0}_{1} decimal({0},{1}), "
                    + "n{0}_{1} numeric({0},{1}), ", sizes[i,0], sizes[i,1]);
        }

        schema.Append("roworder int not null)");

        WVASSERT(Exec(schema.ToString()));

        // Now insert them
        object [] insertParams = new object[2*values.GetLength(1)+1];

        for (int i=0; i < values.GetLength(0); i++) {
            insertParams[insertParams.Length-1] = i;
            for (int j=0; j < insertParams.Length-1; j++) {
                insertParams[j] = values[i,j/2];
            }
            WVASSERT(Insert("#test1", insertParams));
        }

	using (var result = Reader("SELECT * FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, values.GetLength(0));
	    
            for (int i=0; i < values.GetLength(0); i++) {
                for (int j=0; j < insertParams.Length-1; j++) {
                    if (values[i,j/2] is DBNull) {
                        WVPASS(rows[i][j].IsNull);
                    } else {
                        // The preprocessor doesn't like the comma in the array
                        // subscripts
                        string val = (string)values[i,j/2];
                        string fromdb = rows[i][j].ToString();

                        // Mono produces ".1" and "-.1"
                        // Microsoft .NET produces "0.1" and "-0.1"
                        // Deal with that here.
                        // Bug filed with Mono.
                        if (val[0] == '0' && fromdb[0] == '.') {
                            WVPASSEQ(fromdb, val.Substring(1));
                        } else if (val[0] == '-' && val[1] == '0'
                                && fromdb[0] == '-' && fromdb[1] == '.') {
                            WVPASSEQ(fromdb, "-" + val.Substring(2));
                        } else {
                            WVPASSEQ(fromdb, val);
                        }
                    }
                }
            }
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyFloat()
    {
        // float(53), float(24), real
        // Insert 8 rows: max, something positive, smallest positive, 0,
        // smallest negative, something negative, min, nulls
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)
        //
        // Specifically, infinity, -infinity and NaN are not tested here because
        // SQL Server appears to reject them as values for float columns

        // Construct all of the things we will insert
        object [,] values = {
            {
                /* Can't use SqlDouble.MaxValue et al. because there are
                 * rounding issues in Mono somewhere that make it reject the
                 * exact maximum value. These numbers come from the SQL Server
                 * 2005 reference for the float data type
                 * Bug filed with Mono.
                SqlDouble.MaxValue,
                SqlSingle.MaxValue,
                SqlSingle.MaxValue */
                1.79E+308d,
                3.40E+38f,
                3.40E+38f
            }, {
                /* Mono has problems with sending Math.E in a way that is
                 * roundtrip-able
                 * Bug filed with Mono.
                (double)Math.E,
                (float)Math.E,
                (float)Math.E */
                2.71828182845905d,
                2.718282f,
                2.718282f
            }, {
                /* Can't use Double.Epsilon or Single.Epsilon because SQL server
                 * complains, even on the Microsoft .NET implementation
                 * These numbers come from the SQL Server 2005 reference for the
                 * float data type
                Double.Epsilon,
                Single.Epsilon,
                Single.Epsilon */
                2.23E-308d,
                1.18E-38f,
                1.18E-38f
            }, {
                0.0d,
                0.0f,
                0.0f
            }, {
                /*
                -Double.Epsilon,
                -Single.Epsilon,
                -Single.Epsilon */
                -2.23E-308d,
                -1.18E-38f,
                -1.18E-38f
            }, {
                -127.001d,
                -1270.01f,
                -12700.1f
            }, {
                /*
                SqlDouble.MinValue,
                SqlSingle.MinValue,
                SqlSingle.MinValue */
                -1.79E+308d,
                -3.40E+38f,
                -3.40E+38f
            }, {
                DBNull.Value,
                DBNull.Value,
                DBNull.Value
            }
        };

        WVASSERT(Exec("CREATE TABLE #test1 (f53 float(53), f24 float(24), "
                    + "r real, roworder int not null)"));

        // Now insert them
        object [] insertParams = new object[values.GetLength(1)+1];

        for (int i=0; i < values.GetLength(0); i++) {
            insertParams[insertParams.Length-1] = i;
            for (int j=0; j < insertParams.Length-1; j++) {
                insertParams[j] = values[i,j];
            }
            WVASSERT(Insert("#test1", insertParams));
        }

	using (var result = Reader("SELECT * FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, values.GetLength(0));
	    
            for (int i=0; i < values.GetLength(0); i++) {
                for (int j=0; j < insertParams.Length-1; j++) {
                    // The preprocessor doesn't like the comma in the array
                    // subscripts
                    object val = values[i,j];

                    if (val is DBNull) {
                        WVPASS(rows[i][j].IsNull);
                    } else if (val is double) {
                        WVPASSEQ((double)rows[i][j], (double)val);
                    } else if (val is float) {
                        WVPASSEQ((float)rows[i][j], (float)val);
                    } else {
                        // If we get here, a data type was used in the values
                        // array that's not handled by one of the above cases
                        bool test_is_broken = true;
                        WVFAIL(test_is_broken);
                    }
                }
            }
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyMoney()
    {
        // money, smallmoney
        // Insert 6 rows: max, a positive amount, 0, a negative amount, min,
        // null
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)

        WVASSERT(Exec("CREATE TABLE #test1 (m money, sm smallmoney, "
                    + "roworder int not null)"));

        WVASSERT(Insert("#test1", SqlMoney.MaxValue, 214748.3647m, 1));
        WVASSERT(Insert("#test1", 1337.42m, 1337.42m, 2));
        WVASSERT(Insert("#test1", 0.0m, 0.0m, 3));
        WVASSERT(Insert("#test1", -3.141m, -3.141m, 5));
        WVASSERT(Insert("#test1", SqlMoney.MinValue, -214748.3648m, 6));
        WVASSERT(Insert("#test1", DBNull.Value, DBNull.Value, 7));

        // Cast the return type because Mono doesn't properly handle negative
        // money amounts
        // Bug filed with Mono.
	using (var result = Reader("SELECT m, sm "
                    + "FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, 6);
	    
	    WVPASSEQ(rows[0][0], SqlMoney.MaxValue.ToDecimal());
            WVPASSEQ(rows[0][1], 214748.3647m);

            WVPASSEQ(rows[1][0], 1337.42m);
            WVPASSEQ(rows[1][1], 1337.42m);

            WVPASSEQ(rows[2][0], 0m);
            WVPASSEQ(rows[2][1], 0m);

            WVPASSEQ(rows[3][0], -3.141m);
            WVPASSEQ(rows[3][1], -3.141m);

            WVPASSEQ(rows[4][0], SqlMoney.MinValue.ToDecimal());
            WVPASSEQ(rows[4][1], -214748.3648m);

            WVPASS(rows[5][0].IsNull);
            WVPASS(rows[5][1].IsNull);
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyTimestamp()
    {
        // Create a table with a timestamp column, create a bunch of rows in a
        // particular order, then check that they match up after copying

        // This permutation strategy is discussed in the RowOrdering test
        const int numElems = 101;
        const int prime1 = 47;

        WVASSERT(Exec("CREATE TABLE #test1 (ts timestamp, "
                    + "roworder int not null)"));

        for (int i=0, j=0; i < numElems; i++, j = (i*prime1) % numElems) {
            Insert("#test1", DBNull.Value, j);
        }

	byte[] tsdata = new byte[numElems];

	using (var result = 
	       Reader("SELECT ts,roworder FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, numElems);
	    
            for (int i=0; i < numElems; i++) {
                WVPASSEQ(rows[i][1], i);
                tsdata[i] = rows[i][0];
            }
        }

	using (var result =
	       Reader("SELECT ts,roworder FROM #test1 ORDER BY ts"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, numElems);
	    
            for (int i=0, j=0; i < numElems; i++, j = (i*prime1) % numElems) {
                WVPASSEQ(rows[i][1], j);
                WVPASSEQ(rows[i][0], tsdata[j]);
            }
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyUniqueIdentifier()
    {
        // uniqueidentifier
        // Insert 2 rows: a valid number, null
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)

        SqlGuid guid = new SqlGuid("6F9619FF-8B86-D011-B42D-00C04FC964FF");

        WVASSERT(Exec("CREATE TABLE #test1 (u uniqueidentifier, "
                    + "roworder int not null)"));

        WVASSERT(Insert("#test1", guid, 1));
        WVASSERT(Insert("#test1", DBNull.Value, 2));

	using (var result = Reader("SELECT u FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, 2);
	    WVPASSEQ((SqlGuid)rows[0][0], guid);
	    WVPASS  (rows[1][0].IsNull);
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void VerifyVarBinaryMax()
    {
        // varbinary(max), image

        // This must be sorted
        long [] sizes = { 1, 10, 50, 255, 4000, 8000, 8040, 8192, 16080, 16384,
            24120, 32160, 32768, 40200, 65536, 131072, 262144, 524288, 1048576,
            2097152, 3076506 };

        string [] types = { "varbinary(max)", "image" };

        Byte [] image_data = read_image();

        WVASSERT(image_data.Length >= sizes[sizes.Length-1]);

        foreach (string type in types) {
            WVASSERT(Exec(string.Format("CREATE TABLE #test1 "
                            + "(data {0}, roworder int not null)", type)));

            for (int k=0; k < sizes.Length; k++) {
                Byte [] data = new byte[sizes[k]];
                Array.Copy(image_data, data, sizes[k]);

                WVASSERT(Insert("#test1", new SqlBinary(data), k));
            }

            WVASSERT(Insert("#test1", DBNull.Value, sizes.Length));

	    using (var result = Reader("SELECT DATALENGTH(data), data FROM "
                        + "#test1 ORDER BY roworder"))
	    {
		var rows = result.ToArray();
		WVPASSEQ(rows.Length, sizes.Length+1);
		
                for (int k=0; k < sizes.Length; k++) {
                    byte[] data = new byte[sizes[k]];
                    Array.Copy(image_data, data, sizes[k]);

                    WVPASSEQ(rows[k][0], sizes[k]);
                    WVPASSEQ((SqlBinary)rows[k][1], new SqlBinary(data));
                }

                WVPASS(rows[sizes.Length][1].IsNull)
            }

            WVASSERT(Exec("DROP TABLE #test1"));
        }
    }

    [Test, Category("Data")]
    public void VerifyXML()
    {
        // xml
        // Insert 2 rows: some sample XML, null
        // Then check that they were copied correctly
        // Assume that the schema of the output table is correct (tested
        // elsewhere)
        // This isn't very exhaustive, so improvements are welcome.
        // This was going to use SqlXml instead of using a string, but Mono
        // doesn't support that very well.

        // This MUST not have any extra whitespace, as it will be stripped by
        // some SQL parser and won't be reproduced when it comes back out.
        // This is the style that Microsoft's .NET returns
        string xml =
            "<outside><!--hi--><element1 />Text<element2 type=\"pretty\" />"
            + "</outside>";
        // This is the style that Mono returns
        string altxml =
            "<outside><!--hi--><element1/>Text<element2 type=\"pretty\"/>"
            + "</outside>";

        WVASSERT(Exec("CREATE TABLE #test1 (x xml, "
                    + "roworder int not null)"));

        WVASSERT(Insert("#test1", xml, 1));
        WVASSERT(Insert("#test1", DBNull.Value, 2));

	using (var result = Reader("SELECT x FROM #test1 ORDER BY roworder"))
	{
	    var rows = result.ToArray();
	    WVPASSEQ(rows.Length, 2);
	    
            // Sigh. 
            if (rows[0][0] == altxml) {
                WVPASSEQ(rows[0][0], altxml);
            } else {
                WVPASSEQ(rows[0][0], xml);
            }

	    WVPASS(rows[1][0].IsNull);
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    public static void Main()
    {
	WvTest.DoMain();
    }
}
