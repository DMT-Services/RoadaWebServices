using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Data;
#if !NET20
using System.Linq;
#endif

namespace DMTServices
{
    public class SqlHelper
    {
        public enum dbType { SqlServer, Oracle, Access, DBF, Firebird };

        private List<string> ColumnList = new List<string>();       // column names
        private List<object> ValueList = new List<object>();        // values to insert/update
        private List<bool> QuotedList = new List<bool>();           // should the value be quoted
        private List<string> CompareList = new List<string>();      // used for building WHERE clause (defaults to '=')

        private List<string> FieldList = new List<string>();        // these 2 are optionally used to control max data length
        private List<int> LengthList = new List<int>();             // so you don't get 'overflow' errors when adding data

        private Dictionary<string, int> DecimalDict = new Dictionary<string, int>();    // used by Reset() method to truncate arithmetic erros in decimal columns

        private dbType DataSource = dbType.SqlServer;               // used when doing some Database type specific stuff (ie. SqlServer - GETDATE() vs Oracle - SYSDATE)

        public MessLog mess = null;                                 // not really used but does log a couple of Exceptions should they occur...
        private bool inWhere = false;                               // flag used preparing for a WHERE

        // for date time parsing
        //CultureInfo MyCultureInfo = new CultureInfo("en-US");

        public SqlHelper()
        {
        }

        public SqlHelper(dbType typ)
        {
            DataSource = typ;
        }

        // 'older' Oracle's connection stuff doesn't appear to have the GetSchema function?
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

        /// <summary>
        /// if you call this for a given tableName, it will fill an array of max lengths for
        /// all char type data and, each time you add() values it will check the data is not
        /// longer than the max for that field and, if it is, it will truncate it...
        /// </summary>
        /// <param name="tableName">name of table to check</param>
        /// <param name="conn">SqlConn connection (should be open)</param>
        public void Reset(string tableName, SqlConn conn)
        {
            if (conn != null)
            {
                if (conn.dbConnection.State == System.Data.ConnectionState.Open)
                {
                    FieldList.Clear();
                    LengthList.Clear();

                    DecimalDict.Clear();

                    DataTable dt = conn.GetSchema(tableName);
#if CRAP
                    foreach (DataRow dr in dt.Rows)
                    {
                        Log("========");
                        foreach (DataColumn c in dt.Columns)
                            Log(c.ColumnName + " - " + dr.sfetch(c));
                    }
#endif
                    foreach (DataRow dr in dt.Rows)
                    {
                        try
                        {
#if ORACLEDB20
                            if (dr.sfetch("DATATYPE").Contains("CHAR"))
                            {
                                int l = 0;
                                if (dr.ifetch("LENGTH", out l))
                                {
                                    if (l > 0)
                                    {
                                        FieldList.Add(dr.sfetch("COLUMN_NAME").ToLower());
                                        LengthList.Add(l);
                                    }
                                }
                            }
#else
                            int l = 0;
                            if (dr.ifetch("CHARACTER_MAXIMUM_LENGTH", out l))
                            {
                                if (l > 0)
                                {
                                    FieldList.Add(dr.sfetch("COLUMN_NAME").ToLower());
                                    LengthList.Add(l);
                                }
                            }
                            else
                            {
                                // only if decimal numeric type
                                if (dr.fetch("DATA_TYPE") == "decimal")
                                    DecimalDict.Add(dr.sfetch("COLUMN_NAME").ToLower(), dr.ifetch("NUMERIC_PRECISION") - dr.ifetch("NUMERIC_SCALE"));
                                else
                                    if (dr.fetch("DATA_TYPE") == "int")     // we'll check int types as well
                                        DecimalDict.Add(dr.sfetch("COLUMN_NAME").ToLower(), -1);
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

        public bool Empty()
        {
            return Empty(string.Empty);
        }

        /// <summary>
        /// check all ValueList values for a non-zero (or DateTime.MinValue or string.Empty or etc) value
        /// </summary>
        /// <returns></returns>
        public bool Empty(string empDate)
        {
            bool rc = true;
            for (int i = 0; i < ColumnList.Count; i++)
            {
                if (ValueList[i].ToString().ToLower() != "null")
                    switch (Type.GetTypeCode(ValueList[i].GetType()))
                    {
                        case TypeCode.DateTime:
                            rc = ((DateTime)ValueList[i] == DateTime.MinValue);
                            break;
                        case TypeCode.Int32:
                            rc = ((int)ValueList[i] == 0);
                            break;
                        case TypeCode.Double:
                            rc = ((Double)ValueList[i] == 0.0);
                            break;
                        default:
                            // these checks 'might' need some work?
                            rc = (string.IsNullOrEmpty(ValueList[i].ToString()) || ValueList[i].ToString() == "0" || ValueList[i].ToString() == empDate);
                            break;
                    }
                if (!rc)
                    break;
            }
            return rc;
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

            Result = FindIndex(colName);                // make sure doesn't already exist
            if ((Result == -1) || inWhere)              // inWhere  - if building a where clause, you often DO
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

                ColumnList.Add(CheckCols(colName.ToLower()));
                ValueList.Add(CheckVals(v, colName));         // add the value
                QuotedList.Add(quotes);   // add one to the quote flags array
                Result = ColumnList.Count;
            }

            return Result;
        }

        /// <summary>
        /// change a fieldname and value pair in the list, with optional quotes
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
        /// add a double amount to a fieldname and value pair in the list
        /// (it will be added if it doesn't already exist)
        /// </summary>
        public int AddAmount(string colName, double value)
        {
            int Result;

            Result = FindIndex(colName);    // make sure doesn't already exist
            if (Result == -1)
                Result = Add(colName, value);
            else
                ValueList[Result] = (Convert.ToDouble(ValueList[Result]) + value).ToString();

            return Result;
        }

        /// <summary>
        /// add a integer amount to a fieldname and value pair in the list
        /// (it will be added if it doesn't already exist)
        /// </summary>
        public int AddAmount(string colName, int value)
        {
            int Result;
            
            Result = FindIndex(colName);    // make sure doesn't already exist
            if (Result == -1)
                Result = Add(colName, value);
            else
                ValueList[Result] = (Convert.ToInt32(ValueList[Result]) + value).ToString();

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
            if (DataSource == dbType.SqlServer)
            {
                string[] check = { "order", "desc", "key", "user", "type", "option" };
                foreach (string s in check)
                    if (colName == s)
                        return "[" + colName + "]";
            }
            return colName;
        }

        private void Log(string m)
        {
            if (mess != null)
                mess.Flush(m);
        }

        // if FieldList/LengthList have been setup (by a call to Reset)
        // then this will check the value being added for a column
        // against LengthList and shorten it to fit if needed...
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
                            l -= 2;         // so we need to ignore them in length check...
                            if (l > LengthList[p])
                                rc = rc.Substring(0, LengthList[p] + 1) + "'";
                        }
                        else
                            if (l > LengthList[p])
                                rc = rc.Substring(0, LengthList[p]);

                    }
                    catch (Exception e)
                    {
                        Log("Exception! - CheckVals " + e.Message);
                    }
                }
                else
                {
                    // handle decimal values
                    int maxLen = 0;
                    if (DecimalDict.TryGetValue(colName.ToLower(), out maxLen))
                    {
                        v = v.Trim(); // we don't want any trailing/leading white space
                        if (maxLen == -1)       // "int" datatype?
                        {
                            try
                            {
                                Convert.ToInt32(v);
                            }
                            catch
                            {
                                rc = "0";       // force it to be zero on overflow
                            }
                        }
                        else
                        {
                            int expNdx = v.IndexOf('E');    // look for floating point
                            if (expNdx >= 0)    // floating point?
                            {
                                string expStr = v.Substring(expNdx + 1);
                                if (expStr.Length > 0)
                                {
                                    if (expStr[0] == '+')
                                    {
                                        int expValue = 0;
                                        try
                                        {
                                            expValue = Convert.ToInt32(expStr.Substring(1));
                                        }
                                        catch { }
                                        if (expValue > maxLen)
                                            rc = new String('9', maxLen);
                                    }
                                }
                            }
                            else
                            {
                                string wholeNum = v;
                                int wholeLen = v.IndexOf('.');
                                if (wholeLen == 0)
                                    wholeLen = wholeNum.Length;
                                if (wholeLen > maxLen)
                                    rc = new String('9', maxLen);
                            }
                        }
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
            ColumnList.AddRange((string[])fields.ToArray(typeof(string)));
            ValueList.AddRange((object[])values.ToArray(typeof(object)));
            QuotedList.AddRange((bool[])quotes.ToArray(typeof(bool)));
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
        /// add an extended value to the name value pairs
        /// </summary>
        public int Add(string colName, Int64 i64)
        {
            return Add(colName, i64.ToString(), false);
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

        public int AddnotEmtpy(string colName, double value)
        {
            int rc = -1;
            if (value != 0.0)
                rc = Add(colName, value);
            return rc;
        }

        public int AddnotEmtpy(string colName, int value)
        {
            int rc = -1;
            if (value != 0)
                rc = Add(colName, value);
            return rc;
        }

        public int AddnotEmtpy(string colName, string value)
        {
            int rc = -1;
            if (!string.IsNullOrEmpty(value))
                rc = Add(colName, value);
            return rc;
        }

        public int AddnotEmtpy(string colName, DateTime value)
        {
            int rc = -1;
            if (value != DateTime.MinValue)
                rc = Add(colName, value);
            return rc;
        }


        /// <summary>
        /// add in the double value, but ONLY if stmt has no data, or different data than what is in stmt
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="value"></param>
        /// <param name="stmt"></param>
        /// <returns>greater than 0, added</returns>
        public int AddDiff(string colName, double value, SqlStmt stmt)
        {
            int rc = -1;
            if (!stmt.hasData() || value != stmt.dfetch(colName))
                rc = Add(colName, value);
            return rc;
        }

        /// <summary>
        /// add in the int value, but ONLY if stmt has no data, or different data than what is in stmt
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="value"></param>
        /// <param name="stmt"></param>
        /// <returns>greater than 0, added</returns>
        public int AddDiff(string colName, int value, SqlStmt stmt)
        {
            int rc = -1;
            if (!stmt.hasData() || value != stmt.ifetch(colName))
                rc = Add(colName, value);
            return rc;
        }

        /// <summary>
        /// add in the string value, but ONLY if stmt has no data, or different data than what is in stmt
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="value"></param>
        /// <param name="stmt"></param>
        /// <returns>greater than 0, added</returns>
        public int AddDiff(string colName, string value, SqlStmt stmt)
        {
            int rc = -1;
            if (!stmt.hasData() || value != stmt.fetch(colName))
                rc = Add(colName, value);
            return rc;
        }

        /// <summary>
        /// add in the DateTime value, but ONLY if stmt has no data, or different data than what is in stmt
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="value"></param>
        /// <param name="stmt"></param>
        /// <returns>greater than 0, added</returns>
        public int AddDiff(string colName, DateTime value, SqlStmt stmt)
        {
            int rc = -1;
            if (!stmt.hasData() || value != stmt.dtfetch(colName))
                rc = Add(colName, value);
            return rc;
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
                case dbType.Firebird:
                    dateFunction = "current_timestamp";
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
            if (dt == DateTime.MinValue)
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
            if (DataSource == dbType.Oracle)
                return Add(colName, "to_date('" + dt + "','" + format + "')", false);
            else
                return Add(colName, dt);     // if not Oracle, should still do something?!
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

        public int Add(string colName, List<byte> bl)
        {
            return Add(colName, bl.ToArray());
        }

        /// <summary>
        /// Adds all the values from the List
        /// used to create   xxx in (aaa,bbb,ccc,...) for where clause
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="o"></param>
        /// <param name="quotes"></param>
        /// <returns></returns>
#if CRAP
        public int Add(string colName, List<string> o, bool quotes)
        {
            return Add(colName, new ArrayList(o), quotes);
        }

        /// <summary>
        /// Adds all the values from the List
        /// used to create   xxx in (aaa,bbb,ccc,...) for where clause
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="o"></param>
        /// <param name="quotes"></param>
        /// <returns></returns>
        public int Add(string colName, List<int> o)
        {
            return Add(colName, new ArrayList(o), false);
        }
#endif

#if !NET20
        public int Add(string colName, HashSet<string> items, bool quotes)
        {
            return Add(colName, new ArrayList(items.ToArray()), quotes);
        }

        public int Add(string colName, HashSet<int> items)
        {
            return Add(colName, new ArrayList(items.ToArray()), false);
        }
#endif
        public int Add(string colName, List<int> items)
        {
            return Add(colName, new ArrayList(items), false);
        }

        public int Add(string colName, List<string> items, bool quotes)
        {
            return Add(colName, new ArrayList(items), quotes);
        }

        /// <summary>
        /// add in an arraylist, 
        /// used to create   xxx in (aaa,bbb,ccc,...)
        /// for where clauses ONLY...
        /// </summary>
        /// <param name="colName"></param>
        /// <param name="items"></param>
        /// <param name="quotes"></param>
        /// <returns></returns>
        public int Add(string colName, ArrayList items, bool quotes)
        {
            int Result;

            Result = FindIndex(colName);    // make sure doesn't already exist
            if (Result == -1)
            {
                ColumnList.Add(CheckCols(colName.ToLower()));
                ValueList.Add(items);       // add the array values
                QuotedList.Add(quotes);     // add one to the quote flags array
                Result = ColumnList.Count;
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
            if (o != null && !(o is System.DBNull))
            {
                if (o is string)
                    return Add(colName, (string)o);
                else if (o is DateTime)
                     return Add(colName, (DateTime)o);
                else if (o is int)
                    return Add(colName, (int)o);
                else if (o is double)
                    return Add(colName, (double)o);
                else if (o is bool)
                    return Add(colName, (bool)o);
                else if (o is byte[])
                    return Add(colName, (byte[])o);
                else
                {
                    if (o is List<byte>)
                        return Add(colName, (List<byte>)o);
                    else
                    {
                        if (o.GetType().IsValueType)
                        {
                            try
                            {
                                return Add(colName, o.ToString(), false);
                            }
                            catch { }
                        }
                    }
                }
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
            int rc = -1;
            object o = dr.ofetch(dn);
            if (o != null)
                rc = Add(colName, o, NoNulls);
            return rc;
        }

        public ArrayList GetValueList()
        {
            return new ArrayList(ValueList.ToArray());
        }

        public ArrayList GetQuoteList()
        {
            return new ArrayList(QuotedList.ToArray());
        }

        // set the DataSource type (SqlServer, Oracle, etc...)
        public void SetDataType(dbType typ)
        {
            DataSource = typ;
        }


        /*
            Please note the following information on SQLServer for the 3 ways to get Identity Insert values:

            SELECT @@IDENTITY
             It returns the last IDENTITY value produced on a connection, 
             regardless of the table that produced the value, and regardless 
             of the scope of the statement that produced the value.
             @@IDENTITY will return the last identity value entered into a table 
             in your current session. While @@IDENTITY is limited to the current 
             session, it is not limited to the current scope. If you have a trigger 
             on a table that causes an identity to be created in another table, 
             you will get the identity that was created last, even if it was the 
             trigger that created it.

            SELECT SCOPE_IDENTITY()
             It returns the last IDENTITY value produced on a connection and by 
             a statement in the same scope, regardless of the table that produced the value.
             SCOPE_IDENTITY(), like @@IDENTITY, will return the last identity value created 
             in the current session, but it will also limit it to your current scope as well. 
             In other words, it will return the last identity value that you explicitly created, 
             rather than any identity that was created by a trigger or a user defined function.

            SELECT IDENT_CURRENT(‘tablename’)
             It returns the last IDENTITY value produced in a table, regardless of the connection 
             that created the value, and regardless of the scope of the statement that produced the value.
             IDENT_CURRENT is not limited by scope and session; it is limited to a specified table. 
             IDENT_CURRENT returns the identity value generated for a specific table in any session 
             and any scope. 
        */
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
                    res = string.Format("select {0}.currval as {1} from dual", tablename, field);
                    break;
                //case dbType.SqlServer:
                default:
                    res = string.Format("SELECT IDENT_CURRENT('{0}') as {1}", tablename, field);
                    //res = string.Format("SELECT SCOPE_IDENTITY() as {0}", field);
                    break;
            }
            return res;
        }

        public static string NextSequence(string seqName)
        {
            return string.Format("SELECT NEXT VALUE FOR {0} AS {0}", seqName);
        }

        public static string CurrentSequence(string seqName)
        {
            return string.Format("SELECT current_value AS {0} FROM sys.sequences WHERE name = '{0}'", seqName);
        }

        /// <summary>
        /// Static version of above function
        /// NOTE!! Slightly different, uses the _id_seq sequencer name in Oracle, above non-status DOES NOT!
        /// </summary>
        /// <param name="field"></param>
        /// <param name="tablename"></param>
        /// <returns></returns>
        public string LastAutoIncId(string field, string tablename)
        {
            string res;
            switch (DataSource)
            {
                case dbType.Oracle:
                    res = string.Format("select {0}_id_seq.currval as {1} from dual", tablename, field);
                    break;
                //case dbType.SqlServer:
                default:
                    res = string.Format("SELECT IDENT_CURRENT('{0}') as {1}", tablename, field);
                    //res = string.Format("SELECT SCOPE_IDENTITY() as {0}", field);
                    break;
            }
            return res;
        }


        /// <summary>
        /// return the date formatted to {ts 'date'}
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string timeStamp(DateTime date)
        {
            return timeStamp(date, false);
        }

        public static string timeStamp(DateTime date, bool incMs)
        {
            return "{ts '" + date.ToString("yyyy-MM-dd HH:mm:ss" + ((incMs) ? ".fff" : "")) + "'}";
        }

        public static string timeStamp(DateTime date, dbType dbtype)
        {
            if (dbtype == dbType.Oracle)
                return string.Format("to_date('{0}', 'YYYY-MM-DD HH24:MI:SS')", date.ToString("yyyy-MM-dd HH:mm:ss"));
            else
                return timeStamp(date);
        }

        // 'older' Oracle's connection stuff doesn't appear to have the GetSchema function?
#if !ORACLEDB
        // pass in 2 table names and create a
        //   'select ... table1 union select ... table2' string
        public static string CreateUnion(string tableName1, string tableName2, string whereClause1, string whereClause2, string orderBy, SqlConn conn)
        {
            return CreateUnion(tableName1, tableName2, whereClause1, whereClause2, orderBy, conn, 0);
        }

        public static string CreateUnion(string tableName1, string tableName2, string whereClause1, string whereClause2, string orderBy, SqlConn conn, int top)
        {
            string rc = string.Empty;
            if (conn != null)
            {
                if (!conn.Connected())
                    conn.dbConnect();
                if (conn.Connected())
                {
                    string table1 = "select ";
                    string table2 = "select ";

                    List<string> fields1 = new List<string>(conn.FieldList(tableName1));
                    List<string> fields2 = new List<string>(conn.FieldList(tableName2));
                    foreach (string field in fields1)
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

                    rc = table1 + " from " + tableName1 + " with (nolock) " + whereClause1 + " union " + table2 + " from " + tableName2 + " with (nolock) " + whereClause2;
                    if (top > 0)
                        rc = "select top " + top.ToString() + " * from (" + rc + ") ur";
                    rc += " " + orderBy;
                }
            }
            return rc;
        }
#endif

        /// <summary>
        /// Fixup CR/LF by substituting with char(13) + char(10)
        /// </summary>
        /// <param name="s"></param>
        /// <returns>with + char(13) + char(10) appended to the string</returns>
        public string FixCRLF(string Value)
        {
            if (DataSource == dbType.SqlServer)
                Value = Value.Replace("\r\n", "'+char(13)+char(10)+'");
            return Value;
        }

        /// <summary>
        /// double up any quotes that are in a string
        /// </summary>
        public static string FixUp(string Value)
        {
            return Value.Replace("'", "''");
        }

        /// <summary>
        /// double up any quotes that are in a string AND
        /// Fixup CR/LF by substituting with char(13) + char(10) (if SqlServer, not sure how to do it for other DataSources)
        /// </summary>
        public string FixUpEx(string Value)
        {
            Value = Value.Replace("'", "''");
            if (DataSource == dbType.SqlServer)
                Value = Value.Replace("\r\n", "'+char(13)+char(10)+'");
            return Value;
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

        public string BuildCmd(string table, string suffix)
        {
            return BuildCmd(table, false, string.Empty, suffix, false);
        }

        public string BuildCmd(string table, bool insert, string suffix)
        {
            return BuildCmd(table, insert, string.Empty, suffix, false);
        }

        /// <summary>
        /// function uses the BuildInsert or BuildUpdate methods to make the SQL query
        /// </summary>
        public string BuildCmd(string table, bool insert, string suffix, bool rowLock)
        {
            return BuildCmd(table, insert, string.Empty, suffix, rowLock);
        }

        public string BuildCmd(string table, bool insert, string output, string suffix, bool rowLock)
        {
            string Result;

            if (insert)
            {
                string mid = "VALUES";
                if (output != string.Empty)
                    mid = "output inserted." + output + " " + mid;
                Result = BuildInsert("INSERT INTO " + table, mid);
            }
            else
                Result = BuildUpdate("UPDATE " + table + ((rowLock) ? " with (rowlock)" : ""), suffix);

            return Result;
        }

        /// <summary>
        /// overloaded version, doesn't support a suffix on the update
        /// </summary>
        public string BuildCmd(string table, bool insert)
        {
            return BuildCmd(table, insert, "", false);
        }

        /// <summary>
        /// build an SQL update statement from the value pairs
        /// </summary>
        public string BuildUpdate(string prefix, string suffix)
        {
            StringBuilder sqlcmd = new StringBuilder(prefix + " SET ");

            for (int i = 0; i < ColumnList.Count; i++)
            {
                if (i != 0)
                    sqlcmd.Append(",");     // need comma if not the first entry

                sqlcmd.Append(FieldFixUp(ColumnList[i]) + "=");
                string v = ValueList[i].ToString();
                if ((QuotedList[i]) || (v.Length == 0))
                    sqlcmd.Append("'" + FixUpEx(v) + "'");
                else
                    sqlcmd.Append(v);
            }
            if (suffix.Length > 0)
                sqlcmd.Append(" " + suffix);

            return sqlcmd.ToString();
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
            StringBuilder sqlcmd = new StringBuilder(prefix + " (");

            for (int i = 0; i < ColumnList.Count; i++)
            {
                if (i != 0)
                    sqlcmd.Append(",");
                sqlcmd.Append(FieldFixUp(ColumnList[i]));
            }
            sqlcmd.Append(") " + mid + " (");
            for (int i = 0; i < ValueList.Count; i++)
            {
                if (i != 0)
                    sqlcmd.Append(",");
                string v = (string)ValueList[i];
                if ((QuotedList[i]) || (v.Length == 0))
                    sqlcmd.Append("'" + FixUpEx(v) + "'");
                else
                    sqlcmd.Append(v);
            }
            return sqlcmd.Append(")" + suffix).ToString();
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

            StringBuilder sqlcmd = new StringBuilder("");
            if (prefix.Length > 0)
                sqlcmd.Append(prefix + " ");

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

                    sqlcmd.Append(" " + Conjunct + " ");
                }

                if (ValueList[i] is ArrayList)
                {
                    ArrayList av = (ArrayList)ValueList[i];
                    string s = "";
                    for (int j = 0; j < av.Count; j++)
                    {
                        if (av[j] is int)
                            v = ((int)av[j]).ToString();
                        else
                            v = (string)av[j];
                        if (QuotedList[i] || (v.Length == 0))
                            v = "'" + FixUpEx(v) + "'";

                        if (j > 0)
                            s += ",";
                        s += v;
                    }
                    sqlcmd.Append(FieldFixUp(ColumnList[i]) + " in (" + s + ")");
                }
                else
                {
                    // they may not have set the comparison operator, default to '=' ...
                    if (i < CompareList.Count)
                        Compare = CompareList[i];
                    else
                        Compare = "=";
                    v = (string)ValueList[i];

                    if ((Compare == "=") && (v.ToLower() == "null") && !QuotedList[i])
                        Compare = " is ";

                    sqlcmd.Append(FieldFixUp(ColumnList[i]) + Compare);

                    if (QuotedList[i] || (v.Length == 0))
                        sqlcmd.Append("'" + FixUpEx(v) + "'");
                    else
                        sqlcmd.Append(v);
                }
            }

            // may never need this, but it's here...
            if (suffix.Length > 0)
                sqlcmd.Append(" " + suffix);

            return sqlcmd.ToString();
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
                if ((QuotedList[i]) || (l == 0))
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
                if ((QuotedList[i]) || (v.Length == 0))
                    sqlcmd = sqlcmd + "'" + FixUpEx(v) + "'";
                else
                    sqlcmd = sqlcmd + v;
            }
            return sqlcmd;
        }

        public string[] Compare(SqlHelper qcmp)
        {
            List<string> rc = new List<string>();
            for (int i = 0; i < ColumnList.Count; i++)
            {
                bool add = true;
                if (qcmp != null)
                {
                    int j = qcmp.FindIndex(ColumnList[i]);
                    if (j >= 0)
                        add = ((string)qcmp.ValueList[j] != (string)ValueList[i]);
                }
                if (add)
                    rc.Add(ColumnList[i]);
            }
            return rc.ToArray();
        }
    }

    // this adds a function to the SqlStmt class 'automatically',
    // beats me how!?  Anyway, it allows us to get at the stmt.Add()
    // function in dbfunc.cs without messing up any others that use the
    // SqlStmt class....
    public static partial class SqlStmtExt
    {
        // if you are getting "Cannot define a new extension method because the compiler required type..." error
        // you need to add NET20 to the project's 'Conditional compilation symbols' under the Build tab in project properties...
        public static int Add(this SqlStmt stmt, SqlHelper query, string colName, string bindName, object value)
        {
            stmt.Add(bindName, value);
            return query.Add(colName, bindName, false);
        }

        public static int Add(this SqlStmt stmt, SqlHelper query, string colName, object value)
        {
            return Add(stmt, query, colName, "@" + colName, value);
        }
    }

    public static class SqlCommands
    {
        public static string CreateIndex(string name, string table, string fields)
        {
            string rc = "CREATE INDEX [" + name + "] ON ";
            if (table.Contains("."))
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
            if (Alter)
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