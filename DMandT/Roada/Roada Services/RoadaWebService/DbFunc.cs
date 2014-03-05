#if OLEDB
#define QUESTIONMARK_PARAMS
#endif
using System;
using System.Data;
using System.Data.Common;
using System.Text;
#if !NET20
using System.Linq;
#endif
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// these "using xxx=yyy" are like typedef's 
#if OLEDB
using System.Data.OleDb;

using myDbConnection = System.Data.OleDb.OleDbConnection;
using myDbCommand = System.Data.OleDb.OleDbCommand;
using myDataReader = System.Data.OleDb.OleDbDataReader;
using myTransaction = System.Data.OleDb.OleDbTransaction;
using myParameter = System.Data.OleDb.OleDbParameter;

#elif ORACLEDB || ORACLEDB20
using Oracle.DataAccess.Client;

using myDbConnection = Oracle.DataAccess.Client.OracleConnection;
using myDbCommand = Oracle.DataAccess.Client.OracleCommand;
using myDataReader = Oracle.DataAccess.Client.OracleDataReader;
using myParameter = Oracle.DataAccess.Client.OracleParameter;
using myTransaction = Oracle.DataAccess.Client.OracleTransaction;

#elif MSORACLE
using System.Data.OracleClient;

using myDbConnection = System.Data.OracleClient.OracleConnection;
using myDbCommand = System.Data.OracleClient.OracleCommand;
using myDataReader = System.Data.OracleClient.OracleDataReader;
using myParameter = System.Data.OracleClient.OracleParameter;
using myTransaction = System.Data.OracleClient.OracleTransaction;

#elif ODBC
using System.Data.Odbc;

using myDbConnection = System.Data.Odbc.OdbcConnection;
using myDbCommand = System.Data.Odbc.OdbcCommand;
using myDataReader = System.Data.Odbc.OdbcDataReader;
using myParameter = System.Data.Odbc.OdbcParameter;
using myTransaction = System.Data.Odbc.OdbcTransaction;

#elif MYSQL
using Devart.Data;
using Devart.Data.MySql;

using myDbConnection = Devart.Data.MySql.MySqlConnection;
using myDbCommand = Devart.Data.MySql.MySqlCommand;
using myDataReader = Devart.Data.MySql.MySqlDataReader;
using myParameter = Devart.Data.MySql.MySqlParameter;
using myTransaction = Devart.Data.MySql.MySqlTransaction;

#elif ADO_NET
This would be 'legacy' stuff, and there is no DataReader or Transaction so probably will never be useful... ?
using ADODB;

using myDbConnection = ADODB.Connection;
using myDbCommand = ADODB.Command;
using myDataReader = ADODB.DataReader;  Nope
using myParameter = ADODB.Parameter;
using myTransaction = ADODB.Transaction;   Nope 

#else
using System.Data.SqlClient;

using myDbConnection = System.Data.SqlClient.SqlConnection;
using myDbCommand = System.Data.SqlClient.SqlCommand;
using myDataReader = System.Data.SqlClient.SqlDataReader;
using myParameter = System.Data.SqlClient.SqlParameter;
using myTransaction = System.Data.SqlClient.SqlTransaction;
#endif

#if NET20
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ExtensionAttribute : Attribute
    {
        public ExtensionAttribute() { }
    }
}

public static class ArrayExtensions
{
    public static bool Contains<T>(this T[] array, T searchValue, IEqualityComparer si) where T : class
    {
        bool rc = false;
        foreach (T av in array)
            if (si.Equals(av, searchValue))
            {
                rc = true;
                break;
            }
        return rc;
    }

    public static bool Contains<T>(this T[] array, T searchValue)
    {
        bool rc = false;
        foreach (T av in array)
            if (EqualityComparer<T>.Default.Equals(av, searchValue))
            {
                rc = true;
                break;
            }
        return rc;
    }
}
#endif

namespace DMTServices
{
    public class SqlConn
    {
        private myDbConnection connDb = null;
        private myTransaction trans = null;

        /// <summary>
        /// This is the actual connection string
        /// </summary>
        public string ConnectionData = string.Empty;   // this is the actual connection info
        public int defTimeOut = 120; 
        public MessLog Mess = null;
        public string LastError = string.Empty;

        //private bool closeDBConn = false;
        private string ConfigStr = string.Empty;      // this is the name in the .config file that contains the connection info

        private List<SqlStmt> stmts = new List<SqlStmt>();

        /// <summary>
        /// Create the handle passing in the Connection name in the .config file...
        /// </summary>
        public SqlConn(string connStr)
        {
            ConfigStr = connStr;
        }

        /// <summary>
        /// Create the handle with no connection data
        /// </summary>
        public SqlConn()
        {
        }

        /// <summary>
        /// Allow the user to get the connection...
        /// </summary>
        public myDbConnection dbConnection
        {
            get { return connDb; }
        }

        public void StartTransaction()
        {
            EndTransaction();
            if (connDb != null)
                try
                {
                    trans = connDb.BeginTransaction(IsolationLevel.ReadCommitted);  // "aTran");// (IsolationLevel.Serializable);// RepeatableRead);// ReadCommitted);// ReadUncommitted);// Snapshot);
                }
                catch (Exception e)
                {
                    if (Mess != null)
                        Mess.Flush("StartTransaction: " + e.Message);
                }
        }

        public void EndTransaction()
        {
            if (trans != null)
            {
                try
                {
                    trans.Commit();
                    trans.Dispose();
                }
                catch (Exception e)
                {
                    if (Mess != null)
                        Mess.Flush("EndTransaction: " + e.Message);
                }
                trans = null;
            }
        }

        public bool InTransaction()
        {
            return (trans != null);
        }

        public myTransaction GetTransaction()
        {
            return trans;
        }

        public void AddStmt(SqlStmt stmt)
        {
            bool added = false;
            for (int i = 0; i < stmts.Count; i++)
                if (stmts[i] == null)
                {
                    stmts[i] = stmt;
                    added = true;
                    break;
                }
            if (!added)
                stmts.Add(stmt);
        }

        public void RemoveStmt(SqlStmt stmt)
        {
            for (int i = 0; i < stmts.Count; i++)
                if (stmts[i] == stmt)
                {
                    stmts[i] = null;
                    break;
                }
        }

        /// <summary>
        /// create the connection back to the database, passing
        /// the configString which is the key in the .config file
        /// containing connect info
        /// </summary>
        /// <param name="configString"></param>
        public bool dbConnect(string configString)
        {
            if (connDb == null)
            {
                try
                {
                    if (configString.Length == 0)
                    {
                        if (ConnectionData.Length == 0)
                            ConnectionData = GetConnectionString(ConfigStr);
                    }
                    else
                        ConnectionData = GetConnectionString(configString);
#if OLEDB
                    // add in the provider stuff as it's pretty much the only
                    // thing that is different and MUST HAVE to connect...
                    //   NOTE: This may cause problems in future, please keep an eye on it!
                    if (ConnectionData.ToLower().IndexOf("provider") < 0)
                        ConnectionData = "Provider=SQLOLEDB;" + ConnectionData;
#endif
                    connDb = new myDbConnection(ConnectionData);
                    connDb.Open();
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    if (Mess != null)
                        Mess.Flush("dbConnect: " + e.Message);
                }
            }
            else
                try
                {
                    if (connDb.State != ConnectionState.Open)
                        connDb.Open();
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    if (Mess != null)
                        Mess.Flush("dbConnect: " + e.Message);
                }

            bool rc = false;
            if (connDb != null)
                rc = (connDb.State == ConnectionState.Open);
            return rc;
        }

        /// <summary>
        /// connect to the database
        /// </summary>
        public bool dbConnect()
        {
            return dbConnect(string.Empty);
        }

        public bool Connected()
        {
            if (connDb != null)
                return connDb.State == ConnectionState.Open;
            else
                return false;
        }

        public void Close()
        {
            EndTransaction();

            foreach (SqlStmt stmt in stmts)
                if (stmt != null)
                    stmt.Close(false);

            stmts.Clear();

            if (connDb != null)
            {
                connDb.Close();
                connDb.Dispose();
                connDb = null;
            }
        }

        public string FixUpTable(string tableName)
        {
            string rc = tableName;
            int p = rc.IndexOf(".");
            if (p > 0)
                rc = rc.Substring(p + 1);
            rc = rc.Replace("[", "").Replace("]", "");
            return rc;
        }

#if !ORACLEDB
        private class schemaLayout
        {
            public string schemaName;
            public ArrayList Restrictions;
        };

        // used for SchemaGet.Find()
        static Predicate<schemaLayout> ByName(string name)
        {
            return delegate(schemaLayout sl)
            {
                return (string.Compare(sl.schemaName, name, true) == 0);
            };
        }

        private List<schemaLayout> SchemaGet = new List<schemaLayout>();

        // these are the restrictions that we will be interested in...
        // first for tables/views
        private static string[] fCollectionNames = new string[] { "tables", "views" };
        private static string[] fRestrictionNames = new string[] { "owner", "table_schema", "table_schem", "databasename" };
        // and for columns/indexes
        private static string[] fDetailCollectionNames = new string[] { "columns", "indexcolumns", "indexes" };
        private static string[] fDetailRestrictionNames = new string[] { "table", "tablename", "table_name" };

        public void SetupRestrictions()
        {
            SchemaGet.Clear();
            DataTable schemaTable = dbConnection.GetSchema("Restrictions");
            if (schemaTable != null)
            {
                List<string> lRests = new List<string>(fRestrictionNames);
                List<string> lColls = new List<string>(fCollectionNames);
                List<string> lDetRests = new List<string>(fDetailRestrictionNames);
                List<string> lDetColls = new List<string>(fDetailCollectionNames);
                // these 2 are just for pointing to above...
                List<string> CollectionNames;
                List<string> RestrictionNames;

                foreach (DataRow dr in schemaTable.Rows)
                {
                    string cn = dr.fetch("CollectionName").ToLower();
                    for (int l = 0; l < 2; l++)
                    {
                        if (l == 0)
                        {
                            CollectionNames = lColls;
                            RestrictionNames = lRests;
                        }
                        else
                        {
                            CollectionNames = lDetColls;
                            RestrictionNames = lDetRests;
                        }
                        // is this one we are 'interested' in?
                        if (CollectionNames.Contains(cn))
                        {
                            string rn = dr.fetch("RestrictionName").ToLower();
                            if (RestrictionNames.Contains(rn))
                            {
                                int sndx = SchemaGet.FindIndex(ByName(cn));
                                if (sndx < 0)
                                {
                                    schemaLayout sl = new schemaLayout();
                                    sl.schemaName = cn;
                                    sl.Restrictions = new ArrayList();
                                    SchemaGet.Add(sl);
                                    sndx = SchemaGet.FindIndex(ByName(cn));
                                }

                                int r = dr.ifetch("RestrictionNumber");

#if !MYSQL
                                // only MYSQL appears to be 0 based (so far)
                                r--;
#endif
                                for (int i = 0; i < r; i++)
                                    SchemaGet[sndx].Restrictions.Add(null);

                                SchemaGet[sndx].Restrictions.Add(rn);
                            }
                            break;
                        }
                    }
                }
                schemaTable = null;
            }
        }

        private string[] GetRestrictions(ref string sname, string rest)
        {
            string dummy = string.Empty;
            return GetRestrictions(ref sname, rest, ref dummy);
        }

        private string[] GetRestrictions(ref string sname, string rest, ref string rname)
        {
            if (SchemaGet.Count == 0)
                SetupRestrictions();

            string[] restrictions = new string[0];
            rname = string.Empty;

            int sl = SchemaGet.FindIndex(ByName(sname));
            // some data connections have IndexColumns which shows better info,
            // but some don't have that as a choice, so change it to Indexes
            if (sl < 0)
                if (sname == "IndexColumns")
                {
                    sname = "Indexes";
                    sl = SchemaGet.FindIndex(ByName(sname));
                }

            if (sl >= 0)
            {
                ArrayList al = new ArrayList(SchemaGet[sl].Restrictions);
                if (al.Count > 0)
                {
                    rname = al[al.Count - 1].ToString();
                    al[al.Count - 1] = rest;
                    restrictions = (string[])al.ToArray(typeof(string));
#if MYSQL
                    if (restrictions.Length > 0)
                        if (string.IsNullOrEmpty(restrictions[0]))
                            restrictions[0] = dbConnection.Database;
#endif
                }
            }
            return restrictions;
        }

        public string[] GetTableNames()
        {
            return GetTableNames(false, string.Empty, false);
        }

        public string[] GetTableNames(bool view)
        {
            return GetTableNames(view, string.Empty, false);
        }

        public string[] GetTableNames(string owner)
        {
            return GetTableNames(false, owner, false);
        }

        public string[] GetTableNames(bool view, string owner)
        {
            return GetTableNames(view, owner, false);
        }
        
        public bool tableExists(string tableName)
        {
            return GetTableNames(false, string.Empty, false).Contains(tableName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Return an array of strings representing the table names / views in the current connection...
        /// </summary>
        /// <returns>array of strings containing table names</returns>
        public string[] GetTableNames(bool view, string owner, bool all)
        {
            List<string> Result = new List<string>();
            try
            {
                if (!Connected())
                    dbConnect();

                DataTable schemaTable = null;

                string sname = (view) ? "Views" : "Tables";
                string oname = string.Empty;
                // even if you don't want a restriction, call this to get 'oname' filled...
                string[] restrictions = GetRestrictions(ref sname, owner, ref oname);
                if (all || restrictions.Length == 0)// || owner == string.Empty)
                    schemaTable = dbConnection.GetSchema(sname);
                else
                {
                    schemaTable = dbConnection.GetSchema(sname, restrictions);
                    if (schemaTable.Rows.Count == 0)
                        schemaTable = dbConnection.GetSchema(sname);
                }

                string s;
                foreach (DataRow dr in schemaTable.Rows)
                {
                    if (all)
                    {
                        s = dr.sfetch(oname);
                        if (s.Length > 0)
                            s += ".";
                    }
                    else
                        s = string.Empty;

                    string fieldName = "TABLE_NAME";
#if ORACLEDB20 || MSORACLE
                    // Oracle has different field names depending on view or table...
                    if(view)
                        fieldName = "VIEW_NAME";
#else
#if MYSQL
                    // MySQL doesn't seem to have views?
                    fieldName = "NAME";
#else
                    bool ok = true;
                    // where as all the others use the table_type field to determine view or table...
                    if (!view)
                        ok = (dr.sfetch("TABLE_TYPE").ToLower() != "view");
                    if (ok)
#endif
#endif
                    Result.Add(s + dr.sfetch(fieldName));

                }
                schemaTable = null;
            }
            catch { }

            return Result.ToArray();
        }

        /// <summary>
        /// return the owner of a table?
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public string GetOwner(string tableName)
        {
            string owner = string.Empty;
#if ORACLEDB20 || MSORACLE
            string[] restrictions = new string[] { null, tableName.ToUpper()};
#else
            string[] restrictions = new string[] { null, null, tableName };
#endif
            DataTable schemaTable = dbConnection.GetSchema("Tables", restrictions);
            if (schemaTable.Rows.Count > 0)
                // this 'should' get the owner of the table...
                if (!Convert.IsDBNull(schemaTable.Rows[0][1]))
                    owner = myConvert.ToString(schemaTable.Rows[0][1]);
            return owner;
        }

        /// <summary>
        /// Returns schema for a given 'tableName', where column "COLUMN_NAME" returns the column names for instance.
        /// </summary>
        /// <param name="tableName">tableName to get schema</param>
        /// <returns>a DataTable containing the schema</returns>
        public DataTable GetSchema(string tableName)
        {
            return GetSchema(tableName, false);
        }

        public string[][] GetSchemasa(string tableName)
        {
            return myConvert.DataTableToStringArray(GetSchema(tableName, false));
        }

        public string[][] GetSchemasa(string tableName, bool index)
        {
            return myConvert.DataTableToStringArray(GetSchema(tableName, index));
        }

        // check if ALL strings in fields are also in nFields
        private int checkFields(List<string> nFields, string[] fields)
        {
            int rc = 0;
            if (nFields.Count > 0)
            {
                bool ok = true;
                foreach (string field in fields)
                    if (nFields.Contains(field.ToLower()))
                        nFields.Remove(field.ToLower());
                    else
                    {
                        ok = false;
                        break;
                    }

                
                if (ok)
                    if (nFields.Count == 0)
                        rc = 1;
                    else
                        // if there is one 'extra' field and it's 'id', allow that as a match (but return 2 to indicate this)
                        if (nFields.Count == 1 && nFields[0] == "id")
                            rc = 2;
            }

            return rc;
        }

        public string checkIndex(string tableName, string[] fields)
        {
            int dummy;
            return checkIndex(tableName, fields, out dummy);
        }

        public string checkIndex(string tableName, string[] fields, out int idInc)
        {
            idInc = 0;
            string rc = string.Empty;
            if (fields.Length > 0)
            {
                string ndxName = "-=-=-";
                int ok = 0;
                List<string> nFields = new List<string>();
                DataTable dat = GetSchema(tableName, true); 
                foreach (DataRow dr in dat.Rows)
                {
                    string indxName = dr["index_name"].ToString().ToLower();
                    if (indxName != ndxName)
                    {
                        ok = checkFields(nFields, fields);
                        nFields.Clear();
                        if (ok > 0)
                        {
                            idInc = ok;
                            rc = ndxName;
                            if (ok == 1)    // if ok == 2, then match found but had 3rd column; id so keep looking for exact match
                                break;
                        }
                        ndxName = indxName;
                    }

                    string col = dr["column_name"].ToString().ToLower();
                    nFields.Add(col);
                }

                if (idInc != 1)     // if we haven't found an 'exact' match, we need to check the final group
                {
                    ok = checkFields(nFields, fields);
                    if (ok > 0)
                    {
                        idInc = ok;
                        if (ok == 1 || rc == string.Empty)
                            rc = ndxName;
                    }
                }
            }
            return rc;
        }

        private string GetUser()
        {
            return getConnKey(new string[] { "user id", "uid" });
        }

        /// <summary>
        /// Return the value of a key in the connection string
        /// (ie. pass 'Data Source', returns ServerName)
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public string getConnKey(string key)
        {
            return getConnKey(new string[] { key });
        }

        /// <summary>
        /// Return the value of a key in the connection string
        /// will search for a match to any string in the string[] keys
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public string getConnKey(string[] keys)
        {
            DbConnectionStringBuilder builder = new DbConnectionStringBuilder();
            builder.ConnectionString = ConnectionData;
            foreach (string key in builder.Keys)
            {
                foreach (string k in keys)
                    if (key.ToLower() == k.ToLower())
                        return (string)builder[key];
            }
            return string.Empty;
        }

        /// <summary>
        /// Returns schema for a given 'tableName'. 
        ///     If 'index' is false, then Column information is returned (where column "COLUMN_NAME" returns the column names)
        ///     otherwise 'index' is true, then Index information is returned.
        /// </summary>
        /// <param name="tableName">tableName to get schema</param>
        /// <param name="index">set index to false for Column information, or true for Index information</param>
        /// <returns>a DataTable containing the schema</returns>
        public DataTable GetSchema(string tableName, bool index)
        {
            DataTable schemaTable = new DataTable();
            try
            {
                if (!Connected())
                    dbConnect();

                tableName = FixUpTable(tableName);
                string sname = (index) ? "IndexColumns" : "Columns";
                string[] restrictions = GetRestrictions(ref sname, tableName);
                if (restrictions.Length > 0)
                    schemaTable = dbConnection.GetSchema(sname, restrictions);

                if (index && schemaTable.Rows.Count == 0)
                {
                    // there seems to be a bug/problem when getting "Indexes"
                    // if you pass null as owner, you don't get ALL indexes, but rather
                    // only the ones that belong to the default 'owner'...
                    if (restrictions.Length > 1)
                    {
                        string owner = GetOwner(tableName);
                        if (owner.Length > 0)
                        {
                            restrictions[restrictions.Length - 2] = owner;
                            schemaTable = dbConnection.GetSchema("Indexes", restrictions);
                        }
                    }
                }
                try
                {
                    // this gets the columns in natural order, otherwise they 
                    // are returned in alphabetical order...
                    if (schemaTable.Columns.Contains("ORDINAL_POSITION"))
                    {
                        if (index)
                            schemaTable.DefaultView.Sort = "INDEX_NAME,ORDINAL_POSITION";
                        else
                            schemaTable.DefaultView.Sort = "ORDINAL_POSITION";
                        schemaTable = schemaTable.DefaultView.ToTable();
                    }
                }
                catch { /* don't bomb out just because the sort stuff fails... */ }
            }
            catch (Exception e)
            {
                if (Mess != null)
                    Mess.Flush("FieldList: " + e.Message);

                schemaTable = new DataTable();
            }
            return schemaTable;
        }

        // currently, this is only for MSSQL as the DATA_TYPE field is DATATYPE in Oracle, TYPE_NAME in firebird
        public Dictionary<string, string> TableLayout(string Table)
        {
            Dictionary<string, string> f = new Dictionary<string, string>();
            DataTable dt = GetSchema(Table, false);
            try
            {
                foreach (DataRow dr in dt.Rows)
                    f.Add(dr["COLUMN_NAME"].ToString().ToLower(), dr["DATA_TYPE"].ToString().ToLower());
            }
            catch { }

            return f;
        }

        public string[] FieldList(string Table)
        {
            List<string> f = new List<string>();
            DataTable dt = GetSchema(Table, false);
            try
            {
                foreach (DataRow dr in dt.Rows)
                    f.Add(dr["COLUMN_NAME"].ToString().ToLower());
            }
            catch { }

            return f.ToArray();
        }

        public bool FieldExist(string Table, string Field)
        {
            bool rc = false;
            DataTable dt = GetSchema(Table, false);
            foreach (DataRow dr in dt.Rows)
            {
                if (dr.sfetch("COLUMN_NAME").ToLower() == Field.ToLower())
                {
                    rc = true;
                    break;
                }
            }
            return rc;
        }

#endif

        private string GetConnectionString()
        {
            return GetConnectionString(string.Empty);
        }

        private string GetConnectionString(string ConnectName)
        {
#if ! NO_CONNECTION_MANAGER
            // You have to include 'System.configuration' in your Project to get the following to compile
            //  (right click References and select Add Reference... in Solution Explorer  --> )
            if (ConnectName.Length == 0)
                ConnectName = "DbConnectionString";             // default to this if passed as ""

            // To avoid storing the connection string in your code, 
            // you can retrieve it from a configuration file, using the 
            //System.Configuration.ConfigurationSettings.AppSettings.
            ConnectionStringSettings s = ConfigurationManager.ConnectionStrings[ConnectName];
            if (s == null)
            {
                System.Configuration.Configuration appconf = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                for (int i = 0; i < appconf.ConnectionStrings.ConnectionStrings.Count; i++)
                    if (appconf.ConnectionStrings.ConnectionStrings[i].Name.Contains(ConnectName))
                    {
                        s = appconf.ConnectionStrings.ConnectionStrings[i];
                        break;
                    }

                if (s == null)
                {
                    for (int i = 0; i < ConfigurationManager.ConnectionStrings.Count; i++)
                        if (ConfigurationManager.ConnectionStrings[i].Name.Contains(ConnectName))
                        {
                            s = ConfigurationManager.ConnectionStrings[i];
                            break;
                        }
                }
            }

            if (s != null)
                return s.ConnectionString;
            else
#endif
                return string.Empty;
        }

        /// <summary>
        /// Legacy function...
        ///   Please create an SqlConn and pass that to the 
        ///   statement handles...
        /// </summary>
        /// <param name="cDb">dbConnection</param>
        public SqlConn(myDbConnection cDb)
        {
            connDb = cDb;
        }
    }

    public class SqlStmt
    {
#if REPLICATE_DB
        /// <summary>
        /// this event is called if replication is turned on
        /// for every SQL statement
        /// </summary>
        /// <param name="sender">this SqlStmt</param>
        /// <param name="sqlStr">SQL string that gets processed for replication</param>
        /// <param name="param">passed parameters to the SQL string</param>
        public delegate void ReplicationEventHandler(object sender, myDbCommand cmd);
        static public event ReplicationEventHandler ReplicationEvent;
        public bool IgnoreReplicate = true;
#endif
        /// <summary>
        /// this event is called provided it's not null
        /// it's called for every DoSQL as well as dbSkip method call
        /// </summary>
        /// <param name="sender">this SQLStmt object</param>
        /// <param name="totSecs">total length of time for the call</param>
        /// <param name="func">function name which called it</param>
        /// <param name="cmd">SQL command string</param>
        public delegate void TimeOverEventHandler(object sender, double totSecs, string func, string cmd);

        static private event TimeOverEventHandler timeOverEvent = null;
        // this is to prevent 'adding in' the event multiple times...
        static public event TimeOverEventHandler TimeOverEvent
        {
            add
            {
                if (timeOverEvent == null || !timeOverEvent.GetInvocationList().Contains(value))
                {
                    timeOverEvent += value;
                }
            }
            remove
            {
                timeOverEvent -= value;
            }
        }

        private myDbCommand cmd = null;
        private myDataReader rdr = null;

        private SqlConn fParent;
        // for manipulating the SQLParams hashtable stuff...
        private ArrayList ParamKeys = new ArrayList();

        public string LastError = string.Empty;
        private Hashtable SQLParams = new Hashtable();

        public int RowsAffected = 0;
        private bool createdSqlConn = false;

        private bool rdrEOF = true; // best to start with EOF on, until we DoSql()...

        private List<string> frdrFields = new List<string>();

        static private MessLog _sMess = null;

        /// <summary>
        /// Create the handle, passing in the connection class...
        /// </summary>
        public SqlStmt(SqlConn conn)
        {
            fParent = conn;
            fParent.AddStmt(this);
        }

        /// <summary>
        /// Needed to expose SQLParams
        /// </summary>
        public Hashtable Params
        {
            get
            {
                return SQLParams;
            }
            set
            {
                SQLParams = value;
            }
        }

        public bool Connected()
        {
            if (fParent.dbConnection != null)
                return fParent.dbConnection.State == ConnectionState.Open;
            else
                return false;
        }

        public myDataReader Reader
        {
            get { return rdr; }
        }

        /// <summary>
        /// Executes a query, fills and returns a DataTable with the results
        /// </summary>
        /// <param name="selectStr"></param>
        /// <returns>DataTable</returns>
        public DataTable DoSQLdt(string selectStr)
        {
            DataTable dt = new DataTable();
            dt.Load(DoSQLnr(selectStr));
            return dt;
        }

        /// <summary>
        /// Executes a query, but DOES NOT do the DataReader.Read (or dbSkip() as we call it)
        /// </summary>
        /// <param name="selectStr"></param>
        /// <returns>DataReader</returns>
        public myDataReader DoSQLnr(string selectStr)
        {
            return DoSql(selectStr, selectStr.StartsWith("SELECT", StringComparison.CurrentCultureIgnoreCase) || selectStr.StartsWith("EXEC", StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Executes a query with OR WITHOUT a result set</summary>
        /// <param name="selectStr">SQL string to execute</param>
        /// <param name="select">set to false for NonQuery type statements</param>
        public bool DoSQL(string selectStr, bool select)
        {
            DoSql(selectStr, select);
            if (rdr != null)
                return dbSkip();
            else
                if (!select)
                    return RowsAffected > 0;
                else
                    return false;
        }

        public bool DoSQL(string selectStr)
        {
            return DoSQL(selectStr, selectStr.StartsWith("SELECT", StringComparison.CurrentCultureIgnoreCase) || selectStr.StartsWith("EXEC", StringComparison.CurrentCultureIgnoreCase));
        }

        public bool DoSQLFormat(string selectStr, params object[] args)
        {
            return DoSQL(string.Format(selectStr, args));
        }

        /// <summary>Executes a query with OR WITHOUT a result set</summary>
        /// <param name="selectStr">SQL string to execute</param>
        /// <param name="select">set to false for NonQuery type statements</param>
        private myDataReader DoSql(string selectStr, bool select)
        {
            try
            {
                TimeSpan ts = new TimeSpan(DateTime.Now.Ticks);
                // clear out any previous error message
                LastError = string.Empty;

                // if you call DoSql in a loop, you need to .Close the previous result set...
                rdrClose();
                rdrEOF = true;

                // this is here to support older versions...
                //if (createdSqlConn)
                // Actually, we should always do this as it can
                // get you re-connected after a connection problem...
                fParent.dbConnect();

#if QUESTIONMARK_PARAMS
                // for OLEDB stuff (and some Database engines like FireBird), you only pass "?" for each param,
                // so here we change all "@xxx"'s to "?"'s
                // ALSO NOTE: since we only pass "?"'s, the order of the
                //            params MUST MATCH the order in the select statement,
                //            so we will make sure to use ParamKeys order when doing cmd.Parameters.Add()
                if (SQLParams.Count > 0)
                {
                    // first, fill an ArrayList with all the 'keys' (@xxx) in lowercase from the 
                    // select string and replace them with "?" in the select...
                    ParamKeys.Clear();
                    selectStr = ParseKeys(selectStr, ParamKeys);
                    // now get the values in the arraylist to 'exactly' match the keys in the SQLParams
                    // so when you do 'SQLParams[ParamKeys[i]]' it will find the value...
                    string ky = "";
                    foreach (DictionaryEntry parms in SQLParams)
                    {
                        ky = (string)parms.Key;
                        // the @ sign is 'optional' in the .Add() but we filled our ArrayList
                        // from the SELECT so they all DO start with '@', so make sure here...
                        if (!ky.StartsWith("@"))
                            ky = "@" + ky;
                        int i = ParamKeys.IndexOf(ky.ToLower());
                        if (i >= 0)
                            ParamKeys[i] = (string)parms.Key;
                    }
                }
#endif
                if (cmd == null)
                    if (fParent.dbConnection != null)
                        cmd = fParent.dbConnection.CreateCommand();

                if (cmd != null)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = selectStr;
#if !(ORACLEDB  || ORACLEDB20)
                    // if we are in 'transaction mode' we must set this here...
                    if (fParent.InTransaction())
                        cmd.Transaction = fParent.GetTransaction();
#endif
                    if (SQLParams.Count > 0)
                    {
#if QUESTIONMARK_PARAMS
                        // by skipping thru in ParamKeys[i] order, we make sure the params
                        // are added in the same order as they show in select... (critical since we only pass '?')
                        for (int i = 0; i < ParamKeys.Count; i++)
                        {
                            string name = (string)ParamKeys[i];
                            if (SQLParams.ContainsKey(name))
                                cmd.Parameters.Add(new myParameter(name, SQLParams[name]));
                        }
#else
                        foreach (DictionaryEntry parms in SQLParams)
                            cmd.Parameters.Add(new myParameter((string)parms.Key, parms.Value));
#endif
                        
                        SQLParams.Clear();
                    }
                    cmd.CommandTimeout = fParent.defTimeOut;
                    RowsAffected = 0;
                    if (select)
                        rdr = cmd.ExecuteReader();
                    else
                    {
                        RowsAffected = cmd.ExecuteNonQuery();
                        rdr = null;
#if REPLICATE_DB
                        try
                        {
                            // for replication, fire an event
                            if (ReplicationEvent != null && !IgnoreReplicate)
                                ReplicationEvent(this, cmd);
                        }
                        catch { }
#endif
                    }
                    try
                    {
                        if (timeOverEvent != null)
                            timeOverEvent(this, new TimeSpan(DateTime.Now.Ticks).Subtract(ts).TotalSeconds, "DoSQL", selectStr);
                    }
                    catch { }
                }
                else
                    throw new Exception("Not Connected to DataSource!");
            }
            catch (Exception e)
            {
                if (fParent.Mess != null)
                {
                    fParent.Mess.Flush("SQL Error - " + selectStr);
                    fParent.Mess.Flush("            " + e.Message);
                }
                LastError = e.Message;
                rdrClose();
            }
            
            if (rdr != null)
                rdrEOF = !rdr.HasRows;

            return rdr;
        }

        /// <summary>Executes a query with a result set</summary>
        /// <param name="selectStr">SQL string to execute</param>
        /// <param name="Params">(Obsolete! use stmt.Add(,)) - ArrayList containing binding information in pairs (ie Params[0]=@date Params[1]=Today)</param>
        public bool DoSQL(string selectStr, ArrayList Params)
        {
            if (Params != null)
                for (int i = 0; (i + 1) < Params.Count; i += 2)
                    Add(Params[i], Params[i + 1]);
            return DoSQL(selectStr);
        }

        public string FixQuote(string s)
        {
            return s.Replace("'", "''");
        }

        /// <summary>gets the next result record from an sql statement</summary>
        public bool dbSkip()
        {
            bool rc = false;
            if (rdr != null)
            {
                try
                {
                    TimeSpan ts = new TimeSpan(DateTime.Now.Ticks);
                    rc = rdr.Read();
                    if (timeOverEvent != null)
                        timeOverEvent(this, new TimeSpan(DateTime.Now.Ticks).Subtract(ts).TotalSeconds, "dbSkip", cmd.CommandText);
                }
                catch (Exception e)
                {
                    if (fParent.Mess != null)
                        fParent.Mess.Flush("dbSkip() Exception - " + e.Message);
                    LastError = e.Message;
                }
            }
            rdrEOF = !rc;
            return rc;
        }

        /// <summary>Adds fields for binding to a sql statement</summary>
        /// <param name="field">field name (must match the @field in the sql statement)</param>
        /// <param name="data">data to go in the field</param>
        public void Add(object field, object data)
        {
            SQLParams.Add(field, data);
            //SQLParams.Add(field);
            //SQLParams.Add(data);
        }

        public Hashtable SqlParameters
        {
            get { return SQLParams; }
        }

        /// <summary>Clears the fields used in binding to a sql statement</summary>
        public void Clear()
        {
            SQLParams.Clear();
            rdrEOF = true;
        }

        public MessLog stmtMess
        {
            set { _sMess = value; }
        }

        public bool hasData()
        {
            return !rdrEOF;
        }

        public object ofetch(int column)
        {
            if (rdrColumn(column))
                try
                {
                    return rdr[column];
                }
                catch
                {
                    return null;
                }
            return null;
        }

        public object ofetch(string field)
        {
            int column = rdrFieldToColumn(field);
            if (column >= 0)
                try
                {
                    return rdr[column];
                }
                catch
                {
                    return null;
                }

            return null;
        }

        public string fetch(string field)
        {
            return myConvert.ToString(ofetch(field)).TrimEnd(null);
        }

        public bool bfetch(string field)
        {
            return myConvert.ToBoolean(ofetch(field));
        }

        public double dfetch(string field)
        {
            return myConvert.ToDouble(ofetch(field));
        }

        public bool tfetch(string field)
        {
            return (ifetch(field) != 0);
        }

        public int ifetch(string field)
        {
            return myConvert.ToInt32(ofetch(field));
        }

        public long lfetch(string field)
        {
            return myConvert.ToInt64(ofetch(field));
        }

        public DateTime dtfetch(string field)
        {
            return myConvert.ToDateTime(ofetch(field));
        }

        public byte[] btfetch(string field)
        {
            return myConvert.ToByteArray(ofetch(field));
        }

        public bool IsNull(string field)
        {
            return ofetch(field) is System.DBNull;
        }

        public bool nullDate(string field)
        {
            return dtfetch(field) == DateTime.MinValue;
        }

        public void cmdAbort()
        {
            // cancel any command
            // (you MUST do this when you DON'T get all rows from the 'select' otherwise
            //  you might have to wait for them to be returned anyway!)
            if (cmd != null)
            {
                try
                {
                    cmd.Cancel();
                }
                catch { }
                cmd = null;
            }
        }

        /// <summary>
        /// Close the reader handle...
        /// </summary>
        public void rdrClose()
        {
            if (rdr != null)
            {
                // cancel any command
                // (you MUST do this when you DON'T get all rows from the 'select' otherwise
                //  you might have to wait for them to be returned anyway!)
                if (cmd != null)
                {
                    try
                    {
                        cmd.Cancel();
                    }
                    catch { }
                    cmd = null;         // added this when doing some FireBird stuff, if it causes any issues put inside ifdef
                }
                if (!rdr.IsClosed)
                    try
                    {
                        rdr.Close();
                    }
                    catch { }
                rdr = null;
            }
            frdrFields.Clear();
        }

        // if it's empty, fill the List<> with
        // field names from current result set...
        private void rdrGetFieldList()
        {
            try
            {
                if (frdrFields.Count < 1)
                {
                    if (rdr != null)
                    {
                        DataTable dt = rdr.GetSchemaTable();
                        if (dt != null)
                            foreach (DataRow dr in dt.Rows)
                                frdrFields.Add(dr[0].ToString().ToLower());
                    }
                }
            }
            catch { }
        }

        // find the ordinal position in the current result set of a given field
        // uses a 'local' (private) List<> with all fieldnames in current result set...
        public int rdrFieldToColumn(string field)
        {
            rdrGetFieldList();
            return frdrFields.IndexOf(field.ToLower());
        }

        public bool rdrColumn(int c)
        {
            rdrGetFieldList();
            return (c >= 0 && c < frdrFields.Count);
        }

        public string[] rdrFields()
        {
            rdrGetFieldList();
            return frdrFields.ToArray();
        }

        /// <summary>
        /// return a string array of the data types from the select
        /// </summary>
        /// <returns></returns>
        public string[] rdrTypes()
        {
            List<string> rc = new List<string>();
            try
            {
                if (rdr != null)
                {
                    DataTable dt = rdr.GetSchemaTable();
                    if (dt != null)
                    {
                        foreach (DataRow dr in dt.Rows)
                        {
                            rc.Add(dr.fetch("DataType"));
                        }
                    }
                }
            }
            catch { }
            return rc.ToArray();
        }

        public string[][] rdrGetStructure()
        {
            string[][] rc = new string[0][];
            try
            {
                if (rdr != null)
                    rc = myConvert.DataTableToStringArray(rdr.GetSchemaTable());
            }
            catch { }
            return rc;
        }

        /// <summary>Closes all open stuff!  MUST be called when done with SqlStmt</summary>
        public void Close(bool Remove)
        {
            // any dataset
            rdrClose();

            // any command
            if (cmd != null)
            {
                cmd.Dispose();
                cmd = null;
            }

            if (Remove)
                fParent.RemoveStmt(this);

            if (createdSqlConn)
                fParent.Close();
        }

        public void Close()
        {
            Close(true);
        }

        public SqlConn Conn
        {
            get { return fParent; }
        }

        // The following functions are for legacy programs
        //   now you should create an SqlConn class and add
        //   then the SqlStmt's passing in the SqlConn...

        /// <summary>
        /// Create the stmt handle, passing in the config name of the connection string
        /// </summary>
        /// <param name="configString"></param>
        public SqlStmt(string configString)
        {
            fParent = new SqlConn(configString);
            fParent.AddStmt(this);
            
            if (_sMess != null)
                fParent.Mess = _sMess;

            createdSqlConn = true;  // force the parent (the connection) to be closed when we close...
        }

        public SqlStmt()
        {
            fParent = new SqlConn(string.Empty);
            fParent.AddStmt(this);

            createdSqlConn = true;  // force the parent (the connection) to be closed when we close...
        }

        // if they use this one
        public SqlStmt(myDbConnection connDb)
        {
            fParent = new SqlConn(connDb);
            fParent.AddStmt(this);
        }

        public myDbConnection dbConnection
        {
            get { return fParent.dbConnection; }
        }

        public bool dbConnect(string configString)
        {
            if (createdSqlConn)
                return fParent.dbConnect(configString);
            else
                return false;
        }

        public bool dbConnect()
        {
            return dbConnect(string.Empty);
        }

        public string ConnectionData
        {
            get { return fParent.ConnectionData; }
            set { fParent.ConnectionData = value; }
        }

        public MessLog Mess
        {
            get { return fParent.Mess; }
            set { fParent.Mess = value; }
        }

        public int cmdTimeOut
        {
            get { return fParent.defTimeOut; }
            set { fParent.defTimeOut = value; }
        }

        // fills the ArrayList keys with any and all @xxxx data found in the string select
        // set to lowercase and return a new string with all the @xxxx's changed to ?'s...
        private static string ParseKeys(string select, ArrayList keys)
        {
            keys.Clear();
            int p = -1;
            string ky = string.Empty;
            while (true)
            {
                try
                {
                    p = select.IndexOf("@", p + 1);
                    if (p >= 0)
                    {
                        int p2 = p + 1;  // skip the @
                        // only allows 0 thru 9, a thru z and _
                        while (p2 < select.Length)
                        {
                            if (select[p2] < 'A')
                                if (select[p2] < '0' || select[p2] > '9')
                                    break;
                            if (select[p2] > 'z')
                                break;
                            if (select[p2] != '_')
                                if (select[p2] > 'Z' && select[p2] < 'a')
                                    break;
                            p2++;
                        }
                        ky = select.Substring(p, p2 - p);
                        keys.Add(ky);
                    }
                    else
                        break;
                }
                catch
                {
                    break;
                }
            }

            // replace all the @xxx's with ?'s
            // do it in order, from the longest @xxxx to the shortest
            //    otherwise, if you do @cust before @custcode,
            //    the @custcode with end up ?code and fail...
            string nselect = select;
            try
            {
                // first, make a copy of the list of keys...
                ArrayList lkeys = new ArrayList(keys);
                // loop until the copy is empty
                while (lkeys.Count > 0)
                {
                    // find the longest string in the list...
                    int d = 0;
                    int l = 0;
                    for (int i = 0; i < lkeys.Count; i++)
                    {
                        int c = lkeys[i].ToString().Length;
                        if (c > l)
                        {
                            l = c;
                            d = i;
                        }
                    }

                    // replace that string in the select string,
                    // and then delete it from the list
                    nselect = nselect.Replace(lkeys[d].ToString(), "?");
                    lkeys.RemoveAt(d);
                }
            }
            catch { }

            // we need this list to be all lowercase, so do that now...
            for (int i = 0; i < keys.Count; i++)
                keys[i] = keys[i].ToString().ToLower();

            return nselect;
        }

        public static int CountChars(string ch, string s)
        {
            return s.Length - s.Replace(ch, string.Empty).Length;
        }
    }

    public static class DataRowExt
    {
        public static bool ColumnExists(this DataRow dr, string field)
        {
            bool rc = false;
            if (dr.Table != null)
                rc = dr.Table.Columns.Contains(field);
            return rc;
        }

        public static object ofetch(this DataRow dr, string field)
        {
            if (dr.ColumnExists(field))
                if (!(dr[field] is System.DBNull))
                    return dr[field];
            return null;
        }

        public static string fetch(this DataRow dr, string field)
        {
            return myConvert.ToString(dr.ofetch(field)).TrimEnd(null);
        }

        public static string sfetch(this DataRow dr, string field)
        {
            return myConvert.ToString(dr.ofetch(field)).TrimEnd(null);
        }

        public static bool bfetch(this DataRow dr, string field)
        {
            return myConvert.ToBoolean(dr.ofetch(field));
        }

        public static double dfetch(this DataRow dr, string field)
        {
            return myConvert.ToDouble(dr.ofetch(field));
        }

        public static int ifetch(this DataRow dr, string field)
        {
            return myConvert.ToInt32(dr.ofetch(field));
        }

        /// <summary>
        /// get int from datarow
        /// </summary>
        /// <param name="dn">column name</param>
        /// <param name="d">out int</param>
        /// <returns>true if there was data and in valid integer format</returns>
        public static bool ifetch(this DataRow dr, string dn, out int i)
        {
            i = 0;
            bool rc = false;
            try
            {
                object o = dr.ofetch(dn);
                if (o != null)
                {
                    i = Convert.ToInt32(o);
                    rc = true;
                }
            }
            catch
            {
                rc = false;
            }
            return rc;
        }


        public static long lfetch(this DataRow dr, string field)
        {
            return myConvert.ToInt64(dr.ofetch(field));
        }

        public static DateTime dtfetch(this DataRow dr, string field)
        {
            return myConvert.ToDateTime(dr.ofetch(field));
        }
    }

    public class myDataRow
    {
        private DataRow dr;

        public myDataRow()
        {
        }

        public myDataRow(DataRow pdr)
        {
            dr = pdr;
        }

        public DataRow DataRow
        {
            get { return dr; }
            set { dr = value; }
        }

        //public myDataRow(System.Data.DataRowBuilder db)
        //    : base(db)
        //{
        //}

        public bool ColumnExists(string field)
        {
            bool rc = false;
            if (dr != null)
                if (dr.Table != null)
                    rc = dr.Table.Columns.Contains(field);
            return rc;
        }

        public bool ColumnExists(int col)
        {
            bool rc = false;
            if (col >= 0)
                if (dr != null)
                    if (dr.Table != null)
                        rc = (dr.Table.Columns.Count > col);
            return rc;
        }

        /// <summary>
        /// base function used to 'safely' get from a datarow
        /// passing a field name
        /// </summary>
        /// <param name="dn">field name</param>
        /// <returns></returns>
        public object ofetch(string field)
        {
            if (ColumnExists(field))
                if (!(dr[field] is System.DBNull))
                    return dr[field];
            return null;
        }

        /// <summary>
        /// base function used to 'safely' get from a datarow
        /// passing a column number
        /// </summary>
        /// <param name="dc">column number</param>
        /// <returns></returns>
        public object ofetch(int dc)
        {
            if (ColumnExists(dc))
                return dr[dc];
            return null;
        }

        public string fetch(int dc)
        {
            return myConvert.ToString(ofetch(dc)).TrimEnd(null);
        }

        public string fetch(string dn)
        {
            return myConvert.ToString(ofetch(dn)).TrimEnd(null);
        }

        public string sfetch(string dn)
        {
            return fetch(dn);
        }

        public bool bfetch(string dn)
        {
            return myConvert.ToBoolean(ofetch(dn));
        }

        public double dfetch(string dn)
        {
            return myConvert.ToDouble(ofetch(dn));
        }

        /// <summary>
        /// get double from datarow
        /// </summary>
        /// <param name="dn">column name</param>
        /// <param name="d">out double</param>
        /// <returns>true if there was data and in valid double format</returns>
        public bool dfetch(string dn, out double d)
        {
            d = 0.0;
            bool rc = false;
            try
            {
                object o = ofetch(dn);
                if (o != null)
                    if (o is double)
                    {
                        d = (double)o;
                        rc = true;
                    }
            }
            catch
            {
                rc = false;
            }
            return rc;
        }

        public int ifetch(int dc)
        {
            return myConvert.ToInt32(ofetch(dc));
        }

        public int ifetch(string dn)
        {
            return myConvert.ToInt32(ofetch(dn));
        }

        /// <summary>
        /// get int from datarow
        /// </summary>
        /// <param name="dn">column name</param>
        /// <param name="d">out int</param>
        /// <returns>true if there was data and in valid integer format</returns>
        public bool ifetch(string dn, out int i)
        {
            i = 0;
            bool rc = false;
            try
            {
                object o = ofetch(dn);
                if (o != null)
                {
                    i = Convert.ToInt32(o);
                    rc = true;
                }
            }
            catch
            {
                rc = false;
            }
            return rc;
        }

        public static string RemoveChars(string s, string chars)
        {
            string rc = s;
            for (int i = 0; i < chars.Length; i++)
                rc = rc.Replace(chars.Substring(i, 1), string.Empty);
            return rc;
        }

        public static int CountChars(string ch, string s)
        {
            return s.Length - s.Replace(ch, string.Empty).Length;
        }

        /// <summary>
        /// try to make the date format string in fmt, match the data in s...
        ///  (won't change the data, just the format)
        /// </summary>
        /// <param name="s">Data string</param>
        /// <param name="fmt">format to use when parsing</param>
        private string FormatCheck(string s, string pfmt)
        {
            string fmt = pfmt;
            try
            {
                // make sure we account for either 24hour clock or AM/PM
                bool AMPM = s.Contains("AM") || s.Contains("PM");
                if (fmt.Contains("tt"))
                {
                    if (!AMPM)
                    {
                        string ch = " tt";
                        if (!fmt.Contains(ch))
                            ch = "tt";
                        fmt = fmt.Replace(ch, string.Empty).Replace("h:", "H:");    // capitol H for 24 hour time, small h for 12
                    }
                }
                else
                    //  Note: this ONLY works if the AM/PM is the last thing in the data string...
                    if (AMPM)
                    {
                        fmt += " tt";
                        fmt = fmt.Replace("H:", "h:");                  // capitol H for 24 hour time, small h for 12
                    }

                // if the data has seconds and the format doesn't (or vise versa)
                //   Note: this expects time to be at the end of the string and that
                //         AMPM (if present) is separated by a space from the time.
                int fl = CountChars(":", fmt);
                int l = CountChars(":", s);
                if (fl != l)
                {
                    int li = fmt.LastIndexOf(":");
                    int ls = fmt.LastIndexOf(" ");
                    if (li > ls)
                        ls = fmt.Length;

                    if (fl > l)
                    {
                        string ch = ":ss";
                        if (!fmt.Contains(ch))
                            ch = ":s";
                        fmt = fmt.Replace(ch, string.Empty);  // remove the ':s'
                    }
                    else
                        fmt = fmt.Substring(0, ls) + ":s" + fmt.Substring(ls);  // add in the ':s'
                }
            }
            catch { }

            return fmt;
        }

        public DateTime dtfetch(string dn)
        {
            return myConvert.ToDateTime(ofetch(dn));
        }


        /// <summary>
        ///  get DateTime value into dt from DataTable passing in the expected format
        /// </summary>
        /// <param name="dn">Field Name</param>
        /// <param name="fmt">Format the Date string will be in</param>
        /// <param name="dt">out parameter: date value</param>
        /// <returns></returns>
        public bool dtfetch(string dn, string fmt, out DateTime dt)
        {
            dt = DateTime.MinValue;
            bool rc = false;
            try
            {
                string s = sfetch(dn);
                fmt = FormatCheck(s, fmt);
                dt = myConvert.StrToDate(s, fmt.TrimEnd(null));
                rc = true;
            }
            catch
            {
                rc = false;
            }
            return rc;
        }

        #region NOT_USED
        private List<string> fdrFields = new List<string>();
        private void drGetFieldList()
        {
            try
            {
                if (fdrFields.Count < 1)
                {
                    if (dr != null)
                    {
                        DataTable dt = dr.Table;
                        if (dt != null)
                        {
                            foreach (DataColumn dc in dt.Columns)
                                fdrFields.Add(dc.ColumnName.ToLower());
                        }
                    }
                }
            }
            catch { }
        }

        // find the ordinal position in the current DataRow of a given field
        // uses a 'local' (private) List<> with all columnNames in that DataRow...
        public int drFieldToColumn(string field)
        {
            drGetFieldList();
            return fdrFields.IndexOf(field.ToLower());
        }

        public bool drColumn(int c)
        {
            drGetFieldList();
            return (c >= 0 && c < fdrFields.Count);
        }
        #endregion
    }

    /// <summary>
    /// Class for data conversion
    /// WITHOUT exceptions and as fast as possible...
    ///  (I put it here as these are called extensively from the data connection classes above)
    /// </summary>
    public static class myConvert
    {
        public static string ToString(object o)
        {
            return ToString(o, string.Empty);
        }

        public static string ToString(object o, string def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                    return Convert.ToString(o);
            }
            catch
            {
                return def;
            }
        }

        public static bool ToBoolean(object o)
        {
            return ToBoolean(o, false);
        }

        public static bool ToBoolean(object o, bool def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    if (o is string || o is char)
                    {
                        bool b;
                        if (!bool.TryParse((o is char) ? ((char)o).ToString() : (string)o, out b))
                            b = def;
                        return b;
                    }
                    else
                        if (o is DateTime)
                            return def;
                        else
                            return Convert.ToBoolean(o);
                }
            }
            catch
            {
                return def;
            }
        }

        public static double ToDouble(object o)
        {
            return ToDouble(o, 0.0);
        }

        public static double ToDouble(object o, double def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    if (o is string || o is char)
                    {
                        double d;
                        if (!double.TryParse((o is char) ? ((char)o).ToString() : (string)o, out d))
                            d = def;
                        return d;
                    }
                    else
                        if (o is DateTime)
                            return ((DateTime)o).Ticks / 10000;     // milliseconds since DateTime.MinValue
                        else
                            return Convert.ToDouble(o);
                }
            }
            catch
            {
                return def;
            }
        }

        public static int ToInt(object o)
        {
            return ToInt32(o, 0);
        }

        public static int ToInt(object o, int def)
        {
            return ToInt32(o, def);
        }

        public static int ToInt32(object o)
        {
            return ToInt32(o, 0);
        }

        public static int ToInt32(object o, int def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    // the main time delay is when an Exception occurs so,
                    // by handling strings this way, non numeric data will not cause one...
                    if (o is string || o is char)
                    {
                        int i;
                        if (!int.TryParse((o is char) ? ((char)o).ToString() : (string)o, out i))
                            i = def;
                        return i;
                    }
                    else
                        if (o is bool)
                            return ((bool)o) ? 1 : 0;
                        else
                            // again, anything to avoid an exception,
                            // if someone asks for the int value of a DateTime
                            // what do they expect to get?
                            if (o is DateTime)
                                return (int)(((DateTime)o).Ticks / 600000000);      // minutes since DateTime.MinValue
                            else
                            {
                                // try to stop numbers too big or too small from causing Exceptions...
                                if (o is double)
                                    if ((double)o >= Int32.MaxValue)
                                        return Int32.MaxValue;
                                    else
                                        if ((double)o <= Int32.MinValue)
                                            return Int32.MinValue;
                                return Convert.ToInt32(o);
                            }
                }
            }
            catch
            {
                return def;
            }
        }

        // these 2 funcs are not really doing what you might think, but they will give a value
        // based on the first 3 chars of the string...
        public static Int64 AsciiToInt(object o)
        {
            return AsciiToInt(o, 0);
        }

        public static Int64 AsciiToInt(object o, int def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    if (o is string || o is char)
                    {
                        string val = (o is char) ? ((char)o).ToString() : (string)o;
                        Int64 i = 0;
                        int j = 0;
                        foreach (char c in val) 
                        {
                            i *= 256;
                            i += c;
                            if (++j > 2)
                                break;
                        }
                        for (; j < 3; j++)
                            i *= 256;

                        return i;
                    }
                    else
                        return def;
                }
            }
            catch
            {
                return def;
            }
        }

        public static Int64 ToInt64(object o)
        {
            return ToInt64(o, 0);
        }

        public static Int64 ToInt64(object o, Int64 def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    // the main time delay is when an Exception occurs so,
                    // by handling strings this way, non numeric data will not cause one...
                    if (o is string || o is char)
                    {
                        Int64 i;
                        if (!Int64.TryParse((o is char) ? ((char)o).ToString() : (string)o, out i))
                            i = def;
                        return i;
                    }
                    else
                        if (o is bool)
                            return ((bool)o) ? 1 : 0;
                        else
                            // again, anything to avoid an exception,
                            // if someone asks for the int value of a DateTime
                            // what do they expect to get?
                            if (o is DateTime)
                                return (Int64)(((DateTime)o).Ticks / 600000000);      // minutes since DateTime.MinValue
                            else
                            {
                                // try to stop numbers too big or too small from causing Exceptions...
                                if (o is double)
                                    if ((double)o >= Int64.MaxValue)
                                        return Int64.MaxValue;
                                    else
                                        if ((double)o <= Int64.MinValue)
                                            return Int64.MinValue;
                                return Convert.ToInt64(o);
                            }
                }
            }
            catch
            {
                return def;
            }
        }

        public static DateTime ToDateTime(object o)
        {
            return ToDateTime(o, DateTime.MinValue, string.Empty);
        }

        public static DateTime ToDateTime(object o, DateTime def)
        {
            return ToDateTime(o, def, string.Empty);
        }

        // new CultureInfo("en-US").DateTimeFormat
        public static DateTime ToDateTime(object o, DateTime def, string fmt)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    if (o is string)
                    {
                        DateTime dt;
                        if (!string.IsNullOrEmpty(fmt))
                        {
                            if (!DateTime.TryParseExact((string)o, fmt, null, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out dt))
                                dt = def;
                        }
                        else
                            if (!DateTime.TryParse((string)o, out dt))
                                dt = def;
                        return dt;
                    }
                    else
                        if (o is double)
                            return DateTime.MinValue.AddMilliseconds((double)o);
                        else
                            if (o is int)
                                return DateTime.MinValue.AddMinutes((double)((int)o));
                            else
                                if (o is DateTime)
                                    return (DateTime)o;
                                else
                                    return def;
                }
            }
            catch
            {
                return def;
            }
        }

        public static DateTime ToTime(string time, DateTime date)
        {
            string fmt = "HHmm";
            if (time.Contains(":"))
            {
                int c = time.Length;
                time = time.Replace(":", "");
                if ((c - time.Length) > 1)  // if there was more than 1 colon, ass-u-me seconds included...
                    fmt += "ss";
            }

            int l = fmt.Length;
            if (time.Length > l)
                time = time.Substring(time.Length - l);
            else
                time = time.PadLeft(fmt.Length, '0');

            DateTime rc = ToDateTime(time, DateTime.MinValue, fmt);
            //if (rc != DateTime.MinValue)      if time passed in is "0", rc WILL BE MinValue, but we should still add it to date.Date
            rc = date.Date.Add(rc.TimeOfDay);
            return rc;
        }

        public static byte[] ToByteArray(object o)
        {
            try
            {
                if (o != null && !(o is DBNull))
                    return (byte[])o;
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }

        public static int StrToIntDef(string s, int def)
        {
            return ToInt32(s, def);
        }

        public static double StrToFloatDef(string s, double def)
        {
            return ToDouble(s, def);
        }

        public static DateTime StrToDate(string s)
        {
            return ToDateTime(s, DateTime.MinValue, string.Empty);
        }

        public static DateTime StrToDate(string s, string fmt)
        {
            return ToDateTime(s, DateTime.MinValue, fmt);
        }

        public static string ByteArrayToString(byte[] abData)
        {
            StringBuilder psBuilder = new StringBuilder(abData.Length * 2);

            for (int i = 0; i < abData.Length; i++)
                psBuilder.AppendFormat("{0:X2}", abData[i]);

            return psBuilder.ToString();
        }

        public static string[][] DataTableToStringArray(DataTable dt)
        {
            List<string[]> rc = new List<string[]>();
            if (dt != null)
            {
                // get the headings first...
                List<string> h = new List<string>();
                foreach (DataColumn myProperty in dt.Columns)
                    h.Add(myProperty.ColumnName);
                rc.Add(h.ToArray());

                List<string> r = new List<string>();
                foreach (DataRow dr in dt.Rows)
                {
                    r.Clear();
                    foreach (string s in h)
                        try
                        {
                            if (dr[s] != null)
                                try
                                {
                                    r.Add(dr[s].ToString());
                                }
                                catch (Exception e)
                                {
                                    r.Add(e.Message);
                                }
                            else
                                r.Add("");
                        }
                        catch { }
                    rc.Add(r.ToArray());
                }
            }
            return rc.ToArray();
        }
    }
}