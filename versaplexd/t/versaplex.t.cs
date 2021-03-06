/*
 * Versaplex:
 *   Copyright (C)2007-2008 Versabanq Innovations Inc. and contributors.
 *       See the included file named LICENSE for license information.
 */
#include "wvtest.cs.h"

using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Linq;
using Wv.Test;
using Wv;

[TestFixture]
class VxTests : VersaplexTester
{
    [Test, Category("Data")]
    public void RowOrdering()
    {
        // Make sure that data comes out in the right order when ordering is
        // requested from Versaplex

        // If these are all prime then the permutation is guaranteed to work
        // without any duplicates (I think it actually works as long as numElems
        // is coprime with the other two, but making them all prime is safe)
        const int numElems = 101;
        const int prime1 = 47;
        const int prime2 = 53;

	WVASSERT(Exec("CREATE TABLE #test1 (seq int NOT NULL, "
                    + "num int NOT NULL)"));

        // j will be a permutation of 0..numElems without resorting to random
        // numbers, while making sure that we're not inserting in sorted order.
        for (int i=0, j=0; i < numElems; i++, j = (i*prime1) % numElems) {
            // This inserts 0..numElems into seq (in a permuted order), with
            // 0..numElems in num, but permuted in a different order.
            Insert("#test1", j, (j*prime2) % numElems);
        }

	int[] vals = (from r in Reader("SELECT num FROM #test1 ORDER BY seq")
		      select (int)r[0]).ToArray();
	WVPASSEQ(vals.Length, numElems);
	for (int i = 0; i < numElems; i++)
	    WVPASSEQ(vals[i], (i*prime2) % numElems);

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Schema")]
    public void ColumnOrdering()
    {
        // Make a bunch of columns and check that they come back in the right
        // order

        // For an explanation about the permutation stuff here, see the
        // RowOrdering test, above
        const int numCols = 101;
        const int numSelected = 83;
        const int prime1 = 47;
        const int prime2 = 53;

        System.Text.StringBuilder query = new System.Text.StringBuilder(
                "CREATE TABLE #test1 (");

        for (int i=0, j=0; i < numCols; i++, j = (i*prime1) % numCols) {
            if (i > 0)
                query.Append(", ");

            query.AppendFormat("col{0} int", j);
        }

        query.Append(")");

        WVASSERT(Exec(query.ToString()));

        query = new System.Text.StringBuilder("SELECT ");

        // Don't select all of them, in case that makes a difference. But still
        // select from the entire range (as opposed to the first few), so still
        // mod by numCols instead of numSelected.
        for (int i=0, j=0; i < numSelected; i++, j = (i*prime2) % numCols) {
            if (i > 0)
                query.Append(", ");

            query.AppendFormat("col{0}", j);
        }
        query.Append(" FROM #test1");

	using (var rows = Reader(query.ToString())) {
	    var columns = rows.columns.ToArray();
            WVPASSEQ(columns.Length, numSelected);
            for (int i=0; i < numSelected; i++)
                WVPASSEQ(columns[i].name,
                        string.Format("col{0}", (i*prime2) % numCols));
        }

        WVASSERT(Exec("DROP TABLE #test1"));
    }

    [Test, Category("Data")]
    public void Unicode()
    {
        // nchar, nvarchar (in-row or max), ntext
        // Using lots of non-ascii characters
        
        string unicode_text = read_unicode();

        int [] sizes = { 4000, unicode_text.Length };
        WVASSERT(unicode_text.Length >= sizes[0]);

        string [] types = { "nchar", "nvarchar", "ntext", "nvarchar(max)" };
        int [] typemax = { 4000, 4000, Int32.MaxValue/2, Int32.MaxValue/2 };
        int [] charsize = { 2, 2, 2, 2 };
        bool [] varsize = { false, true, true, true };
        bool [] sizeparam = { true, true, false, false };
        bool [] lenok = { true, true, false, true };

        for (int i=0; i < types.Length; i++) {
            for (int j=0; j < sizes.Length && sizes[j] <= typemax[i]; j++) {
                if (sizeparam[i]) {
                    WVASSERT(Exec(string.Format("CREATE TABLE #test1 "
                                    + "(data {0}({1}), roworder int not null)",
                                    types[i], sizes[j])));
                } else {
                    WVASSERT(Exec(string.Format("CREATE TABLE #test1 "
                                    + "(data {0}, roworder int not null)",
                                    types[i])));
                    j = sizes.Length-1;
                }

                for (int k=0; k <= j; k++) {
                    WVASSERT(Exec(string.Format(
                                    "INSERT INTO #test1 VALUES (N'{0}', {1})",
                                    unicode_text.Substring(0,
                                        sizes[k]).Replace("'", "''"), k)));
                }

		WvSqlRow[] rows;

                if (lenok[i]) {
                    rows = Reader("SELECT LEN(data), DATALENGTH(data), data "
                                + "FROM #test1 ORDER BY roworder").ToArray();
                } else {
                    rows = Reader("SELECT -1, "
			       + "DATALENGTH(data), data FROM #test1 "
			       + "ORDER BY roworder").ToArray();
                }

		WVPASSEQ(rows.Length, j+1);
		for (int k=0; k <= j; k++) {
		    WvSqlRow r = rows[k];
		    if (lenok[i])
			WVPASSEQ(r[0], sizes[k]);
		    
		    WVPASSEQ(r[1], sizes[varsize[i] ? k : j]*charsize[i]);
		    WVPASSEQ(((string)r[2]).Substring(0, sizes[k]),
			     unicode_text.Substring(0, sizes[k]));
		}
 
                WVASSERT(Exec("DROP TABLE #test1"));
            }
        }
    }

    public static void Main()
    {
	WvTest.DoMain();
    }
}
