using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Data;

namespace DMTServices
{
    public class SqlHelper
    {
        public enum dbType { SqlServer, Oracle, Access, DBF };

        private ArrayList ColumnList = new ArrayList();
        private ArrayList ValueList = new ArrayList();
        private ArrayList QuotedList = new ArrayList();
        private ArrayList CompareList = new ArrayList();

        private ArrayList FieldList = new ArrayList();
        private ArrayList LengthList = new ArrayList();

        private dbType DataSource = dbType.SqlServer;

        public MessLog mess = null;
        private bool inWhere = false;

        // for date time parsing
        //CultureInfo MyCultureInfo = new CultureInfo("en-US");

        public SqlHelper()
        {
        }

        public SqlHelper(dbType typ)
        {
            DataSource = typ;
        }

        // Orcale's connection stuff doesn't appear to have the GetSchema function?
#if !ORACLEDB
        /// <summary>
        /// Get a list of all varchar and char types and their length from the database
        /// and does a length check when .Add is called
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="conn"></param>
        public SqlHelper(string tableName, SqlConn conn)
        {
            Reset(tableName, conn);
        }

        public void Reset(string tableName, SqlConn conn)
        {
            if (conn != null)
            {

                if (conn.dbConnection.State == System.Data.ConnectionState.Open)
                {
                    FieldList.Clear();
                    LengthList.Clear();

                    DataTable dt = conn.GetSchema(tableName);
                    myDataRow myRow = new myDataRow();
#if CRAP
                    foreach (DataRow dr in dt.Rows)
                    {
                        myRow.DataRow = dr;
                        Log("========");
                        foreach (DataColumn c in dt.Columns)
                            Log(c.ColumnName + " - " + myRow.sfetch(c));
                    }
#endif
                    foreach (DataRow dr in dt.Rows)
                    {
                        try
                        {
                            myRow.DataRow = dr;
#if ORACLEDB20
                            if (myRow.sfetch("DATATYPE").Contains("CHAR"))
                            {
                                int l = 0;
                                if (myRow.ifetch("LENGTH", out l))
                                {
                                    if (l > 0)
                                    {
                                        FieldList.Add(myRow.sfetch("COLUMN_NAME").ToLower());
                                        LengthList.Add(l);
                                    }
                                }
                            }
#else
                            int l = 0;
                            if (myRow.ifetch("CHARACTER_MAXIMUM_LENGTH", out l))
                            {
                                if (l > 0)
                                {
                                    FieldList.Add(myRow.sfetch("COLUMN_NAME").ToLower());
                                    LengthList.Add(l);
                                }
                            }
#endif
                        }
                        catch (Exception e)
                        {
                            Log("Exception! - Reset " + e.Message);
                        }
                    }
                }
            }
        }
#endif

        /// <summary>
        /// return if anything has been added to the query yet
        /// </summary>
        public bool AnyQuery()
        {
            return ColumnList.Count > 0;
        }

        /// <summary>
        /// find the columns index
        /// </summary>
        public int FindIndex(string colName)
        {
            return ColumnList.IndexOf(CheckCols(colName.ToLower()));
        }

        /// <summary>
        /// Find Value for the given column name
        /// </summary>
        public string FindValue(string colName)
        {
            string Result;

            int ndx = FindIndex(colName);
            if (ndx != -1)
                Result = (string)ValueList[ndx];
            else
                Result = "";

            return Result;
        }

        /// <summary>
        /// remove a fieldname/value pair from the list...
        /// </summary>
        /// <param name="colName"></param>
        public void Remove(string colName)
        {
            int ndx = FindIndex(colName);
            if (ndx != -1)
            {
                ColumnList.RemoveAt(ndx);                     // remove the column name
                ValueList.RemoveAt(ndx);                      // and the value,
                QuotedList.RemoveAt(ndx);                     // ditto for the quotes...
            }
        }

        /// <summary>
        /// add a fieldname and value pair to the list with optional quotes
        /// </summary>
        public int Add(string colName, string value, bool quotes)
        {
            int Result;
            string v;

            Result = FindIndex(colName);    // make sure doesn't already exist
            if ((Result == -1) || inWhere)              // inWhere if building a where clause, and you often do
            {                                           // want the same field multiple times ie. where (a>1 and a<10)
                if (value != null)
                {
                    v = value.Trim();
                    if ((v.Length == 0) && !quotes)
                        v = "null";
                }
                else
                {
                    v = "null";
                    quotes = false;
                }

                Result = ColumnList.Add(CheckCols(colName.ToLower()));
                ValueList.Add(CheckVals(v, colName));         // add the value
                QuotedList.Add(quotes);   // add one to the quote flags array
            }

            return Result;
        }

        /// <summary>
        /// change a fieldname and value pair to the list with optional quotes
        /// (it will be added if it doesn't already exist)
        /// </summary>
        public int Change(string colName, string value, bool quotes)
        {
            int Result;
            string v;

            Result = FindIndex(colName);    // make sure doesn't already exist
            if (Result == -1)
                Result = Add(colName, value, quotes);
            else
            {
                if (value != null)
                {
                    v = value.Trim();
                    if ((v.Length == 0) && !quotes)
                        v = "null";
                }
                else
                {
                    quotes = false;
                    v = "null";
                }

                ValueList[Result] = CheckVals(v, colName);              // change the value
                QuotedList[Result] = quotes;        // change the value of quote flags array
            }

            return Result;
        }

        /// <summary>
        /// </summary>
        public int Change(string colName, string value)
        {
            return Change(colName, value, true);
        }

        /// <summary>
        /// change an integer value in the name value pairs
        /// (if it doesn't exist, then it will be added)
        /// </summary>
        public int Change(string colName, int i)
        {
            return Change(colName, i.ToString(), false);
        }

        public string CheckCols(string colName)
        {
            string[] check = { "order", "desc", "key" };
            foreach (string s in check)
                if (colName == s)
                    return "[" + colName + "]";
            return colName;
        }

        private void Log(string m)
        {
            if (mess != null)
                mess.Flush(m);
        }

        public string CheckVals(string v, string colName)
        {
            string rc = v;
            // if it's the key word 'null', skip any checks...
            if (rc.ToLower() != "null")
            {
                int p = FieldList.IndexOf(colName.ToLower());
                if (p >= 0)
                {
                    int l = rc.Length;
                    try
                    {
                        // it could already have quotes around it
                        if (rc.StartsWith("'") && rc.EndsWith("'"))
                        {
                            l -= -2;         // so we need to ignore them in length check...
                            if (l > (int)LengthList[p])
                                rc = rc.Substring(1, (int)LengthList[p] + 1) + "'";
                        }
                        else
                            if (l > (int)LengthList[p])
                                rc = rc.Substring(0, (int)LengthList[p]);

                    }
                    catch (Exception e)
                    {
                        Log("Exception! - CheckVals " + e.Message);
                    }
                }
            }
            return rc;
        }

        /// <summary>Clears the fields used in sql statement</summary>
        public void Clear()
        {
            ColumnList.Clear();
            ValueList.Clear();
            QuotedList.Clear();
            CompareList.Clear();

            inWhere = false;
        }

        public void Clear(bool where)
        {
            Clear();
            inWhere = where;
        }

        public void Clear(ArrayList fields, ArrayList values, ArrayList quotes)
        {
            Clear();
            ColumnList = (ArrayList)fields.Clone();
            ValueList = (ArrayList)values.Clone();
            QuotedList = (ArrayList)quotes.Clone();
        }

        /// <summary>
        /// add a fieldname and value pair to the list with quotes
        /// </summary>
        public int Add(string colName, string value)
        {
            return Add(colName, value, true);
        }

        /// <summary>
        /// add an integer value to the name value pairs
        /// </summary>
        public int Add(string colName, int i)
        {
            return Add(colName, i.ToString(), false);
        }

        /// <summary>
        /// add an extended value to the name value pairs
        /// </summary>
        public int Add(string colName, double f)
        {
            return Add(colName, f.ToString(), false);
        }

        /// <summary>
        /// add boolean value to the name value pairs, convert to 1 or 0
        /// </summary>
        public int Add(string colName, bool b)
        {
            if (b)
                return Add(colName, 1);
            else
                return Add(colName, 0);
        }

        /// <summary>
        /// add the database now() 
        /// <param name="colName">SQL column to store it to</param>
        /// </summary>
        public int AddNow(string colName)
        {
            string dateFunction = "";
            switch (DataSource)
            {
                case dbType.SqlServer:
                    dateFunction = "GETDATE()";
                    break;
                case dbType.Oracle:
                    dateFunction = "SYSDATE";
                    break;
                case dbType.Access:
                    dateFunction = "GETTIME()";
                    break;
                case dbType.DBF:
                    dateFunction = "DATETIME()";
                    break;
                default:
                    dateFunction = "NOW()";
                    break;
            }
            return Add(colName, dateFunction, false);
        }

        /// <summary>
        /// add datetime value to the name value pairs
        /// (uses fixed format of YYYY-MM-DD HH:MM:SS, check this works for non-sqlserver) 
        ///  - Oracle needs format: '01-JAN-2008' and WILL NOT accept the time portion (or at least I can't find how to do that)
        /// </summary>
        public int Add(string colName, DateTime dt)
        {
            if (dt <= DateTime.MinValue)
                return Add(colName, "null", false);
            else
                if (DataSource == dbType.Oracle)
                    return Add(colName, dt.ToString("yyyy-MM-dd HH:mm:ss"), "YYYY-MM-DD HH24:MI:SS"); //ToString("dd-MMM-yyyy"));
                else
                    if (DataSource == dbType.DBF)
                        return Add(colName, "{^" + dt.ToString("yyyy-MM-dd") + "}", false);
                    else
                        return Add(colName, dt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>
        /// add datetime string using the to_date() function of Oracle...
        ///   NOTE: this is ONLY for Oracle...
        /// </summary>
        public int Add(string colName, string dt, string format)
        {
            return Add(colName, "to_date('" + dt + "','" + format + "')", false);
        }

        public int Add(string colName, byte[] bt)
        {
            string data = "null";
            try
            {
                if (bt != null)
                    data = "0x" + myConvert.ByteArrayToString(bt);
            }
            catch
            {
                data = "null";
            }

            return Add(colName, data, false);
        }

        public int Add(string colName, List<string> o, bool quotes)
        {
            return Add(colName, new ArrayList(o), quotes);
        }

        // add in an arraylist, 
        // used to create   xxx in (aaa,bbb,ccc,...)
        // for where clauses ONLY...
        public int Add(string colName, ArrayList items, bool quotes)
        {
            int Result;

            Result = FindIndex(colName);    // make sure doesn't already exist
            if (Result == -1)
            {
                Result = ColumnList.Add(CheckCols(colName.ToLower()));
                ValueList.Add(items);       // add the array values
                QuotedList.Add(quotes);     // add one to the quote flags array
            }

            return Result;
        }

        public void SetCompare(int ndx, string Compare)
        {
            if (ndx < ColumnList.Count)
            {
                inWhere = true;
                while (CompareList.Count <= ndx)
                    CompareList.Add("=");
                CompareList[ndx] = Compare;
            }
        }

        public int Add(string colName, ArrayList items)
        {
            return Add(colName, items, true);
        }

        public int SafeAdd(string colName, object o)
        {
            return Add(colName, o);
        }

        public int Add(string colName, object o)
        {
            return Add(colName, o, false);
        }

        public int Add(string colName, object o, bool NoNulls)
        {
            if (o != null)
            {
                if (o is string)
                    return Add(colName, (string)o);
                else
                    if (o is DateTime)
                        return Add(colName, (DateTime)o);
                    else
                        if (o is int)
                            return Add(colName, (int)o);
                        else
                            if (o is double)
                                return Add(colName, (double)o);
                            else
                                if (o is bool)
                                    return Add(colName, (bool)o);
                                else
                                    if (o is byte[])
                                        return Add(colName, (byte[])o);
                                    else
                                        if(o.GetType().IsValueType)
                                            try
                                            {
                                                return Add(colName, o.ToString(), false);
                                            }
                                            catch { }
            }

            if (!NoNulls)
                return Add(colName, "null", false);
            else
                return -1;
        }

        public int Add(string colName, DataRow dr, string dn)
        {
            return Add(colName, dr, dn, false);
        }

        public int Add(string colName, DataRow dr, string dn, bool NoNulls)
        {
            return Add(colName, new myDataRow(dr), dn, NoNulls);
        }

        public int Add(string colName, myDataRow dr, string dn)
        {
            return Add(colName, dr, dn, false);
        }

        public int Add(string colName, myDataRow dr, string dn, bool NoNulls)
        {
            int rc = -1;
            object o = dr.ofetch(dn);
            if (o != null)
                rc = Add(colName, o, NoNulls);
            return rc;
        }

        public ArrayList GetValueList()
        {
            return (ArrayList)ValueList.Clone();
        }

        public ArrayList GetQuoteList()
        {
            return (ArrayList)QuotedList.Clone();
        }

        // set the DataSource type (SqlServer, Oracle, etc...)
        public void SetDataType(dbType typ)
        {
            DataSource = typ;
        }

        /// <summary>
        /// returns the query string that would get the value of the last auto increment 
        /// <param name="field">fieldname the value would be fetched from</param>
        /// <param name="tablename">table that the autoinc occurred in (ORACLE: the sequence name that was used to autoinc)</param>
        /// </summary>
        public string LastAutoInc(string field, string tablename)
        {
            string res;
            switch (DataSource)
            {
                case dbType.Oracle:
                    res = "select " + tablename + ".currval as " + field + " from dual";
                    break;
                //case dbType.SqlServer:
                default:
                    res = "SELECT IDENT_CURRENT('" + tablename + "') as " + field;
                    break;
            }
            return res;
        }


        // Orcale's connection stuff doesn't appear to have the GetSchema function?
#if !ORACLEDB
        // pass in 2 table names and create a
        //   'select ... table1 union select ... table2' string
        public static string CreateUnion(string tableName1, string tableName2, string whereClause1, string whereClause2, string orderBy, SqlConn conn)
        {
            string rc = string.Empty;
            if (conn != null)
            {
                if (conn.Connected())
                {
                    string table1 = "select ";
                    string table2 = "select ";

                    List<string> fields1 = new List<string>(conn.FieldList(tableName1));
                    List<string> fields2 = new List<string>(conn.FieldList(tableName2));
                    foreach(string field in fields1)
                    {
                        table1 += field + ",";
                        if (fields2.Contains(field))
                        {
                            table2 += field + ",";
                            fields2.Remove(field);
                        }
                        else
                            table2 += "null,";
                    }
                    // any fields left in the second list must not be in the first, so add in here...
                    foreach (string field in fields2)
                    {
                        table2 += field + ",";
                        table1 += "null as " + field + ",";
                    }
                    table1 += "0 as TableNumber";
                    table2 += "1 as TableNumber";

                    rc = table1 + " from " + tableName1 + " with (nolock) " + whereClause1 + " union " + table2 + " from " + tableName2 + " with (nolock)" + whereClause2 + " " + orderBy;
                }
            }
            return rc;
        }
#endif

        /// <summary>
        /// double up any quotes that are in a string
        /// </summary>
        private string FixUp(string Value)
        {
            return Value.Replace("'", "''");
        }

        /// <summary>
        /// if a space exists, then put quotes around it
        /// </summary>
        private string FieldFixUp(string Field)
        {
            if (Field.IndexOf(' ') >= 0)
                Field = '[' + Field + ']';

            return Field;
        }

        /// <summary>
        /// function uses the BuildInsert or BuildUpdate methods to make the SQL query
        /// </summary>
        public string BuildCmd(string table, bool insert, string suffix)
        {
            string Result;

            if (insert)
                Result = BuildInsert("INSERT INTO " + table);
            else
                Result = BuildUpdate("UPDATE " + table, suffix);

            return Result;
        }

        /// <summary>
        /// overloaded version, doesn't support a suffix on the update
        /// </summary>
        public string BuildCmd(string table, bool insert)
        {
            return BuildCmd(table, insert, "");
        }

        /// <summary>
        /// build an SQL update statement from the value pairs
        /// </summary>
        public string BuildUpdate(string prefix, string suffix)
        {
            string sqlcmd = prefix + " SET ";

            for (int i = 0; i < ColumnList.Count; i++)
            {
                if (i != 0)
                    sqlcmd = sqlcmd + ", ";     // need comma if not the first entry

                sqlcmd = sqlcmd + FieldFixUp((string)ColumnList[i]) + "=";
                string v = ValueList[i].ToString();
                if (((bool)QuotedList[i]) || (v.Length == 0))
                    sqlcmd = sqlcmd + "'" + FixUp(v) + "'";
                else
                    sqlcmd = sqlcmd + v;
            }
            if (suffix.Length > 0)
                sqlcmd = sqlcmd + " " + suffix;

            return sqlcmd;
        }

        /// <summary>
        /// build an SQL update statement from the value pairs, overload, no suffix version
        /// </summary>
        public string BuildUpdate(string prefix)
        {
            return BuildUpdate(prefix, "");
        }

        /// <summary>
        /// build an SQL insert statement from the value pairs
        /// </summary>
        public string BuildInsert(string prefix, string mid, string suffix)
        {
            string sqlcmd = prefix + " (";

            for (int i = 0; i < ColumnList.Count; i++)
            {
                if (i != 0)
                    sqlcmd += ", ";
                sqlcmd = sqlcmd + FieldFixUp((string)ColumnList[i]);
            }
            sqlcmd = sqlcmd + ") " + mid + " (";
            for (int i = 0; i < ValueList.Count; i++)
            {
                if (i != 0)
                    sqlcmd = sqlcmd + ", ";
                string v = (string)ValueList[i];
                if (((bool)QuotedList[i]) || (v.Length == 0))
                    sqlcmd = sqlcmd + "'" + FixUp(v) + "'";
                else
                    sqlcmd = sqlcmd + v;
            }
            return sqlcmd + ")" + suffix;
        }

        /// <summary>
        /// build an SQL insert statement from the value pairs
        /// overloaded version doesn't require a suffix
        /// </summary>
        public string BuildInsert(string prefix, string mid)
        {
            return BuildInsert(prefix, mid, "");
        }

        /// <summary>
        /// build an SQL insert statement from the value pairs
        /// overloaded version doesn't require a suffix or a mid
        /// </summary>
        public string BuildInsert(string prefix)
        {
            return BuildInsert(prefix, "VALUES", "");
        }

        public string BuildWhere(string prefix, string suffix)
        {
            string Conjunct, Compare, v;

            string sqlcmd = "";
            if (prefix.Length > 0)
                sqlcmd += (prefix + " ");

            for (int i = 0; i < ColumnList.Count; i++)
            {
                if (i != 0)
                {
                    // ToDo: add in AndOrList stuff...

                    // they may not have set the conjuction operator, default to 'and' ...
                    //if(i < AndOrList.Count)
                    //    Conjunct = AndOrList[i];
                    //else
                    Conjunct = "and";

                    sqlcmd += (" " + Conjunct + " ");
                }

                if (ValueList[i] is ArrayList)
                {
                    ArrayList av = (ArrayList)ValueList[i];
                    string s = "";
                    for (int j = 0; j < av.Count; j++)
                    {
                        v = (string)av[j];
                        if ((bool)QuotedList[i] || (v.Length == 0))
                            v = "'" + FixUp(v) + "'";

                        if (j > 0)
                            s += ",";
                        s += v;
                    }
                    sqlcmd += (FieldFixUp((string)ColumnList[i]) + " in (" + s + ")");
                }
                else
                {
                    // ToDo: add in CompareList stuff...
                    // they may not have set the comparison operator, default to '=' ...
                    if (i < CompareList.Count)
                        Compare = (string)CompareList[i];
                    else
                        Compare = "=";
                    v = (string)ValueList[i];

                    if ((Compare == "=") && (v.ToLower() == "null"))
                        Compare = " is ";

                    sqlcmd += (FieldFixUp((string)ColumnList[i]) + Compare);

                    if ((bool)QuotedList[i] || (v.Length == 0))
                        sqlcmd += ("'" + FixUp(v) + "'");
                    else
                        sqlcmd += v;
                }
            }

            // may never need this, but it's here...
            if (suffix.Length > 0)
                sqlcmd += (" " + suffix);

            return sqlcmd;
        }

        public string BuildWhere()
        {
            return BuildWhere("", "");
        }

        public string BuildWhere(string prefix)
        {
            return BuildWhere(prefix, "");
        }

        public int DataSize()
        {
            int rc = 0;
            for (int i = 0; i < ValueList.Count; i++)
            {
                int l = ((string)ValueList[i]).Length;
                if (((bool)QuotedList[i]) || (l == 0))
                    l += 2;
                rc += l;
            }
            return rc;
        }

        /// <summary>
        /// get just the ValueList part (without the brackets)
        /// </summary>
        public string BuildValues()
        {
            string sqlcmd = "";

            for (int i = 0; i < ValueList.Count; i++)
            {
                if (i != 0)
                    sqlcmd = sqlcmd + ", ";
                string v = (string)ValueList[i];
                if (((bool)QuotedList[i]) || (v.Length == 0))
                    sqlcmd = sqlcmd + "'" + FixUp(v) + "'";
                else
                    sqlcmd = sqlcmd + v;
            }
            return sqlcmd;
        }
    }

    public static class SqlCommands
    {
        public static string CreateIndex(string name, string table, string fields)
        {
            string rc  = "CREATE INDEX [" + name + "] ON ";
            if(table.Contains("."))
                rc += table;
            else
                rc += "[" + table + "]";
            rc += " (" + fields + ")";
            return rc;
        }

        public static string DropIndex(string name, string table)
        {
            string rc = "DROP INDEX ";
            if (table.Contains("."))
                rc += table;
            else
                rc += "[" + table + "]";
            rc += ".[" + name + "]";
            return rc;
        }

        private static string ModifyColumn(string table, string column, string colDesc, bool Alter)
        {
            string rc = "ALTER TABLE ";
            if (table.Contains("."))
                rc += table;
            else
                rc += "[" + table + "]";
            if(Alter)
                rc += " ALTER COLUMN ";
            else
            rc += " ADD ";
            rc += "[" + column + "] " + colDesc;
            return rc;
        }

        public static string AddColumn(string table, string column, string colDesc)
        {
            return ModifyColumn(table, column, colDesc, false);
        }

        public static string AlterColumn(string table, string column, string colDesc)
        {
            return ModifyColumn(table, column, colDesc, true);
        }

        public static string GetMaxID(string table, string IDColumn)
        {
            string rc = "SELECT MAX([" + IDColumn + "]) AS last FROM ";
            if (table.Contains("."))
                rc += table;
            else
                rc += "[" + table + "]";
            return rc;
        }

        public static string SetIdentityInsert(string table, bool on)
        {
            string rc = "SET IDENTITY_INSERT ";
            if (table.Contains("."))
                rc += table;
            else
                rc += "[" + table + "]";
            if (on)
                rc += " ON";
            else
                rc += " OFF";
            return rc;
        }
    }
}