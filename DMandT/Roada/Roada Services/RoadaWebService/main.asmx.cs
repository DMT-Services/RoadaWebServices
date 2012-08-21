using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services;
using System.Messaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Xml.Serialization;
using System.Collections;
using System.Reflection;
using DMTServices;
using System.Diagnostics;

namespace RoadaWebService
{
    /// <summary>
    /// Summary description for Service1
    /// </summary>
    [WebService(Namespace = "http://www.roada.ca/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    // [System.Web.Script.Services.ScriptService]
    public class ROADAService : System.Web.Services.WebService
    {
        private System.Object lockThis = new System.Object();
        public const int MAX_RECORDS = 500;
        private List<MQueue> queueList = new List<MQueue>();
        const string DEFAULT_USER = "DMTServices";
        const string DEFAULT_PASSWORD = "1Secr3t2";
        const string RECVD_PATH = @"Recvd\";
        const string OBJECTS_PATH = @"objects\";

        private void InitializeComponent()
        {

        }

        private string[] ReadQueueList(bool forceRefresh)
        {
            List<string> custCodes = new List<string>();
            if (queueList.Count == 0 || forceRefresh)
            {
                SqlConn mConn = new SqlConn("MainConn");
                try
                {
                    queueList.Clear();
                    mConn.Mess = Global.messLog;
                    if (mConn.dbConnect())
                    {
                        SqlStmt stmt = new SqlStmt(mConn);
                        try
                        {
                            bool rc = stmt.DoSQL("SELECT * FROM customer");     // main database customer that points to all customers
                            while (rc)
                            {
                                string user = stmt.fetch("wserv_user");
                                if (user != "")
                                {
                                    MQueue q = new MQueue();
                                    q.CustCode = stmt.fetch("cust_code");
                                    custCodes.Add(q.CustCode);
                                    q.ConnectionString = stmt.fetch("connectionStrings");
                                    q.UserName = user;
                                    q.Password = stmt.fetch("wserv_password");
                                    q.QName = string.Format(@".\private$\{0}", q.CustCode);
                                    bool qok = true;
                                    if (! MessageQueue.Exists(q.QName))
                                    {
                                        try
                                        {   
                                            MessageQueue.Create(q.QName, true);
                                        }
                                        catch (Exception ex)
                                        {
                                            Global.messLog.Flush(String.Format("MessageQueue Create Exception: {0}", ex.Message));
                                            qok = false;
                                        }
                                    }
                                    // make sure the queue was created ok
                                    if(qok)
                                    {
                                        q.MyMessage = new System.Messaging.Message(q.QName);
                                        q.MessageTx = new MessageQueueTransaction();
                                        q.MessageQ = new MessageQueue(q.QName);
                                        queueList.Add(q);
                                    }
                                }
                                rc = stmt.dbSkip();
                            }
                            if (queueList.Count == 0)
                                Global.messLog.Flush("No customer records found with web service privileges");
                            else
                                Global.messLog.Flush("Customer Queue List Refreshed");
                        }
                        finally
                        {
                            stmt.Close();                   // Always force closing.
                        }
                    }
                    else
                        Global.messLog.Flush("ReadQueueList: Error connecting to main database!");
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush(string.Format("Excp ReadQueueList: {0}", ex.Message));
                }
                finally
                {
                    if (mConn != null)
                        mConn.Close();
                }
            }
            return custCodes.ToArray();
        }

        private Response GetResponse(int err)
        {
            Response r = new Response();
            r.ErrCode = err;
            r.Success = err == 0;
            switch (err)
            {
                case -1:
                    r.ErrMessage = "Invalid request object";
                    break;
                case -2:
                    r.ErrMessage = "Failed adding to MSMQ";
                    break;
                case -3:
                    r.ErrMessage = "Required field is null";
                    break;
                case -4:
                    r.ErrMessage = "Message queue problem";
                    break;
                case -5:
                    r.ErrMessage = "Maximum record count exceeded";
                    break;
                     
            }

            return r;
        }

        private MQueue GetQueue(Request request, bool checkBin, bool ignoreAuth)
        {
            MQueue Result = null;
            ReadQueueList(false);      // always must check if we have read the connection list
            if(request.CustCode.Length == 4)
            {
                foreach (MQueue q in queueList)
                {
                    if (request.CustCode == q.CustCode)
                    {
                        bool ok = true;
                        if(! ignoreAuth)
                        {
                            ok = false;
                            if(request.UserName == q.UserName && request.Password == q.Password)
                                ok = ! checkBin || request.BinPassword == "1dmtserv2";
                        }
                        if(ok)
                        {
                            Result = q;
                            break;
                        }
                    }
                }
            }

            return Result;
        }

        private bool AddToMSMQueue(MQueue q, TCPPacketBuffer b)
        {
            bool Result = true;

            lock (lockThis)
            {
                q.MyMessage.Body = b;
                try
                {
                    q.MessageTx.Begin();
                    q.MessageQ.Send(q.MyMessage, q.MessageTx);
                    q.MessageTx.Commit();
                }
                catch (Exception ex)
                {
                    q.MessageTx.Abort();
                    Result = false;
                    Global.messLog.Flush(string.Format("Exception in AddToMSMQueue Cust {0} {1}", q.CustCode, ex.Message));
                }
            }
            /*
            AddCount++;
            if (Result)
            {
                if (AddCount > 2000)
                {
                    TotCount += AddCount;
                    AddCount = 0;
                    Global.messLog.Flush(string.Format("Queued {0} for customer {1}, waiting...", TotCount, custCodes[mNdx]));
                    while (!IsQueueNearEmpty(MessageQ[mNdx], 5))
                        System.Threading.Thread.Sleep(50);
                    Global.messLog.Flush(string.Format("Queueing Resumed", custCodes[mNdx]));
                }
            }
            */

            return Result;
        }


        [WebMethod(Description = "Refreshes valid memory queue list.  Internal use only.")]
        public Response RefreshQueueList(Request request, out string[] custCodes)
        {
            custCodes = null;
            Response Result;
            if (request.UserName == "randy@dmtserv.com" && request.BinPassword == "1secret2")
            {
                custCodes = ReadQueueList(true);
                Result = GetResponse(0);
            }
            else
                Result = GetResponse(-1);

            return Result;
        }

        [WebMethod(Description = "Add binary spreader and GPS data records")]
        public Response AddTCPPacket(Request request, TCPPacketBuffer[] buffer)
        {
            Response Result;
            MQueue q = GetQueue(request, true, false);
            if (q == null)
            {
                Result = GetResponse(-1);
            }
            else
            {
                Result = GetResponse(0);
                foreach (TCPPacketBuffer b in buffer)
                {
                    if (! AddToMSMQueue(q, b))
                    {
                        Result = GetResponse(-2);
                        break;
                    }
                }
            }
            return Result;
        }

        private byte TempC(double temp)
        {
            sbyte sb = (sbyte)((temp / 5.0) * 10);
            return (byte)sb;
        }

        private TCPPacketBuffer MakeGPSReading(GPSData gpsData)
        {
            TCPPacketBuffer b = new TCPPacketBuffer();
            b.lastDate = DateTime.UtcNow;
            b.DevId = gpsData.DevId;
            b.Vers = 3;

            b.Data[0] = (byte)'C';        // multi-message command
            b.Data[1] = 0;
            b.Data[2] = 0;
            b.Data[3] = 0;
            b.Data[4] = 0;

            int p = 6;
            b.Data[p++] = (byte)'G';    // GPS cmd
            b.Data[p++] = 3;        // vers

            byte cnt = 1;        // 1 gps reading
            // temperature data?
            if(gpsData.TempValid)
                cnt |= 0x80;
            if (gpsData.MetersTravelled != 0)
                cnt |= 0x20;
            b.Data[p++] = cnt;        // count and data flags

            b.Data[p++] = (byte)(gpsData.UTCDate.Year - 2000);
            b.Data[p++] = (byte)gpsData.UTCDate.Month;
            b.Data[p++] = (byte)gpsData.UTCDate.Day;

            b.Data[p++] = (byte)gpsData.UTCDate.Hour;
            b.Data[p++] = (byte)gpsData.UTCDate.Minute;
            b.Data[p++] = (byte)gpsData.UTCDate.Second;

            Flatten((Int32)(gpsData.Latitude * 10000000), b.Data, ref p);
            Flatten((Int32)(gpsData.Longitude * 10000000), b.Data, ref p);

            Flatten((Int16)gpsData.SOG, b.Data, ref p);

            short cog = (short)gpsData.COG;
            // optionally deal with inputs
            if (gpsData.PlowInp)
                cog |= 0x400;
            if (gpsData.WingInp)
                cog |= 0x800;
            if (gpsData.TowInp)
                cog |= 0x1000;
            // creates moving event
            if (gpsData.Moving)
                cog |= 0x2000;
            // creates stopped event
            if (gpsData.Stopped)
                cog |= 0x4000;

            // check for odometer
            if ((cnt & 0x20) != 0)
                Flatten((Int16)gpsData.MetersTravelled, b.Data, ref p);

            // temperature readings?
            if ((cnt & 0x80) != 0)
            {
                b.Data[p++] = TempC(gpsData.TempAir);
                b.Data[p++] = TempC(gpsData.TempSurface);
            }
            // COG
            Flatten(cog, b.Data, ref p);

            b.DataLength = p;
            b.Data[5] = (byte)(p - 5);      // individual length
            
            return b;
        }

        [WebMethod(Description = "Add GPS data records")]
        public Response AddGPSData(Request request, GPSData[] gpsData)
        {
            Response Result = new Response();
            MQueue q = GetQueue(request, false, false);
            if (q == null)
                Result = GetResponse(-1);
            else
            {
                if (gpsData.Length > MAX_RECORDS)
                    Result = GetResponse(-5);
                else
                {
                    Result = GetResponse(0);
                    foreach (GPSData g in gpsData)
                    {
                        TCPPacketBuffer b = null;
                        try
                        {
                            b = MakeGPSReading(g);
                        }
                        catch (Exception ex)
                        {
                            Global.messLog.Flush(string.Format("MakeGPSReading Excp: {0}", ex.Message));
                            Result = GetResponse(-3);
                        }

                        if (!AddToMSMQueue(q, b))
                        {
                            Result = GetResponse(-2);
                            break;
                        }
                    }
                }
            }

            return Result;
        }

        private void FixByteLength(byte[] src, byte[] b, int len, ref int p)
        {
            if (src != null)
            {
                if (len <= src.Length)
                    Array.Copy(src, 0, b, p, len);
                else
                {
                    byte[] tmp = new byte[len];
                    Array.Copy(src, tmp, src.Length);
                    for (int i = src.Length; i < len; i++)
                        tmp[i] = 0;
                    Array.Copy(tmp, 0, b, p, len);
                }
            }
            p += len;
        }

        void Flatten(Int32 i, byte[] b, ref int p)
        {
            int len = Marshal.SizeOf(i);
            Array.Copy(BitConverter.GetBytes(i), 0, b, p, len);
            p += len;
        }

        void Flatten(Int16 i, byte[] b, ref int p)
        {
            int len = Marshal.SizeOf(i);
            Array.Copy(BitConverter.GetBytes(i), 0, b, p, len);
            p += len;
        }

        void Flatten(UInt16 i, byte[] b, ref int p)
        {
            int len = Marshal.SizeOf(i);
            Array.Copy(BitConverter.GetBytes(i), 0, b, p, len);
            p += len;
        }

        private TCPPacketBuffer MakeACE(ACEData ace)
        {
            TCPPacketBuffer b = new TCPPacketBuffer();
            b.lastDate = DateTime.UtcNow;
            b.DevId = ace.DevId;
            b.Vers = 3;

            b.Data[0] = (byte)'C';        // multi-message command
            b.Data[1] = 0;
            b.Data[2] = 0;
            b.Data[3] = 0;
            b.Data[4] = 0;

            int p = 6;
            b.Data[p++] = (byte)'D';    // ACE data cmd
            b.Data[p++] = 3;        // vers

            // UTC Date of event
            b.Data[p++] = (byte)(ace.UTCDate.Year - 2000);
            b.Data[p++] = (byte)ace.UTCDate.Month;
            b.Data[p++] = (byte)ace.UTCDate.Day;

            b.Data[p++] = (byte)ace.UTCDate.Hour;
            b.Data[p++] = (byte)ace.UTCDate.Minute;
            b.Data[p++] = (byte)ace.UTCDate.Second;

            Flatten((Int32)(ace.Latitude * 10000000), b.Data, ref p);
            Flatten((Int32)(ace.Longitude * 10000000), b.Data, ref p);

            // course over ground
            b.Data[p++] = (byte)(ace.COG / 3);

            // speed over ground in m/s X 100
            b.Data[p++] = (byte)ace.SOG;

            // plow inputs
            byte plow = 0;
            if (ace.PlowInp)
                plow |= 1;
            if (ace.WingInp)
                plow |= 2;
            if (ace.TowInp)
                plow |= 4;
            b.Data[p++] = plow;

            // our temperature, ignored
            b.Data[p++] = 0;
            b.Data[p++] = 0;

            // controller id
            FixByteLength(ace.ContID, b.Data, 6, ref p);
            // operator id
            FixByteLength(ace.OpID, b.Data, 8, ref p);
            // Rates
            Flatten((UInt16)ace.SpreadRate, b.Data, ref p);
            Flatten((UInt16)ace.BlastRate, b.Data, ref p);
            Flatten((UInt16)ace.PrewetRate, b.Data, ref p);
            Flatten((UInt16)ace.SpinnerWidth, b.Data, ref p);
            
            // material type
            b.Data[p++] = ace.MatType;

            // breakdown bit etc..
            b.Data[p++] = ace.EventType;
            b.Data[p++] = ace.ErrorBits;
            Flatten((UInt16)ace.VehSpeed, b.Data, ref p);

            // ace temperatures
            Flatten((Int16)ace.TempSurface, b.Data, ref p);
            Flatten((Int16)ace.TempAir, b.Data, ref p);
            // inputs
            Flatten((UInt16)ace.Inputs, b.Data, ref p);

            // Sys State
            b.Data[p++] = (byte)ace.SysState;

            // Distances
            Flatten((Int32)ace.TripDist, b.Data, ref p);
            Flatten((Int32)ace.TripSolid, b.Data, ref p);
            Flatten((Int32)ace.TripLiquid, b.Data, ref p);
            Flatten((Int32)ace.ServicedDist, b.Data, ref p);

            b.DataLength = p;
            b.Data[5] = (byte)(p - 5);      // individual length

            return b;
        }

        [WebMethod(Description = "Add ACE spreader data records")]
        public Response AddACEData(Request request, ACEData[] aceData)
        {
            Response Result = new Response();
            MQueue q = GetQueue(request, false, false);
            if (q == null)
                Result = GetResponse(-1);
            else
            {
                if (aceData.Length > MAX_RECORDS)
                    Result = GetResponse(-5);
                else
                {
                    Result = GetResponse(0);
                    foreach (ACEData ace in aceData)
                    {
                        TCPPacketBuffer b = null;
                        try
                        {
                            b = MakeACE(ace);
                        }
                        catch (Exception ex)
                        {
                            Global.messLog.Flush(string.Format("MakeACE Excp: {0}", ex.Message));
                            Result = GetResponse(-3);
                        }

                        if (!AddToMSMQueue(q, b))
                        {
                            Result = GetResponse(-2);
                            break;
                        }
                    }
                }
            }

            return Result;
        }

        private bool LowSecurityCheck(Request request)
        {
            return (request.UserName == DEFAULT_USER && request.Password == DEFAULT_PASSWORD);
        }

        [WebMethod(Description = "Commission Software")]
        public CommResponse CommSoftware(Request request, string guid, string descr)
        {
            CommResponse response = new CommResponse();
            response.CommFlag = false;
            response.Mesg = "Unknown error";
            // added security!!
            if(LowSecurityCheck(request))
            {
                SqlConn mConn = new SqlConn("MainConn");
                SqlStmt stmt = null;
                try
                {
                    mConn.Mess = Global.messLog;
                    if (mConn.dbConnect())
                    {
                        stmt = new SqlStmt(mConn);
                        // main database, look for a new software activation record that
                        bool rc = stmt.DoSQL(string.Format("SELECT * FROM soft_activate WHERE pc_code = '{0}'", request.CustCode.ToUpper()));
                        if (!rc)
                        {
                            response.Mesg = string.Format("Entered customer code {0} does not exist", request.CustCode);
                        }
                        else
                        {
                            while (rc)
                            {
                                if (!stmt.tfetch("commissioned"))
                                {
                                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                                    sql.Add("commissioned", true);
                                    sql.AddNow("comm_date");
                                    sql.Add("client_guid", guid);
                                    sql.Add("description", descr);
                                    int rowId = stmt.ifetch("id");
                                    int mode = stmt.ifetch("mode");
                                    int uniqId = stmt.ifetch("unique_id");
                                    stmt.DoSQL(sql.BuildUpdate("UPDATE soft_activate", string.Format("WHERE id = {0}", rowId)));
                                    if (stmt.RowsAffected != 1)
                                        response.Mesg = "Failed updating activation database";
                                    else
                                    {
                                        response.Id = rowId;
                                        response.CommFlag = true;
                                        response.Mode = mode;
                                        response.UniqueId = uniqId;
                                        response.Mesg = "Commission success";
                                    }
                                    break;
                                }
                                rc = stmt.dbSkip();
                            }
                            if (!response.CommFlag)
                                response.Mesg = string.Format("No validation keys left for customer code {0}", request.CustCode);
                        }
                    }
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                    if (mConn != null)
                        mConn.Close();
                }
            }
            return response;
        }

        [WebMethod(Description = "Checks for any new objects")]
        public string[] GetObjects(Request request, VersionInfo[] versions)
        {
            string[] Result = new string[0];
            if(LowSecurityCheck(request))
            {
                SqlStmt stmt = new SqlStmt("mainconn");
                stmt.Mess = Global.messLog;
                List<string> objList = null;
                try
                {
                    objList = new List<string>();
                    string objPath = MessLog.ApplicationPath() + OBJECTS_PATH;
                    foreach (VersionInfo v in versions)
                    {
                        // check if we've staged this DLL or EXE
                        if (File.Exists(objPath + Path.GetFileName(v.Name)))
                        {
                            // get our version info and compare against the clients
                            FileVersionInfo verInfo = FileVersionInfo.GetVersionInfo(objPath + Path.GetFileName(v.Name));
                            Version fVer = new Version(verInfo.FileMajorPart, verInfo.FileMinorPart, verInfo.FileBuildPart, verInfo.FilePrivatePart);
                            Version oVer = new Version(v.Vers);
                            if (fVer > oVer)
                                objList.Add(v.Name);
                        }
                    }
                    if (stmt.DoSQL(string.Format("SELECT objects FROM soft_activate WHERE id = {0}", request.CustId)))
                    {
                        string tmp = stmt.fetch("objects");
                        if (tmp != "")
                        {
                            string[] objAry = tmp.Split(new char[] { '|' });
                            foreach (string o in objAry)
                                objList.Add(o);
                        }
                    }
                    Result = objList.ToArray();
                }
                finally
                {
                    stmt.Close();
                    if (objList != null)
                        objList.Clear();                    // Always force clearing (just in case of exception).
                }
            }
            return Result;
        }


        [WebMethod(Description = "Update that all objects have been received")]
        public bool SetObjects(Request request, string[] objList)
        {
            bool Result = false;
            if (LowSecurityCheck(request))
            {
                SqlStmt stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;
                SqlHelper sql = null;
                try
                {
                    sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.AddNow("objdate");
                    string objs = string.Empty;
                    foreach(string s in objList)
                    {
                        if(objs.Length > 0)
                            objs += "|";
                        objs += s;
                    }
                    sql.Add("objrecvd", objs);
                    sql.Add("objects", "");
                    stmt.DoSQL(sql.BuildUpdate("UPDATE soft_activate", string.Format("WHERE id = {0}", request.CustId)));
                    Result = true;
                }
                finally
                {
                    stmt.Close();
                    if (sql != null)
                        sql.Clear();    // Is this really necessary?
                }
            }
            return Result;
        }

        [WebMethod(Description = "Get a block of a file")]
        public byte[] GetFileBlock(Request request, string fileName, ref int block, out int count)
        {
            byte[] Result = new byte[0];
            count = 0;
            if (! LowSecurityCheck(request))
                block = -2;     // failed security check!
            else
            {
                FileStream fs = null;
                try
                {
                    string objectName = MessLog.ApplicationPath() + OBJECTS_PATH + Path.GetFileName(fileName);
                    fs = new FileStream(objectName, FileMode.Open, FileAccess.Read);
                    fs.Seek(block * 50000, SeekOrigin.Begin);
                    Result = new byte[50000];
                    count = fs.Read(Result, 0, 50000);
                    if (count < 50000)
                        block = -1;     // mark EOF
                    else
                        block++;        // next block
                    fs.Close();
                    fs = null;
                }
                catch(Exception ex)
                {
                    Global.messLog.Flush("GetFileBlock Excp: " + ex.Message);
                    block = -2;     // error reading file
                    if (fs != null)
                        fs.Close();     // In case we do not make it to the end.
                }
            }
            return Result;
        }

        [WebMethod(Description = "Set (receive) a block of a file")]
        public bool SetFileBlock(Request request, string fileName, byte[] data, int count)
        {
            bool Result = false;
            if (LowSecurityCheck(request))
            {
                string destName = MessLog.ApplicationPath() + RECVD_PATH + Path.GetFileName(fileName);
                Global.messLog.Flush("Path " + destName);
                FileStream fs = null;
                try
                {
                    fs = new FileStream(destName, FileMode.Append, FileAccess.Write);
                    fs.Write(data, 0, count);
                    fs.Close();
                    fs = null;
                    Result = true;
                }
                catch
                {
                    Global.messLog.Flush(string.Format("Failed writing block of file {0}", fileName));
                    if (fs != null)
                        fs.Close();     // In case we do not make it to the end.
                }
            }
            return Result;
        }

        [WebMethod(Description = "Rename a file sent from the client")]
        public bool RenameFile(Request request, string oldName, string newName)
        {
            bool Result = false;
            if (LowSecurityCheck(request))
            {
                try
                {
                    string pathName = MessLog.ApplicationPath() + RECVD_PATH;
                    if (File.Exists(pathName + Path.GetFileName(newName)))
                        File.Delete(pathName + Path.GetFileName(newName));

                    File.Move(pathName + Path.GetFileName(oldName), pathName + Path.GetFileName(newName));
                    Result = true;
                }
                catch
                {
                    Global.messLog.Flush(string.Format("Failed renaming {0} to {1}", oldName, newName));
                }
            }
            return Result;
        }

        [WebMethod(Description = "Get the client table info")]
        public Client[] GetClients(Request request)
        {
            Client[] Result = new Client[0];
            if (LowSecurityCheck(request))
            {
                SqlConn mConn = new SqlConn("MainConn");
                SqlStmt stmt = null;
                List<Client> clients = null;
                try
                {
                    mConn.Mess = Global.messLog;
                    if (mConn.dbConnect())
                    {
                        clients = new List<Client>();
                        stmt = new SqlStmt(mConn);
                        // made a change, get the comma separated list of db_codes from the soft_activate table
                        stmt.DoSQL(string.Format("SELECT db_codes FROM soft_activate WHERE pc_code = '{0}'", request.CustCode));
                        string[] dbCodes = stmt.fetch("db_codes").Split(new char[] { ',' });
                        foreach (string dbcode in dbCodes)
                        {
                            // look for this db code in the clients table
                            stmt.DoSQL(string.Format("SELECT * FROM clients WHERE db_code = '{0}'", dbcode));
                            Client c = new Client();
                            c.CustCode = dbcode;
                            c.Name = stmt.fetch("name");
                            c.SaveData = stmt.tfetch("savedata");
                            clients.Add(c);
                        }
                        Result = clients.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("Excp in GetClients: " + ex.Message);
                    if (clients != null)
                        clients.Clear();                    // In case we do not make it to the end.
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                    if (mConn != null)
                        mConn.Close();
                }
            }
            return Result;
        }

        private string SerializeObject(object obj, Type objType)
        {
            string Result = string.Empty;
            StringWriter writer= new StringWriter();
            try
            {
                XmlSerializer serializer = new XmlSerializer(objType);
                XmlSerializerNamespaces xs = new XmlSerializerNamespaces();
                //To remove namespace and any 
                //other inline information tag
                xs.Add("", "");
                serializer.Serialize(writer, obj, xs);
                Result = writer.ToString();
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("Error serializing object " + ex.Message);
            }
            finally
            {
                writer.Close();
            }
            return Result;
        }

        private object DeserializeObject(string xmlString, Type objType)
        {
            object obj = null;
            StringReader reader = new StringReader(xmlString);
            try
            {
                XmlSerializer xmlSerializer = new XmlSerializer(objType);
                obj = xmlSerializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                Global.messLog.Flush(string.Format("Error deserializing object {0}", ex.Message));
                obj = null;
            }
            finally
            {
                reader.Close();
            }
            return obj;
        }

        private string SerializeSQLString(string sqlStr)
        {
            string Result = sqlStr.Replace("\r", "^r");
            Result = Result.Replace("\n", "^n");
            return Result;
        }

        private string DeserializeSQLString(string sqlStr)
        {
            string Result = sqlStr.Replace("^r", "\r");
            Result = Result.Replace("^n", "\n");
            return Result;
        }


        [WebMethod(Description = "Retrieves replication data from the server")]
        public ReplicationData[] GetReplicationData(Request request, string dbCode, int startRecord)
        {
            ReplicationData[] Result = new ReplicationData[0];
            if (LowSecurityCheck(request))
            {
                SqlStmt rstmt = new SqlStmt("MainConn");
                rstmt.Mess = Global.messLog;
                try
                {
                    SqlConn cConn = new SqlConn();
                    cConn.Mess = Global.messLog;
                    bool foundConn;
                    if (dbCode == "MSTR")        // master (mainconn) database?
                    {
                        Global.messLog.Flush(string.Format("Checking GetReplicationData MSTR for {0}", request.CustCode));
                        // for the master database, lookup the pc_code in the soft_activate and get the mstr_code
                        foundConn = rstmt.DoSQL(string.Format("SELECT mstr_code FROM soft_activate WHERE pc_code = '{0}'", request.CustCode));
                        if (foundConn)
                        {
                            // now, lookup the master code in the customer tables connectionstrings column
                            string mstrCode = rstmt.fetch("mstr_code");
                            foundConn = rstmt.DoSQL(string.Format("SELECT connectionstrings FROM customer WHERE cust_code = '{0}'", mstrCode));
                            cConn.ConnectionData = rstmt.fetch("connectionstrings");
                        }
                        // this is the old way, no longer used
                        //rstmt.Conn.dbConnect();
                        //cConn.ConnectionData = rstmt.Conn.ConnectionData;
                    }
                    else
                    {
                        Global.messLog.Flush(string.Format("Checking GetReplicationData for {0}", dbCode));
                        // we only need to look up the db_code now since there's only one record per db_code in the clients table
                        foundConn = rstmt.DoSQL(string.Format("SELECT connectionstring FROM clients WHERE db_code = '{0}' and savedata = 1", dbCode));
                        cConn.ConnectionData = rstmt.fetch("connectionstring");
                    }
                    if (foundConn)
                    {
                        SqlStmt stmt = null;
                        List<ReplicationData> repList = null;
                        try
                        {
                            if (cConn.dbConnect())
                            {
                                stmt = new SqlStmt(cConn);
                                repList = new List<ReplicationData>();
                                bool rc = stmt.DoSQL(string.Format("SELECT * FROM datarep WHERE record_id > {0} and client_id != {1} ORDER BY record_id", startRecord, request.CustId));
                                while (rc)
                                {
                                    ReplicationData r = new ReplicationData();
                                    string mySql = stmt.fetch("sqlstr");
                                    // need to deserialize it
                                    mySql = DeserializeSQLString(mySql);
                                    r.SqlStr = mySql;
                                    string par = stmt.fetch("params");
                                    SQLParameter[] tmpParam = new SQLParameter[0];
                                    if (par.Trim().Length > 0)
                                        r.Params = (SQLParameter[])DeserializeObject(par, tmpParam.GetType());
                                    r.RecordId = stmt.ifetch("record_id");
                                    repList.Add(r);
                                    rc = stmt.dbSkip();
                                }
                                Result = repList.ToArray();
                                Global.messLog.Flush(string.Format("GetReplication Found {0} records Start id {1} client_id {2}", Result.Length, startRecord, request.CustId));
                            }
                        }
                        catch (Exception ex)
                        {
                            Global.messLog.Flush(string.Format("Exception GetRecplicationData {0}", ex.Message));
                            if (repList != null)
                                repList.Clear();            // In case we do not make it to the end.
                        }
                        finally
                        {
                            if (stmt != null)
                                stmt.Close();
                            cConn.Close();
                        }
                    }
                    else
                        Global.messLog.Flush(string.Format("GetReplicationData: Cannot find connectionstring for {0} - {1}", request.CustCode, dbCode));
                }
                finally
                {
                    rstmt.Close();
                }
            }

            return Result;
        }
    }
}
