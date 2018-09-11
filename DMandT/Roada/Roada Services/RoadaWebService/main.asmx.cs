using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Services;
using System.Messaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Xml;
using System.Web.Configuration;
using System.Web.Script.Services;

using DMTServices;

namespace RoadaWebService
{
    /// <summary>
    /// Roada Web Service, used for Wabasto Heaters among other things
    /// </summary>
    [WebService(Namespace = "http://www.roada.ca/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [System.Web.Script.Services.ScriptService]
    public class ROADAService : System.Web.Services.WebService
    {
        private System.Object lockThis = new System.Object();
        public const int MAX_RECORDS = 500;
        private List<MQueue> queueList = new List<MQueue>();
        private List<MQueue> custList = new List<MQueue>();
        const string DEFAULT_USER = "DMTServices";
        const string DEFAULT_PASSWORD = "1Secr3t2";
        const string RECVD_PATH = @"Recvd\";
        const string OBJECTS_PATH = @"objects\";
        const string LONG_DATE = "yyyy-MM-dd HH:mm:ss";
        const int DEFAULT_RUN_TIME = 40;
        const double VERSION = 1.3;
        private const int MaxDownloadBatchSize = 1000;

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
                                    q.QName = string.Format(@"FormatName:DIRECT=OS:roadaserv\private$\{0}", q.CustCode);

                                    /*      can't do this to remote queue
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
                                        } */

                                    // make sure the queue was created ok
                                    q.MyMessage = new System.Messaging.Message(q.QName);
                                    q.MessageTx = new MessageQueueTransaction();
                                    q.MessageQ = new MessageQueue(q.QName);
                                    queueList.Add(q);
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

        private void ReadCustomers(bool forceRefresh)
        {
            if (queueList.Count == 0 || forceRefresh)
            {
                SqlConn mConn = new SqlConn("MainConn");
                try
                {
                    custList.Clear();
                    mConn.Mess = Global.messLog;
                    if (mConn.dbConnect())
                    {
                        SqlStmt stmt = new SqlStmt(mConn);
                        try
                        {
                            bool rc = stmt.DoSQL("SELECT * FROM customer ORDER BY cust_code");     // main database customer that points to all customers
                            while (rc)
                            {
                                MQueue q = new MQueue();
                                q.CustCode = stmt.fetch("cust_code");
                                q.ConnectionString = stmt.fetch("connectionStrings");
                                q.Name = stmt.fetch("name");
                                custList.Add(q);
                                rc = stmt.dbSkip();
                            }
                        }
                        finally
                        {
                            stmt.Close();                   // Always force closing.
                        }
                    }
                    else
                        Global.messLog.Flush("ReadCustomers: Error connecting to main database!");
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush(string.Format("Excp ReadCustomers: {0}", ex.Message));

                }
                finally
                {
                    if (mConn != null)
                        mConn.Close();
                }
            }
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
                case -6:
                    r.ErrMessage = "Buffer too large";
                    break;
                case -7:
                    r.ErrMessage = "Buffer too small";
                    break;
            }
            if (err != 0)
                Global.messLog.Flush(r.ErrMessage);
            return r;
        }

        private bool CheckCredentials(Request request)
        {
            bool Result = false;
            if (request.Password == "1ac355web" && request.UserName == "webservice")
                Result = true;
            else
                Global.messLog.Flush("CheckCredentials() User {0} Pass {1} failure!", request.UserName, request.Password);
            return Result;
        }

        private MQueue GetQueue(Request request, bool checkBin, bool ignoreAuth)
        {
            MQueue Result = null;
            ReadQueueList(false);      // always must check if we have read the connection list
            if (request.CustCode.Length == 4)
            {
                foreach (MQueue q in queueList)
                {
                    if (request.CustCode == q.CustCode)
                    {
                        bool ok = true;
                        if (!ignoreAuth)
                        {
                            ok = false;
                            if (request.UserName == q.UserName && request.Password == q.Password)
                                ok = !checkBin || request.BinPassword == "1dmtserv2";
                        }
                        if (ok)
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
                ReadCustomers(true);
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
                    if (!AddToMSMQueue(q, b))
                    {
                        Result = GetResponse(-2);
                        break;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Add binary NeoTerra data records")]
        public Response AddBinaryPacket(Request request, int devId, byte[] buffer)
        {
            Response Result;
            MQueue q = GetQueue(request, false, false);
            if (q == null)
            {
                Result = GetResponse(-1);
            }
            else
            {
                TCPPacketBuffer b = new TCPPacketBuffer();
                if (buffer.Length > b.Data.Length)
                    Result = GetResponse(-6);       // buffer too long
                else
                {
                    if (buffer.Length > 2)
                    {
                        Result = GetResponse(0);
                        b.Vers = buffer[1];     // version of the record
                        b.lastDate = DateTime.UtcNow;
                        b.DevId = devId;
                        b.DataLength = buffer.Length;
                        b.Data[0] = buffer[0];  // the command
                        b.Data[1] = 0;  // not used
                        b.Data[2] = 0;
                        b.Data[3] = 0;
                        b.Data[4] = 0;
                        Array.Copy(buffer, 2, b.Data, 5, buffer.Length - 2);
                        string s = string.Empty;
                        for (int i = 0; i < 6; i++)
                            s += string.Format("{0:X2} ", b.Data[i + 5]);
                        if (!AddToMSMQueue(q, b))
                            Result = GetResponse(-2);
                        Global.messLog.Flush("Cmd {0} Ver {1} Id {2} Date {3}", b.Data[0], b.Vers, b.DevId, s);
                    }
                    else
                        Result = GetResponse(-7);
                }
            }
            return Result;
        }


        [WebMethod(Description = "Add NeoTerra GPS data records")]
        public Response AddNeoTerraGPSData(Request request, NeoTerraSystemsGatewayData data)
        {
            Response Result;
            MQueue q = GetQueue(request, false, false);
            if (q == null)
            {
                Result = GetResponse(-1);
            }
            else
            {
                Result = GetResponse(0);    // default to success
                GPSData g = new GPSData();
                g.COG = data.Direction;
                g.DevId = Convert.ToInt32(data.UnitID);
                g.Latitude = data.Lat;
                g.Longitude = data.Lon;
                g.MetersTravelled = 0;
                g.Moving = false;
                g.Stopped = false;
                g.TempAir = 0;
                g.TempSurface = 0;
                g.TempValid = false;
                g.UTCDate = data.Dt;
                g.SOG = (int)(data.Speed * 51.44444);       // need m/s * 100, but speed is in knots
                g.PlowInp = false;
                g.WingInp = false;
                g.TowInp = false;
                TCPPacketBuffer b = MakeGPSReading(g);
                b.lastDate = DateTime.UtcNow;
                Global.messLog.Flush("GPS Data Id {0} Speed {1} Lat {2} Lon {3} Date {4}", b.DevId, data.Speed, data.Lat, data.Lon, data.Dt);
                if (!AddToMSMQueue(q, b))
                    Result = GetResponse(-2);
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
            if (gpsData.TempValid)
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

                            bool test = myConvert.ToBoolean(WebConfigurationManager.AppSettings["iamtest"]);

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

        private void InsertErrorRecord(string customer, string error, Request req)
        {
            SqlStmt stmt = null;
            try
            {
                bool test = myConvert.ToBoolean(WebConfigurationManager.AppSettings["iamtest"]);
                string serviceName = myConvert.ToString(WebConfigurationManager.AppSettings["servicename"], "Not Set in web service config");

                string connStr = CustConnectionString(req);
                SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                stmt = new SqlStmt();
                if (GetStmt(connStr, out stmt))
                {
                    sql.Add("cust_code", customer);
                    sql.Add("service_name", serviceName);
                    sql.Add("error_message", error);
                    sql.Add("version", VERSION);
                    sql.AddNow("error_time");
                    sql.Add("test", test);
                }

                if (!stmt.DoSQL(sql.BuildInsert("INSERT INTO service_errors")))
                {
                    Global.messLog.Flush(string.Format("Failed to insert logg error"));
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush(string.Format("InsertErrorRecord Excp: {0}", ex.Message));
            }
            finally
            {
                if (stmt != null)
                {
                    stmt.Close();
                    stmt = null;
                }
            }
        }

        private bool LowSecurityCheck(Request request)
        {
            return LowSecurityCheck(request, string.Empty, false);
        }

        private bool LowSecurityCheck(Request request, string function)
        {
            return LowSecurityCheck(request, function, false);
        }

        private bool LowSecurityCheck(Request request, string function, bool onlyNull)
        {
            bool Result = false;
            if (request != null)
            {
                Result = (request.UserName == DEFAULT_USER && request.Password == DEFAULT_PASSWORD);
                if (!Result)
                    Global.messLog.Flush("LowSecurityCheck() failed with {0} and {1} {2}", request.UserName, request.Password, function);
                else
                {
                    if (function != string.Empty && !onlyNull)
                        Global.messLog.Flush("{0}() HC: {1}  CC: {2} - {3}", function, request.Handcode, request.CustCode, request.Version);
                }
            }
            else
            {
                Global.messLog.Flush("Received Null Request" + ((function != string.Empty) ? " - " + function : ""));
            }

            return Result;
        }

        [WebMethod(Description = "Add Patrol Weather Information")]
        public int AddPatrolWeather(Request request, TPatrol511 patrol)
        {
            int Result = -1;
            if (CheckCredentials(request))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("hand_code", patrol.HandCode);
                        sql.Add("start_date", patrol.FromDate);
                        sql.Add("end_date", patrol.ToDate);
                        sql.Add("segment_id", -1);
                        sql.Add("segment", patrol.FromSegment);
                        sql.Add("perc_cond", patrol.PCPercipitationConditions);
                        sql.Add("perc_cond_an", patrol.PCPercipitationConditionsAN);
                        sql.Add("perc", patrol.PCPercipitation);
                        sql.Add("perc_an", patrol.PCPercipitationAN);
                        sql.Add("ac_wind", patrol.ACWind);
                        sql.Add("ac_wind_an", patrol.ACWindAN);
                        sql.Add("ac_wind_dir", patrol.ACWindDirection);
                        sql.Add("ac_vis", patrol.ACVisibility);
                        sql.Add("ac_vis_an", patrol.ACVisibilityAN);
                        sql.Add("ac_sky_cond", patrol.ACSkyCloudConditions);
                        sql.Add("ac_sky_cond_an", patrol.ACSkyCloudConditionsAN);
                        sql.Add("ac_fog", patrol.ACFog);
                        sql.Add("ac_fog_an", patrol.ACFogAN);
                        sql.Add("ac_drifting", patrol.ACDrifting);
                        sql.Add("ac_drifting_an", patrol.ACDriftingAN);
                        sql.Add("road_cond", patrol.RoadConditions);
                        sql.Add("road_cond_an", patrol.RoadConditionsAN);
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO weather_data"));
                        Result = 1;
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("AddPatrolWeather() Ex: {0}", ex.Message);
                    InsertErrorRecord(request.CustCode, "AddPatrolWeather() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Request list of processed trips")]
        public Trip[] RequestTrips(Request request, int handCode, DateTime startDate, int rowCount)
        {
            Trip[] Result = null;
            if (LowSecurityCheck(request, "RequestTrips"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Trip> trips = new List<Trip>();

                        if (startDate == null)
                            startDate = DateTime.UtcNow;
                        bool rc = stmt.DoSQLFormat("SELECT TOP {0} * FROM trip WHERE start_trip < {{ts '{1}'}} and hand_code = {2} ORDER BY start_trip DESC", rowCount, startDate.ToString(LONG_DATE), handCode);
                        while (rc)
                        {
                            Trip t = new Trip();
                            t.StartDate = stmt.dtfetch("start_trip");
                            t.EndDate = stmt.dtfetch("end_trip");
                            t.TripNumber = stmt.ifetch("trip_number");
                            trips.Add(t);
                            rc = stmt.dbSkip();
                        }
                        Result = trips.ToArray();
                        trips.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("RequestTrips() Ex: {0}", ex.Message);
                    InsertErrorRecord(request.CustCode, "RequestTrips() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve range of GPS readings")]
        public GPSData[] GPSDataRangeRequest(Request request, int devId, string unitNum, int maxRows, bool firstBlock, DateTime start, DateTime end)
        {
            GPSData[] Result = null;

            if (!LowSecurityCheck(request, "GPSDataRangeRequest"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    if (unitNum != string.Empty)
                    {
                        if (stmt.DoSQLFormat("SELECT hand_code FROM vehicles WHERE unit_num = '{0}'", stmt.FixQuote(unitNum)))
                            devId = stmt.ifetch("hand_code");
                    }
                    if (stmt.DoSQLFormat("SELECT * FROM handheld WHERE hand_code = {0}", devId))
                    {
                        bool rc = stmt.DoSQLFormat("SELECT TOP {4} * FROM messagein WHERE hand_code = {0} and rdate >{1} {{ts '{2}'}} and rdate <= {{ts '{3}'}} ORDER BY rdate", devId, firstBlock ? "=" : "", start.ToString(LONG_DATE), end.ToString(LONG_DATE), maxRows);
                        if (rc)
                        {
                            List<GPSData> gpsdata = new List<GPSData>();
                            while (rc)
                            {
                                GPSData g = new GPSData();
                                g.DevId = devId;
                                g.UTCDate = stmt.dtfetch("rdate");
                                g.SOG = stmt.ifetch("speed");
                                g.COG = stmt.ifetch("degrees");
                                g.Latitude = stmt.dfetch("latitude");
                                g.Longitude = stmt.dfetch("longitude");
                                g.PlowInp = stmt.tfetch("plow");
                                g.WingInp = stmt.tfetch("wing");
                                g.TowInp = stmt.tfetch("tow");
                                g.TempAir = stmt.dfetch("temp_air");
                                g.TempSurface = stmt.dfetch("temp_road");
                                if (!stmt.IsNull("cell_strength"))
                                    g.CellStrength = stmt.ifetch("cell_strength");
                                g.IDate = stmt.dtfetch("idate");
                                gpsdata.Add(g);
                                rc = stmt.dbSkip();
                            }
                            Result = gpsdata.ToArray();
                            gpsdata.Clear();
                        }
                    }
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GPSDataRangeRequest() Excp: {0}", x.Message);
                InsertErrorRecord(request.CustCode, "GPSDataRangeRequest()" + x.Message, request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Save Route Data")]
        public int SaveRoute(Request request, Route route)
        {
            int Result = -1;
            if (LowSecurityCheck(request, "SaveRoute"))
            {
                Result = -2;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        Result = 0;
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                        sql.Add("code", route.Code);
                        sql.Add("area", route.Area);
                        sql.Add("yard", route.Yard);
                        sql.Add("name", route.Name);
                        sql.Add("info", route.Info);
                        sql.Add("date", route.Date);
                        sql.Add("last", route.Last);
                        sql.Add("type", route.RType);

                        string whereClause = string.Format("WHERE code = '{0}'", sql.FixUpEx(route.Code));
                        bool insFlag = !stmt.DoSQLFormat("SELECT id FROM routes {0}", whereClause);
                        int routeId = -1;
                        if (!insFlag)
                            routeId = stmt.ifetch("id");
                        // either update or insert into the routes table
                        string cmdStr = sql.BuildCmd("routes", insFlag, whereClause, true);
                        stmt.DoSQL(cmdStr);
                        if (insFlag)
                        {
                            // get this record id
                            stmt.DoSQL("SELECT IDENT_CURRENT('routes') as temp");
                            routeId = stmt.ifetch("temp");
                        }
                        string whereRid = string.Format("WHERE route_id = {0}", routeId);
                        // delete the route points
                        stmt.DoSQLFormat("DELETE FROM route_points {0}", whereRid);
                        // add in the new route points

                        route.RoutePoints.OrderBy(item => item.Dst).ToArray();
                        route.WayPoints.OrderBy(item => item.Dst).ToArray();
                        int rtpnt = 0;
                        int wppnt = 0;
                        foreach (RoutePoint rp in route.RoutePoints)
                        {
                            sql.Clear();
                            sql.Add("route_id", routeId);
                            sql.Add("rdate", rp.Dt);
                            sql.Add("latitude", rp.Lat);
                            sql.Add("longitude", rp.Lon);
                            sql.Add("speed", rp.Spd);
                            sql.Add("degrees", rp.Dir);
                            sql.Add("distance", rp.Dst);
                            sql.Add("point_order", rtpnt++);
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO route_points"));
                        }
                        // delete the way points
                        stmt.DoSQLFormat("DELETE FROM route_waypoints {0}", whereRid);
                        // add in the new route points
                        foreach (WayPoint wp in route.WayPoints)
                        {
                            sql.Clear();
                            sql.Add("route_id", routeId);
                            sql.Add("rdate", wp.Dt);
                            sql.Add("latitude", wp.Lat);
                            sql.Add("longitude", wp.Lon);
                            sql.Add("speed", wp.Spd);
                            sql.Add("degrees", wp.Dir);
                            sql.Add("distance", wp.Dst);
                            sql.Add("point_order", wppnt++);
                            sql.Add("wtype", wp.WTp);
                            sql.Add("hint", wp.Txt);
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO route_waypoints"));
                        }
                        Result = 1;
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("SaveRoute() Ex: {0}", ex.Message);
                    InsertErrorRecord(request.CustCode, "SaveRoute() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }
        /*
        [WebMethod(Description = "Get Route Data")]
        public Route GetRoute(Request request, string code, int? rid)
        {
            Route Result = null;
            if (LowSecurityCheck(request))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        bool rc;
                        if (code != string.Empty)
                            rc = stmt.DoSQLFormat("SELECT * FROM routes with (nolock) WHERE code = '{0}'", stmt.FixQuote(code));
                        else
                            rc = stmt.DoSQLFormat("SELECT * FROM routes with (nolock) WHERE id = {0}", rid);
                        if (rc)
                        {
                            // start by filling the route result
                            Result = new Route();
                            Result.Code = stmt.fetch("code");
                            Result.Area = stmt.ifetch("area");
                            Result.Yard = stmt.ifetch("yard");
                            Result.Name = stmt.fetch("name");
                            Result.Info = stmt.fetch("info");
                            Result.Date = stmt.dtfetch("date");
                            Result.Last = stmt.dtfetch("last");
                            Result.RType = stmt.ifetch("type");
                            int routeId = stmt.ifetch("id");
                            // next get the route points
                            rc = stmt.DoSQLFormat("SELECT * FROM route_points with (nolock) WHERE route_id = {0}", routeId);
                            List<RoutePoint> rps = new List<RoutePoint>();
                            while (rc)
                            {
                                RoutePoint rp = new RoutePoint();
                                rp.Dt = stmt.dtfetch("rdate");
                                rp.Lat = stmt.dfetch("latitude");
                                rp.Lon = stmt.dfetch("longitude");
                                rp.Spd = stmt.ifetch("speed");
                                rp.Dir = stmt.ifetch("degrees");
                                rp.Dst = stmt.dfetch("distance");
                                rp.Order = stmt.ifetch("point_order");
                                rps.Add(rp);
                                rc = stmt.dbSkip();
                            }
                            // get the array of route points
                            Result.RoutePoints = rps.ToArray();
                            // next get the way points
                            rc = stmt.DoSQLFormat("SELECT * FROM route_waypoints with (nolock) WHERE route_id = {0}", routeId);
                            List<WayPoint> wps = new List<WayPoint>();
                            while (rc)
                            {
                                WayPoint wp = new WayPoint();
                                wp.Dt = stmt.dtfetch("rdate");
                                wp.Lat = stmt.dfetch("latitude");
                                wp.Lon = stmt.dfetch("longitude");
                                wp.Spd = stmt.ifetch("speed");
                                wp.Dir = stmt.ifetch("degrees");
                                wp.Dst = stmt.dfetch("distance");
                                wp.WTp = stmt.ifetch("wtype");
                                wp.Order = stmt.ifetch("point_order");
                                wp.Txt = stmt.fetch("hint");
                                wps.Add(wp);
                                rc = stmt.dbSkip();
                            }
                            // get the array of way points
                            Result.WayPoints = wps.ToArray();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetRoute() Ex: {0}", ex.Message);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Get a List of Routes")]
        public Route[] GetRouteList(Request request)
        {
            Route[] Result = null;
            if (LowSecurityCheck(request))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Route> routes = new List<Route>();
                        bool rc = stmt.DoSQLFormat("SELECT * FROM routes with (nolock) ORDER BY code");
                        while(rc)
                        {
                            // start by filling the route result
                            Route r = new Route();
                            r.Id = stmt.ifetch("id");
                            r.Code = stmt.fetch("code");
                            r.Area = stmt.ifetch("area");
                            r.Yard = stmt.ifetch("yard");
                            r.Name = stmt.fetch("name");
                            r.Info = stmt.fetch("info");
                            r.Date = stmt.dtfetch("date");
                            r.Last = stmt.dtfetch("last");
                            r.RType = stmt.ifetch("type");
                            routes.Add(r);
                            rc = stmt.dbSkip();
                        }
                        // get the array of routes
                        Result = routes.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetRouteList() Ex: {0}", ex.Message);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }
        */
        private bool FindSpatialMatch(SqlStmt stmt, SpatData spat)
        {
            int i = 0;
            try
            {
                bool rc = stmt.DoSQLFormat("SELECT * FROM MyDistance({0},{1},0.002) order by distance", spat.Longitude, spat.Latitude);
                while (rc)
                {
                    spat.ObjIds[i] = stmt.lfetch("object_id");
                    spat.Dist[i] = stmt.dfetch("distance");
                    if (++i >= 3)
                        break;
                    rc = stmt.dbSkip();
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("FindSpatialMatch() Excp: ", ex.Message);
            }

            return i > 0;
        }

        private bool GetRoadNet(SpatData spat, out string address)
        {
            SqlHelper sql = new SqlHelper();

            bool found = false;
            address = string.Empty;
            SqlStmt stmt = new SqlStmt("SpatialConn");
            try
            {
                found = FindSpatialMatch(stmt, spat);

                if (found)
                {
                    found = false;
                    // sometimes there is no address info for a give object, so try all 3
                    for (int ndx = 0; ndx < spat.ObjIds.Length; ndx++)
                    {
                        // look for address info
                        bool rc = stmt.DoSQLFormat("SELECT HOUSE_NUMBER_STRUCTURE,FIRST_HOUSE_NUMBER,LAST_HOUSE_NUMBER,FULL_STREET_NAME,STANDARD_MUNICIPALITY FROM orn_address_info WITH (nolock) WHERE ROAD_NET_ELEMENT_ID={0} ORDER BY event_id", spat.ObjIds[ndx]);
                        if (rc)
                        {
                            // can't very well skip through every record?!?
                            int max = 6;
                            while (rc)
                            {
                                string streetName = stmt.fetch("full_street_name");
                                if (streetName.Length > 2)
                                {
                                    address = string.Format("{0}, {1}", streetName, stmt.fetch("standard_municipality"));
                                    found = true;      // we found a street!
                                    break;
                                }
                                rc = stmt.dbSkip();

                                if (--max <= 0)
                                    rc = false;
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("GetRoadNetDataEx() Excp: ", ex.Message);
            }
            finally
            {
                stmt.Close();
            }
            return found;
        }


        [WebMethod(Description = "Get details of the temperature and street location at a specific time")]
        public WeatherLocation GetWeatherLocation(Request request, int devId, DateTime locationDate)
        {
            WeatherLocation Result = null;
            if (LowSecurityCheck(request, "GetWeatherLocation"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        // subtract 4 minutes
                        DateTime locDateMin4 = locationDate.Subtract(new TimeSpan(0, 4, 0));
                        string sqlStr = string.Format("SELECT top 50 * FROM messagein with (nolock) WHERE rdate >= {0} and hand_code = {1} ORDER BY rdate", SqlHelper.timeStamp(locDateMin4, SqlHelper.dbType.SqlServer), devId);
                        bool rc = stmt.DoSQL(sqlStr);
                        if (rc)
                        {
                            Result = new WeatherLocation();
                            while (rc)
                            {
                                // make sure we always get a result
                                Result.calcLat = stmt.dfetch("latitude");
                                Result.calcLong = stmt.dfetch("longitude");
                                Result.tempAir = stmt.dfetch("temp_air");
                                Result.tempRoad = stmt.dfetch("temp_road");
                                // if this records date > requested we're done!
                                if (stmt.dtfetch("rdate") > locationDate)
                                    break;
                                rc = stmt.dbSkip();
                            }
                            SpatData spatData = new SpatData(Result.calcLat, Result.calcLong);

                            string address;
                            if (GetRoadNet(spatData, out address))
                            {
                                Result.address = address;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetWeatherLocation() Ex: {0}", ex.Message);
                    InsertErrorRecord(request.CustCode, "GetWeatherLocation() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Request list of Yards")]
        public Yard[] RequestYards(Request request)
        {
            Yard[] Result = null;
            if (LowSecurityCheck(request, "RequestYards"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Yard> yards = new List<Yard>();
                        bool rc = stmt.DoSQL("SELECT fleet.id as fid, * FROM distribution_center LEFT JOIN fleet ON fleet.distribution_id = distribution_center.id ORDER BY distribution_center.name");
                        while (rc)
                        {
                            Yard y = new Yard();
                            y.Name = stmt.fetch("name");
                            y.Address = stmt.fetch("address");
                            y.Province = stmt.fetch("province");
                            y.Postal = stmt.fetch("postal");
                            y.Phone = stmt.fetch("phone");
                            y.GEOFence = stmt.fetch("geofence_locations");
                            y.BrineDensity = stmt.dfetch("brine_density");
                            y.PerSodium = stmt.dfetch("per_sodium");
                            y.Visible = stmt.tfetch("visible");
                            y.FleetId = stmt.ifetch("fid");
                            yards.Add(y);

                            rc = stmt.dbSkip();
                        }
                        Result = yards.ToArray();
                        yards.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("RequestYards() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, "RequestYards() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }


        [WebMethod(Description = "S3 info for the tablets only")]
        public int GetServerInfo(Request req, out AppServerInfo outData)
        {
            outData = new AppServerInfo();
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "GetServerInfo"))
                return -1;
            else
            {
                if (GetAppServerInfo(outData, req.CustCode, false))
                    return 1;
                else
                    return -2;
            }
        }

        [WebMethod(Description = "Send Request to Roada database to check if the device needs new config scripts")]
        public ManagerData ManagerCheckin(Request request, Comissioninfo comInfo)
        {

            ManagerData Result = null;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(request, "ManagerCheckin", true))
                return null;

            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;
                Result = new ManagerData();

                Global.messLog.Flush("ManagerCheckin() HC: {0,5:####0} Version: {1} Cust: {2} IMEI: {3}", request.Handcode, request.Version, request.CustCode, comInfo.IMEI);
                if (!stmt.DoSQL(String.Format("SELECT * FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI)))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.Add("pc_code", comInfo.Owner);
                    sql.Add("client_guid", comInfo.IMEI);
                    sql.Add("description", comInfo.desc);
                    sql.Add("device_hardware", comInfo.hardware);
                    sql.Add("installer", 0);
                    sql.Add("roada_go", 0);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO soft_activate"));
                }
                else //this is just to make sure we get teh device hardware for existing devices at least until all the devices have checked in once
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.Add("device_hardware", comInfo.hardware);
                    stmt.DoSQL(sql.BuildUpdate("UPDATE soft_activate", string.Format("WHERE client_guid = '{0}'", comInfo.IMEI)));
                }
                bool testUrl;
                if (stmt.DoSQLFormat("SELECT * FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI))
                {

                    Result.PhoneID = stmt.ifetch("id"); //use this for id to commision device
                    Result.HandCode = stmt.ifetch("hand_code");
                    Result.roadaDiag = stmt.bfetch("installer");
                    Result.roadaDiagVer = stmt.dfetch("diag_version");
                    Result.roadaGo = stmt.bfetch("roada_go");
                    Result.goVer = stmt.dfetch("go_version");
                    testUrl = stmt.bfetch("testurl");

                }
                else
                {
                    return null;
                }



                if (stmt.bfetch("update_now") || comInfo.ForceUpdate)
                {
                    bool validConfig = false;
                    if (stmt.bfetch("roada_go"))
                    {
                        if (GetRoadaGoConfig(Result, stmt.dfetch("go_version")) && GetAppServerInfo(Result.goConfig.ServerInfo, stmt.fetch("db_codes"), testUrl))
                        {
                            validConfig = true;
                        }
                    }
                    if (stmt.bfetch("installer")) // Field name can ge changed to diag_codes at later date 
                    {
                        if (GetCustomerDataList(Result, stmt.fetch("diag_customers")) && GetDiagConfig(Result, stmt.dfetch("go_version")))
                        {
                            validConfig = true;
                        }
                    }

                    if (validConfig) // Will let device know it has a new script to replace old one with
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("update_now", 0);
                        sql.Add("last_checkin", DateTime.UtcNow);
                        stmt.DoSQL(sql.BuildUpdate("UPDATE soft_activate", string.Format("WHERE client_guid = '{0}'", comInfo.IMEI)));
                        // Global.messLog.Flush("Device updated HandCode: {0} GUID: {1}", Result.HandCode, comInfo.IMEI);
                        Result.updatenow = true;
                    }
                }

            }
            catch (Exception ex)
            {
                Result = null;
                Global.messLog.Flush("ManagerCheckin()HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                InsertErrorRecord(request.CustCode, "ManagerCheckin() " + ex.Message, request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        public bool GetRoadaGoConfig(ManagerData mdata, double version)
        {
            bool validConfig = false;
            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;

                if (stmt.DoSQL(String.Format("SELECT * FROM go_config WHERE version = {0}", version)))
                {
                    mdata.goConfig.version = stmt.dfetch("version");
                    mdata.goConfig.PhotoInterval = stmt.ifetch("photo_interval");
                    mdata.goConfig.description = stmt.fetch("description");
                    mdata.goConfig.cameraSound = stmt.bfetch("camera_sound");
                    mdata.goConfig.MainActivity = stmt.ifetch("main_activity");
                    mdata.goConfig.ttsEnable = stmt.bfetch("tts_enable");
                    mdata.goConfig.offlineLogin = stmt.bfetch("offline_login");
                    mdata.goConfig.adbOn = stmt.bfetch("adb_on");
                    mdata.goConfig.AutoResume = stmt.bfetch("auto_resume");
                    mdata.goConfig.tracking = stmt.bfetch("tracking");
                    mdata.goConfig.photoService = stmt.bfetch("photo_service");
                    mdata.goConfig.eventService = stmt.bfetch("event_service");
                    mdata.goConfig.pdTimeout = stmt.ifetch("pd_timeout");
                    mdata.goConfig.esTimeout = stmt.ifetch("es_timeout");
                    mdata.goConfig.dmTimeout = stmt.ifetch("dm_timeout");
                    mdata.goConfig.opps = stmt.bfetch("opps");
                    mdata.goConfig.partolFrq = stmt.bfetch("patrol_frq");

                    validConfig = true;
                }
                else
                {
                    Global.messLog.Flush("GetRoadaGoConfig() Failed to get Go_Version--{0}", version);
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("GetRoadaGoConfig() {0}", ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return validConfig;
        }
        public bool GetDiagConfig(ManagerData mdata, double version)
        {
            bool validConfig = false;
            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;

                if (stmt.DoSQL(String.Format("SELECT * FROM diag_config WHERE version = {0}", version)))
                {
                    mdata.diagConfig.version = stmt.dfetch("version");
                    mdata.diagConfig.description = stmt.fetch("description");
                    validConfig = true;
                }
                else
                {
                    Global.messLog.Flush("GetDiagConfig() Failed to get Go_Version--{0}", version);
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("GetDiagConfig() {0}", ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return validConfig;
        }

        private bool GetAppServerInfo(AppServerInfo ServerInfo, string custCode, bool TestServer)
        {
            bool validConfig = false;
            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;

                if (stmt.DoSQL(String.Format("SELECT * FROM customer WHERE cust_code = '{0}'", custCode)))
                {
                    ServerInfo.customerCode = custCode;
                    if (TestServer)
                    {
                        ServerInfo.WebServiceUrl = stmt.fetch("go_test_weburl");
                    }
                    else
                    {
                        ServerInfo.WebServiceUrl = stmt.fetch("go_weburl");
                    }
                    ServerInfo.PhotoBucket = stmt.fetch("go_photo_bucket");
                    ServerInfo.PickupBucket = stmt.fetch("go_pickup_bucket");
                    ServerInfo.ReportBucket = stmt.fetch("go_report_bucket");
                    ServerInfo.ObjStoreUrl = stmt.fetch("obj_store_url");
                    ServerInfo.AmazonSecretKey = stmt.fetch("amz_secret_key");
                    ServerInfo.AmazonAccessKey = stmt.fetch("amz_access_key");
                    ServerInfo.patrolFrqURL = stmt.fetch("patrol_frq");
                    validConfig = true;
                }
                else
                {
                    Global.messLog.Flush("GetAppServerInfo() Failed to get Customer--{0}", custCode);
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("GetAppServerInfo() {0}", ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return validConfig;
        }

        private bool GetCustomerDataList(ManagerData mdata, string customers)
        {
            bool validConfig = false;
            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;

                string[] values = customers.Split(',');

                foreach (string customer in values)
                {
                    stmt.DoSQL(String.Format("SELECT * FROM customer WHERE cust_code = '{0}'", customer));
                    AppServerInfo asi = new AppServerInfo();
                    asi.customerCode = customer;
                    asi.WebServiceUrl = stmt.fetch("go_weburl");
                    asi.PhotoBucket = stmt.fetch("go_photo_bucket");
                    asi.PickupBucket = stmt.fetch("go_pickup_bucket");
                    asi.ReportBucket = stmt.fetch("go_report_bucket");
                    asi.ObjStoreUrl = stmt.fetch("obj_store_url");
                    asi.AmazonSecretKey = stmt.fetch("amz_secret_key");
                    asi.AmazonAccessKey = stmt.fetch("amz_access_key");
                    mdata.diagConfig.CustomerInfo.Add(asi);
                    validConfig = true;
                }


            }
            catch (Exception ex)
            {
                Global.messLog.Flush("GetCustomerDataList() Failed {0}", ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return validConfig;
        }

        [WebMethod(Description = "Commission Software")]
        public CommResponse CommSoftware(Request request, string guid, string descr)
        {
            CommResponse response = new CommResponse();
            response.CommFlag = false;
            response.Mesg = "Unknown error";
            // added security!!
            if (LowSecurityCheck(request, "CommSoftware"))
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
                catch (Exception ex)
                {
                    Global.messLog.Flush("CommSoftware(): " + ex.Message);
                    InsertErrorRecord(request.CustCode, "CommSoftware() " + ex.Message, request);
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

        [WebMethod(Description = "Soft activate for phone app")]
        public CommResponse PhoneSoft(Request request, string guid)
        {
            CommResponse response = new CommResponse();
            response.CommFlag = false;
            response.Mesg = "Unknown error";
            // added security!!
            if (LowSecurityCheck(request, "PhoneSoft"))
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
                        stmt.DoSQL(string.Format("SELECT * FROM soft_activate WHERE client_guid = '{0}'", guid));

                        if (stmt.fetch("db_codes") == "")
                        {
                            response.Mesg = ("Device has not yet been Assigned Contact DM&T");
                        }
                        else
                        {

                            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                            sql.AddNow("comm_date");
                            int rowId = stmt.ifetch("id");
                            string pcCode = stmt.fetch("pc_code");
                            string dbCodes = stmt.fetch("db_codes");
                            string desc = stmt.fetch("description");
                            int HandCode = stmt.ifetch("handcode");

                            stmt.DoSQL(sql.BuildUpdate("UPDATE soft_activate", string.Format("WHERE client_guid = '{0}'", guid)));

                            if (stmt.RowsAffected != 1)
                                response.Mesg = "Failed updating activation database";
                            else
                            {
                                response.desc = desc;
                                response.Id = rowId;
                                response.pcCode = pcCode;
                                response.dbCodes = dbCodes;
                                response.handcode = HandCode;
                                response.Mesg = "Settings updated";
                                response.CommFlag = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("PhoneSoft" + ex.Message);
                    InsertErrorRecord(request.CustCode, "PhoneSoft() " + ex.Message, request);
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
            if (LowSecurityCheck(request, "GetObjects"))
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
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetObjects() " + ex.Message);
                    InsertErrorRecord(request.CustCode, "GetObjects() " + ex.Message, request);
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
            if (LowSecurityCheck(request, "SetObjects"))
            {
                SqlStmt stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;
                SqlHelper sql = null;
                try
                {
                    sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.AddNow("objdate");
                    string objs = string.Empty;
                    foreach (string s in objList)
                    {
                        if (objs.Length > 0)
                            objs += "|";
                        objs += s;
                    }
                    sql.Add("objrecvd", objs);
                    sql.Add("objects", "");
                    stmt.DoSQL(sql.BuildUpdate("UPDATE soft_activate", string.Format("WHERE id = {0}", request.CustId)));
                    Result = true;
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("SetObjects" + ex.Message);
                    InsertErrorRecord(request.CustCode, "SetObjects() " + ex.Message, request);
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
            if (!LowSecurityCheck(request, "GetFileBlock"))
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
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetFileBlock Excp: " + ex.Message);
                    InsertErrorRecord(request.CustCode, "GetFileBlock() " + ex.Message, request);
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
            if (LowSecurityCheck(request, "SetFileBlock"))
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
                catch (Exception ex)
                {
                    Global.messLog.Flush(string.Format("SetFileBlock() Failed writing block of file {0}", fileName));
                    InsertErrorRecord(request.CustCode, "SetFileBlock() " + ex.Message, request);
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
            if (LowSecurityCheck(request, "RenameFile"))
            {
                try
                {
                    string pathName = MessLog.ApplicationPath() + RECVD_PATH;
                    if (File.Exists(pathName + Path.GetFileName(newName)))
                        File.Delete(pathName + Path.GetFileName(newName));

                    File.Move(pathName + Path.GetFileName(oldName), pathName + Path.GetFileName(newName));
                    Result = true;
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush(string.Format("RenameFile() Failed renaming {0} to {1}", oldName, newName));
                    InsertErrorRecord(request.CustCode, "RenameFile() " + ex.Message, request);
                }
            }
            return Result;
        }



        [WebMethod(Description = "Get the client table info")]
        public Client[] GetClients(Request request)
        {
            Client[] Result = new Client[0];
            if (LowSecurityCheck(request, "GetClients"))
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
                    Global.messLog.Flush("Excp in GetClients HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, "GetClients() " + ex.Message, request);
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

        private void ActivateHandCode(int handcode, string IMEI)
        {

            SqlConn mConn = new SqlConn("MainConn");
            SqlStmt stmt = null;

            try
            {
                stmt = new SqlStmt(mConn);
                SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                sql.Add("hand_code", handcode);
                stmt.DoSQLFormat(sql.BuildUpdate("UPDATE soft_activate with (rowlock)", " WHERE client_guid = '{0}'"), IMEI);

            }
            catch (Exception ex)
            {
                Global.messLog.Flush("Excp in GetClients: " + ex.Message);

            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
                if (mConn != null)
                    mConn.Close();
            }
        }

        private CustomerSettings getCustInfo(string CustomerCode)
        {
            CustomerSettings CSet = null;
            SqlConn mConn = new SqlConn("MainConn");
            SqlStmt stmt = null;
            try
            {
                if (mConn.dbConnect())
                {
                    stmt = new SqlStmt(mConn);
                    stmt.DoSQL(string.Format("SELECT * FROM customer WHERE cust_code = '{0}'", CustomerCode));
                    CSet = new CustomerSettings();
                    CSet.Handcode = stmt.ifetch("next_id");

                    CSet.PortNumber = stmt.fetch("lmdirect_port");
                    CSet.CameraIP = stmt.fetch("camera_ip");
                }
                else
                {
                    CSet.ErrorResp = "Failed to get Data From Customer";
                }
            }
            catch (Exception ex)
            {
                CSet.ErrorResp = "Exception thrown " + ex.ToString();
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
                if (mConn != null)
                    mConn.Close();
            }

            return CSet;
        }

        [WebMethod(Description = "Get the next avaiable HandCode and other settings info")]
        public CustomerSettings GetCustomerSettings(Request request, int currentHandcode, string IMEI)
        {
            CustomerSettings custSet = new CustomerSettings();
            if (!LowSecurityCheck(request, "GetCustomerSettings"))
            {
                custSet.ErrorResp = "Failed to authenticate";
            }
            else
            {

                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    custSet.ErrorResp = "Failed to Get Customer Settings";

                    if (GetStmt(connStr, out stmt))
                    {
                        if (currentHandcode < 1)
                        {
                            custSet = getCustInfo(request.CustCode); // get starting value and other customer settings                           

                            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                            while (stmt.DoSQL(string.Format("SELECT hand_code FROM handheld WHERE hand_code = '{0}'", custSet.Handcode)))
                            {
                                custSet.Handcode++; // Increment the Value in handcode until it cand find a record
                            }

                            sql.Add("hand_code", custSet.Handcode);
                            sql.AddNow("commissioned");
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO handheld"));

                            ActivateHandCode(custSet.Handcode, IMEI);// adds hand code to the soft_activate table


                        }
                        else
                        {
                            custSet = getCustInfo(request.CustCode); // gets latest customer settings
                            custSet.Handcode = currentHandcode; //sets handcode back to current handcode 

                        }

                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("Excp in GetCustomerSettings HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, "GetCustomerSettings() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return custSet;
        }

        private string SerializeObject(object obj, Type objType)
        {
            string Result = string.Empty;
            StringWriter writer = new StringWriter();
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

        // helper class to ignore namespaces when de-serializing
        public class NamespaceIgnorantXmlTextReader : XmlTextReader
        {
            public NamespaceIgnorantXmlTextReader(System.IO.TextReader reader) : base(reader) { }

            public override string NamespaceURI
            {
                get { return ""; }
            }
        }

        private object DeserializeObject(string xmlString, Type objType)
        {
            object obj = null;
            StringReader stringReader = new StringReader(xmlString);
            NamespaceIgnorantXmlTextReader reader = new NamespaceIgnorantXmlTextReader(stringReader);
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
            // I needed to preserve this older version for backwards compatibility.
            return GetReplicationDataX(request, dbCode, "", -1, startRecord);
        }

        [WebMethod(Description = "Retrieve customer list from the server")]
        public Client[] GetClientList(Request request)
        {
            Client[] Result = null;
            if (!LowSecurityCheck(request, "GetClientList"))
                return Result;

            SqlConn mConn = new SqlConn("MainConn");
            SqlStmt stmt = null;
            List<Client> clients = null;
            try
            {
                mConn.Mess = Global.messLog;
                if (mConn.dbConnect())
                {
                    ReadQueueList(false);      // always must check if we have read the connection list
                    clients = new List<Client>();
                    stmt = new SqlStmt(mConn);
                    // made a change, get the comma separated list of db_codes from the soft_activate table
                    stmt.DoSQL(string.Format("SELECT db_codes FROM soft_activate WHERE pc_code = '{0}'", request.CustCode));
                    string[] dbCodes = stmt.fetch("db_codes").Split(new char[] { ',' });
                    foreach (string dbcode in dbCodes)
                    {
                        Client c = new Client();
                        c.CustCode = dbcode;
                        if (stmt.DoSQLFormat("SELECT name FROM customer WHERE cust_code = '{0}'", dbcode))
                            c.Name = stmt.fetch("name");
                        else
                            c.Name = "Not Found!";
                        c.SaveData = false;
                        clients.Add(c);
                    }
                    Result = clients.ToArray();
                    clients.Clear();
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("Excp in GetClients HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                InsertErrorRecord(request.CustCode, "GetClientList() " + ex.Message, request);
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

            return Result;
        }

        private string CustConnectionString(Request request)
        {
            string Result = string.Empty;

            ReadCustomers(false);
            foreach (MQueue mq in custList)
                if (mq.CustCode == request.CustCode)
                {
                    Result = mq.ConnectionString;
                    break;
                }

            if (Result == string.Empty)
            {
                Global.messLog.Flush("Unknown customer code CustConnectionString(): {0}  HC: {1}  Ver: {2}", request.CustCode, request.Handcode, request.Version);

                // SORRY, CANNOT CALL THIS FUNCTION FROM HERE
                // THE FIRST THING InsertErrorRecord DOES IS CALL CustConnectionString TO WRITE ERROR TO TABLE ... RECURSION!!!!
                // InsertErrorRecord(request.CustCode, "CustConnectionString() " + request.CustCode.ToString(), request);
            }

            return Result;
        }

        [WebMethod(Description = "Send XBee Acknowledge")]
        public int AckXBee(Request request, XBAck[] dat)
        {
            int Result = 0;

            if (CheckCredentials(request))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        Result = 1;
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        foreach (XBAck x in dat)
                        {
                            sql.Clear();
                            if (x.rOn)
                                sql.Add("rem_req_on", false);
                            if (x.rOff)
                                sql.Add("rem_req_off", false);
                            if (x.sReq)
                                sql.Add("sch_req", false);
                            if (sql.AnyQuery())
                            {
                                string sqlStr = string.Format(sql.BuildUpdate("UPDATE heater with (rowlock)", "WHERE xbee_addr = '{0}'"), x.Addr);
                                stmt.DoSQL(sqlStr);
                            }

                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AckXBee() Excp: {0}", x.Message);
                    InsertErrorRecord(request.CustCode, "AckXBee() " + x.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        private DateTime StringToDateTime(string s)
        {
            DateTime rc = DateTime.MinValue;
            try
            {
                rc = DateTime.ParseExact(s, "yyyyMMddHHmmss", null);
            }
            catch
            {
                Global.messLog.Flush("Bad Date String {0}", s);
            }
            return rc;
        }

        [WebMethod(Description = "Handle iButton Data")]
        public int iButtXBee(Request request, XBiB[] dat)
        {
            int Result = 0;

            if (CheckCredentials(request))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        Result = 1;
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        foreach (XBiB x in dat)
                        {
                            sql.Clear();
                            sql.Add("xbee_addr", x.Addr);
                            sql.Add("rdate", StringToDateTime(x.Td));
                            sql.Add("ibutton_code", x.Code);
                            sql.AddNow("idate");
                            // get the employee id based on the ibutton hex code
                            if (stmt.DoSQLFormat("SELECT id FROM employee with (nolock) WHERE ibutton_code = '{0}'", x.Code))
                                sql.Add("employee_id", stmt.ifetch("id"));
                            // get the vehicle_id from the heater table
                            if (stmt.DoSQLFormat("SELECT vehicle_id FROM heater with (nolock) WHERE xbee_addr = '{0}' and vehicle_id IS NOT NULL", x.Addr))
                                sql.Add("vehicle_id", stmt.ifetch("vehicle_id"));
                            string sqlStr = sql.BuildInsert("INSERT INTO logstatus");
                            stmt.DoSQL(sqlStr);
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("iButtXBee() Excp: {0}", x.Message);
                    InsertErrorRecord(request.CustCode, "iButtXBee() " + x.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Send XBee Status Data")]
        public XBResp[] StatXBee(Request request, XBData[] dat)
        {
            XBResp[] Result = null;

            if (CheckCredentials(request))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        // update the gateway last_date so we know the gateway is alive!
                        SqlConn mConn = new SqlConn("MainConn");
                        mConn.Mess = Global.messLog;
                        int defRunTime = DEFAULT_RUN_TIME;
                        if (mConn.dbConnect())
                        {
                            SqlStmt stmt2 = new SqlStmt(mConn);
                            // get the default run time to use
                            // since it's already there, we use CustId as the gateway id
                            stmt2.DoSQLFormat("SELECT def_run_time FROM gateway with (nolock) WHERE id = {0}", request.CustId);
                            defRunTime = stmt2.ifetch("def_run_time");
                            // we can't have a zero run time!
                            if (defRunTime == 0)
                                defRunTime = DEFAULT_RUN_TIME;
                            sql.AddNow("last_date");
                            stmt2.DoSQLFormat(sql.BuildUpdate("UPDATE gateway with (rowlock)", "WHERE id = {0}"), request.CustId);
                            stmt2.Close();
                            mConn.Close();
                        }
                        // iterate through the responses from the ConnectPort X2 gateway
                        List<XBResp> resp = new List<XBResp>();
                        foreach (XBData x in dat)
                        {
                            sql.Clear();
                            sql.AddNow("last_date");
                            sql.Add("xbee_addr", x.Addr);
                            // check for special commission command!
                            bool heaterFound = stmt.DoSQLFormat("SELECT * FROM heater with (nolock) WHERE xbee_addr = '{0}'", x.Addr);
                            if (x.In == 99)
                            {
                                sql.AddNow("idate");        // commission date
                                sql.Add("rem_req_on", false);
                                sql.Add("rem_req_off", false);
                                sql.Add("sch_req", false);
                                sql.Add("def_run_time", defRunTime);
                                if (!heaterFound)
                                    stmt.DoSQL(sql.BuildInsert("INSERT INTO heater"));
                                XBResp xbr = new XBResp();
                                xbr.Addr = x.Addr;
                                resp.Add(xbr);
                            }
                            else
                            {
                                // just standard data, store it away
                                sql.Add("ign_inp", (x.In & 1) != 0);
                                sql.Add("start_inp", (x.In & 2) != 0);
                                sql.Add("curr_run_time", x.Tim);
                                sql.Add("temp_valid", x.tV);
                                if (x.tV)
                                    sql.Add("temp_value", x.tVal);
                                else
                                    sql.Add("temp_value", "NULL", false);
                                sql.Add("soft_ver", x.Ver);
                                string sqlStr;
                                if (heaterFound)
                                {
                                    int id = stmt.ifetch("id");
                                    sqlStr = sql.BuildUpdate("UPDATE heater with (rowlock)", string.Format("WHERE id = {0}", id));
                                    bool sendFlag = false;
                                    XBResp xb = new XBResp();
                                    xb.Addr = x.Addr;
                                    if (stmt.tfetch("rem_req_on"))
                                    {
                                        xb.rOn = true;
                                        sendFlag = true;
                                    }
                                    if (stmt.tfetch("rem_req_off"))
                                    {
                                        xb.rOff = true;
                                        sendFlag = true;
                                    }
                                    if (stmt.tfetch("sch_req"))
                                    {
                                        xb.rTim = stmt.ifetch("def_run_time");
                                        xb.mTemp = stmt.dfetch("min_temp");
                                        xb.wTim = new int[7];
                                        DateTime td;
                                        int ival;
                                        for (int i = 1; i <= 7; i++)
                                        {
                                            td = stmt.dtfetch(string.Format("daywk_{0}", i));
                                            ival = td.Hour * 60 + td.Minute;
                                            xb.wTim[i - 1] = ival;
                                        }
                                        xb.oDay = stmt.dtfetch("day_ovrd");
                                        xb.sReq = true;
                                        sendFlag = true;
                                    }
                                    if (sendFlag)
                                        resp.Add(xb);
                                    stmt.DoSQL(sqlStr);
                                }
                                else
                                {
                                    sql.AddNow("idate");        // commission date
                                    sql.Add("rem_req_on", false);
                                    sql.Add("rem_req_off", false);
                                    sql.Add("sch_req", false);
                                    sql.Add("def_run_time", defRunTime);
                                    stmt.DoSQL(sql.BuildInsert("INSERT INTO heater"));
                                }
                            }
                        }
                        if (resp.Count > 0)
                        {
                            Result = resp.ToArray();
                            resp.Clear();
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("StatXBee() Excp: {0}", x.Message);
                    InsertErrorRecord(request.CustCode, "StatXBee() " + x.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        private int ConvertToUnixTimestamp(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            TimeSpan diff = date.ToUniversalTime() - origin;
            return (int)Math.Floor(diff.TotalSeconds);
        }

        [WebMethod(Description = "Request a pan Id for an XBee Gateway")]
        public XBeeCommResponse CommissionXBee(Request request, string addr, string ipaddr)
        {
            XBeeCommResponse Result = null;

            if (CheckCredentials(request))
            {
                SqlConn mConn = new SqlConn("MainConn");
                try
                {
                    mConn.Mess = Global.messLog;
                    if (mConn.dbConnect())
                    {
                        SqlStmt stmt = new SqlStmt(mConn);
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        bool insFlag = true;
                        // look for a valid commissioned gateway
                        if (stmt.DoSQLFormat("SELECT * FROM gateway WHERE xbee_addr = '{0}'", stmt.FixQuote(addr)))
                        {
                            insFlag = false;
                            if (stmt.ifetch("pan_id") > 0 && stmt.fetch("cust_code").Length > 3)
                            {
                                Result = new XBeeCommResponse();
                                Result.CustCode = stmt.fetch("cust_code");
                                Result.PanId = stmt.ifetch("pan_id");
                                Result.DSTApp = stmt.tfetch("dst_applies");
                                Result.TzOff = stmt.ifetch("tz_offset");
                                Result.GwyId = stmt.ifetch("id");
                                Result.PollRt = stmt.ifetch("poll_rate");
                                Result.UnixTim = ConvertToUnixTimestamp(DateTime.UtcNow);
                                // we now need Cust Id as well
                                stmt.DoSQLFormat("SELECT id FROM customer WHERE cust_code = '{0}'", Result.CustCode);
                                Result.CustId = stmt.ifetch("id");
                                sql.AddNow("req_date");
                                sql.Add("ip_addr", ipaddr);
                                // update the gateways req_date as to when we requested the pan_id and cust code
                                stmt.DoSQLFormat(sql.BuildUpdate("UPDATE gateway", "WHERE xbee_addr = '{0}'"), stmt.FixQuote(addr));
                            }
                        }
                        // add a record so we know on the server what the xbee_addr of the gateway is!
                        if (insFlag)
                        {
                            sql.Add("xbee_addr", addr);
                            sql.Add("ip_addr", ipaddr);
                            sql.AddNow("comm_date");
                            sql.Add("def_run_time", DEFAULT_RUN_TIME);
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO gateway"));
                        }
                        stmt.Close();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("CommissionGateway() Excp: {0}", x.Message);
                    InsertErrorRecord(request.CustCode, "CommissionGateway() " + x.Message, request);
                }
                finally
                {
                    if (mConn != null)
                        mConn.Close();
                }
            }
            return Result;
        }


        [WebMethod(Description = "Validate user login info")]
        public int ValidateUser(Request request, out UserInfo info)
        {
            int Result = -1;
            string connStr = CustConnectionString(request);
            info = null;
            if (connStr != string.Empty)
            {
                SqlStmt stmt = null;
                try
                {
                    Result = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        Result = -3;
                        if (stmt.DoSQLFormat("SELECT * FROM users WHERE username = '{0}'", stmt.FixQuote(request.UserName)))
                        {
                            if (!stmt.tfetch("mobile_user"))
                            {
                                Result = -5;
                            }
                            else
                            {
                                Result = -6;
                                if (stmt.fetch("password") == request.Password)
                                {
                                    info = new UserInfo();
                                    info.FirstName = stmt.fetch("first_name");
                                    info.LastName = stmt.fetch("last_name");
                                    info.Phone = stmt.fetch("phone_number");
                                    info.Email = stmt.fetch("email");
                                    info.userId = stmt.ifetch("id");
                                    info.title = stmt.fetch("title");
                                    //info.EmployeeNum = stmt.fetch("employee_num");
                                    info.role = stmt.ifetch("mobile_role");

                                    stmt.Clear();
                                    Result = -4;
                                    if (stmt.DoSQLFormat("SELECT * FROM go_user_roles WHERE id = {0}", info.role))
                                    {

                                        info.truckStatus = stmt.ifetch("status_perm");
                                        info.icReports = stmt.ifetch("ic_perm");
                                        info.routing = stmt.ifetch("routing_perm");
                                        info.weatherRep = stmt.ifetch("weather_perm");
                                        info.fullDiag = stmt.ifetch("diag_perm");
                                        info.camPerm = stmt.ifetch("cam_perm");
                                        Result = 1;
                                    }
                                }
                            }

                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("ValidateUser() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, "ValidateUser() " + x.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }

            return Result;
        }

        [WebMethod(Description = "Retrieve DCD device info for a specific devId")]
        public DeviceInfo GetDeviceInfo(Request request, int devId)
        {
            DeviceInfo dInfo = null;

            if (!LowSecurityCheck(request, "GetDeviceInfo"))
                return dInfo;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    if (stmt.DoSQLFormat("SELECT * FROM handheld WHERE hand_code = {0}", devId))
                    {
                        dInfo = new DeviceInfo();
                        dInfo.DevId = devId;
                        dInfo.ServerDate = DateTime.UtcNow;
                        dInfo.LoginDate = stmt.dtfetch("log_date");
                        dInfo.GPSDate = stmt.dtfetch("last_date");
                        dInfo.Latitude = stmt.dfetch("last_lat");
                        dInfo.Longitude = stmt.dfetch("last_long");
                        dInfo.Speed = stmt.ifetch("speed");
                        dInfo.FirmVers = stmt.fetch("firmware_version");
                        dInfo.ModemVers = stmt.fetch("modem_version");
                        if (!stmt.IsNull("cell_strength"))
                            dInfo.CellStrength = stmt.ifetch("cell_strength");
                        if (devId > 0 && stmt.DoSQLFormat("SELECT TOP 1 * FROM messagein WHERE hand_code = {0} and rdate >= {1}", devId, SqlHelper.timeStamp(dInfo.GPSDate)))
                        {
                            dInfo.WriteDate = stmt.dtfetch("idate");
                            dInfo.Degrees = stmt.ifetch("degrees");
                            dInfo.Plow = stmt.tfetch("plow");
                            dInfo.Wing = stmt.tfetch("wing");
                            dInfo.Tow = stmt.tfetch("tow");
                            dInfo.IOstatus = stmt.ifetch("io_status");
                            double roadTemp = stmt.dfetch("temp_road");
                            double airTemp = stmt.dfetch("temp_air");
                            if (roadTemp != 0 || airTemp != 0)     // if both zero, then not likely connected
                            {
                                dInfo.RoadTemp = roadTemp;
                                dInfo.AirTemp = airTemp;
                            }
                        }

                        if (stmt.DoSQLFormat("SELECT Max(id) as maxid FROM spreader_activity WHERE hand_code = {0} and rdate >= {{ts '{1}'}}", devId, DateTime.UtcNow.AddMinutes(-30).ToString(LONG_DATE)))
                        {
                            if (!stmt.IsNull("maxid"))
                            {
                                int maxId = stmt.ifetch("maxid");
                                stmt.DoSQLFormat("SELECT * FROM spreader_activity WHERE id = {0}", maxId);
                                dInfo.SpreadDate = stmt.dtfetch("rdate");
                                dInfo.SpreadDry = stmt.tfetch("start_spread_dry");
                                dInfo.SpreadWet = stmt.tfetch("start_spread_liq");
                                dInfo.DryRate = stmt.dfetch("dry_rate");
                                dInfo.WetRate = stmt.dfetch("wet_rate");
                            }
                        }
                        /* do not enable until engine in is re-indexed
                        if (stmt.DoSQLFormat("SELECT * from EngineIn WHERE id=(SELECT max(id) FROM EngineIn WHERE hand_code = '{0}')", devId))
                        {

                            if (stmt.dtfetch("rdate") >= DateTime.UtcNow.AddMinutes(-30))
                                {

                                int maxId = stmt.ifetch("maxid");
                                stmt.DoSQLFormat("SELECT * FROM EngineIn WHERE id = {0}", maxId);
                                dInfo.ECMDate = stmt.dtfetch("rdate");
                                if (stmt.tfetch("rpm_valid"))
                                    dInfo.RPM = stmt.dfetch("avgrpm");
                                if (stmt.tfetch("cool_valid"))
                                    dInfo.CoolTemp = stmt.dfetch("avgcool");
                                if (stmt.tfetch("oilp_valid"))
                                    dInfo.OilPress = stmt.dfetch("avgoilp");
                            }
                        }*/

                    }
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetDeviceInfo() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, "GetDeviceInfo() " + x.Message, request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return dInfo;
        }

        [WebMethod(Description = "Push Weather Report")]
        public int SendWeatherReport(Request req, WeatherData wdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendWeatherReport"))
                return -2;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    Result = -5;
                    if (wdata.fromTime.Year > 1000)         // just incase someone from the year 919 tries to send us data (1019 is ok)?
                    {
                        sql.Clear();
                        sql.Add("ci_id", wdata.reportId);
                        sql.Add("start_time", wdata.fromTime);
                        sql.Add("comm_id", wdata.commId);
                        sql.Add("unit_num", wdata.unitNum);
                        sql.Add("hand_code", wdata.handCode);
                        sql.Add("driver", wdata.driver);
                        sql.Add("p_type", wdata.percipitationType);
                        sql.Add("p_level", wdata.percipitationLvl);
                        sql.Add("w_gust", wdata.windStrength);
                        sql.Add("w_direction", wdata.windDirection);
                        sql.Add("visibility", wdata.visibility);
                        sql.Add("cloud", wdata.cloudCondition);
                        sql.Add("fog", wdata.fogLvl);
                        sql.Add("drifting", wdata.drifting);
                        sql.Add("road", wdata.roadCondition);
                        sql.Add("road2", wdata.road2ndCondition);
                        sql.Add("latitude", wdata.CurrentLat);
                        sql.Add("longitude", wdata.CurrentLong);
                        sql.Add("calc_lat", wdata.calcLat);
                        sql.Add("calc_long", wdata.calcLong);
                        sql.Add("temp_air", wdata.tempAir);
                        sql.Add("temp_road", wdata.tempRoad);
                        sql.Add("valid_loc", wdata.validLoc);
                        sql.Add("address", wdata.address);
                        sql.Add("comment", wdata.comment);
                        sql.Add("action_taken", wdata.action);
                        sql.Add("work_order", wdata.workOrder);
                        sql.AddNow("idate");

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO weather_condition"));

                        Result = 1;
                    }
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendWeatherReport() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendWeatherReport() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }
        [WebMethod(Description = "Push Collision Report")]
        public int SendReportNew(Request req, ReportData rdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendReportNew"))
                return -2;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    //DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("sig_data", rdata.Signiture);
                    sql.Add("ci_date", rdata.DateCol);
                    sql.Add("hwy", rdata.hwy);
                    sql.Add("loc_direction", rdata.locDirection);
                    sql.Add("loc_details", rdata.locDetails);
                    sql.Add("detour", rdata.Detour);
                    sql.Add("description", rdata.ColDesc);
                    sql.Add("fatal", rdata.Fatal);
                    sql.Add("injuries", rdata.injuries);
                    sql.Add("features", rdata.Physical);
                    sql.Add("other_info", rdata.Other);
                    sql.Add("report_by", rdata.repPrep);
                    sql.Add("report_phone", rdata.repPhone);
                    sql.Add("report_datetime", rdata.repDate);
                    sql.Add("opp_reports", rdata.chkOpp);
                    sql.Add("fatal_report", rdata.chkFatal);
                    sql.Add("other_report", rdata.chkOther);
                    sql.Add("message_sign", rdata.chkSign);
                    sql.Add("latitude", rdata.CurrentLat);
                    sql.Add("longitude", rdata.CurrentLong);
                    sql.Add("date_cleared", rdata.clearDate);
                    sql.Add("damage", rdata.chkDmg);
                    sql.Add("loc_inter", rdata.locIntersection);
                    sql.Add("submit_time", DateTime.UtcNow);
                    sql.Add("title", rdata.repTitle);
                    sql.Add("equipment", rdata.equipment);

                    //adds and updates main report table 
                    if (stmt.DoSQL(String.Format("SELECT ci_id FROM collision_incident WHERE ci_id = '{0}'", rdata.reportTime)))
                    {

                        stmt.DoSQL(sql.BuildUpdate("UPDATE collision_incident", String.Format("WHERE ci_id = '{0}'", rdata.reportTime)));

                        Result = 1;


                    }
                    else
                    {
                        sql.Add("ci_id", rdata.reportTime);

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_incident"));

                        Result = 2;
                    }



                    //updates add adds update files to update table 
                    int ndx = 0;
                    foreach (DateTime record in rdata.updateTime)
                    {
                        if (stmt.DoSQL(String.Format("SELECT ci_id FROM collision_updates WHERE ci_id = '{0}' AND update_time = '{1}.000'", rdata.reportTime, rdata.updateTime[ndx].ToString("yyyy-MM-dd HH:mm:ss"))))
                        {
                            sql.Clear();
                            sql.Add("description", rdata.upDesc[ndx]);
                            stmt.DoSQL(sql.BuildUpdate("UPDATE collision_updates", string.Format("WHERE ci_id = '{0}' AND update_time = '{1}.000'", rdata.reportTime, rdata.updateTime[ndx].ToString("yyyy-MM-dd HH:mm:ss"))));
                            ndx++;
                            Result = 5;
                        }


                        else
                        {
                            if (rdata.updateTime[ndx].Year > 1000)
                            {
                                sql.Clear();
                                sql.Add("ci_id", rdata.reportTime);
                                sql.Add("update_time", rdata.updateTime[ndx]);
                                sql.Add("description", rdata.upDesc[ndx]);
                                stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_updates"));
                                ndx++;
                                Result = 6;
                            }
                        }

                    }
                    ndx = 0;

                    stmt.DoSQLFormat("DELETE FROM report_ppl WHERE ci_id = '{0}'", rdata.reportTime);
                    for (int pplNdx = 0; pplNdx < rdata.name.Count; pplNdx++)
                    {
                        sql.Clear();
                        sql.Add("ci_id", rdata.reportTime);
                        sql.Add("name", rdata.name[pplNdx]);
                        sql.Add("title", rdata.title[pplNdx]);
                        sql.Add("equip", rdata.equip[pplNdx]);
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO report_ppl"));
                        Result = 6;
                    }
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendRptNew() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendRptNew() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push Collision Report")]
        public int SendReport(Request req, ReportData rdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendReport"))
                return -2;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("sig_data", rdata.Signiture);
                    sql.Add("ci_date", rdata.DateCol);
                    sql.Add("hwy", rdata.hwy);
                    sql.Add("loc_direction", rdata.locDirection);
                    sql.Add("loc_details", rdata.locDetails);
                    sql.Add("detour", rdata.Detour);
                    sql.Add("description", rdata.ColDesc);
                    sql.Add("fatal", rdata.Fatal);
                    sql.Add("injuries", rdata.injuries);
                    sql.Add("features", rdata.Physical);
                    sql.Add("other_info", rdata.Other);
                    sql.Add("report_by", rdata.repPrep);
                    sql.Add("report_phone", rdata.repPhone);
                    sql.Add("report_datetime", rdata.repDate);
                    sql.Add("opp_reports", rdata.chkOpp);
                    sql.Add("fatal_report", rdata.chkFatal);
                    sql.Add("other_report", rdata.chkOther);
                    sql.Add("message_sign", rdata.chkSign);
                    sql.Add("latitude", rdata.CurrentLat);
                    sql.Add("longitude", rdata.CurrentLong);
                    sql.Add("date_cleared", rdata.clearDate);
                    sql.Add("damage", rdata.chkDmg);
                    sql.Add("loc_inter", rdata.locIntersection);
                    sql.Add("submit_time", now);
                    sql.Add("title", rdata.repTitle);
                    sql.Add("equipment", rdata.equipment);

                    //adds and updates main report table 
                    if (stmt.DoSQL(String.Format("SELECT ci_id FROM collision_incident WHERE ci_id = '{0}-{1}'", rdata.phoneID, rdata.reportTime)))
                    {

                        stmt.DoSQL(sql.BuildUpdate("UPDATE collision_incident", String.Format("WHERE ci_id = '{0}-{1}'", rdata.phoneID, rdata.reportTime)));

                        Result = 1;


                    }
                    else
                    {
                        sql.Add("ci_id", String.Format("{0}-{1}", rdata.phoneID, rdata.reportTime));

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_incident"));

                        Result = 2;
                    }



                    //updates add adds update files to update table 
                    int ndx = 0;
                    foreach (DateTime record in rdata.updateTime)
                    {
                        if (stmt.DoSQL(String.Format("SELECT ci_id FROM collision_updates WHERE ci_id = '{0}-{1}' AND update_time = '{2}.000'", rdata.phoneID, rdata.reportTime, rdata.updateTime[ndx].ToString("yyyy-MM-dd HH:mm:ss"))))
                        {
                            sql.Clear();
                            sql.Add("description", rdata.upDesc[ndx]);
                            stmt.DoSQL(sql.BuildUpdate("UPDATE collision_updates", string.Format("WHERE ci_id = '{0}-{1}' AND update_time = '{2}.000'", rdata.phoneID, rdata.reportTime, rdata.updateTime[ndx].ToString("yyyy-MM-dd HH:mm:ss"))));
                            ndx++;
                            Result = 5;
                        }


                        else
                        {
                            if (rdata.updateTime[ndx].Year > 1000)
                            {
                                sql.Clear();
                                sql.Add("ci_id", String.Format("{0}-{1}", rdata.phoneID, rdata.reportTime));
                                sql.Add("update_time", rdata.updateTime[ndx]);
                                sql.Add("description", rdata.upDesc[ndx]);
                                stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_updates"));
                                ndx++;
                                Result = 6;
                            }
                        }

                    }
                    ndx = 0;

                    stmt.DoSQLFormat("DELETE FROM report_ppl WHERE ci_id = '{0}-{1}'", rdata.phoneID, rdata.reportTime);
                    for (int pplNdx = 0; pplNdx < rdata.name.Count; pplNdx++)
                    {
                        sql.Clear();
                        sql.Add("ci_id", String.Format("{0}-{1}", rdata.phoneID, rdata.reportTime));
                        sql.Add("name", rdata.name[pplNdx]);
                        sql.Add("title", rdata.title[pplNdx]);
                        sql.Add("equip", rdata.equip[pplNdx]);
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO report_ppl"));
                        Result = 7;
                    }
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendReport() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendReport() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }



        [WebMethod(Description = "Push Camera Image")]
        public int SendImage(Request req, ImgRec imgRec)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendImage"))
                return -2;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.Add("idate", DateTime.UtcNow);
                    sql.Add("rdate", imgRec.rDate);
                    sql.Add("hand_code", imgRec.HC);
                    if (stmt.DoSQL(String.Format("SELECT unit_num FROM vehicles with (nolock) WHERE hand_code = {0}", imgRec.HC)))
                        sql.Add("unit_num", stmt.fetch("unit_num"));
                    sql.Add("evt_type", imgRec.EType);
                    sql.Add("latitude", imgRec.Lat);
                    sql.Add("longitude", imgRec.Lon);
                    sql.Add("speed", imgRec.Sog);
                    sql.Add("degrees", imgRec.Cog);
                    stmt.DoSQL(sql.BuildInsert("INSERT INTO img_events"));
                    // get this record id
                    stmt.DoSQL("SELECT IDENT_CURRENT('img_events') as temp");
                    int id = stmt.ifetch("temp");
                    sql.Clear();
                    sql.Add("event_id", id);
                    sql.Add("pic_image", imgRec.Img);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO img_photos"));
                    Result = 1;
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendImage() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendImage() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        private int SavePatrolTripData(SqlStmt stmt, PatrolTripData data)
        {
            // Internal to minimise duplicate code ...
            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

            sql.Add("hand_code", data.HandCode);
            sql.Add("unit_num", data.UnitNum);
            sql.Add("user_name", data.UserName);
            sql.Add("report_time", data.ReportTime);
            sql.Add("latitude", data.Lat);
            sql.Add("longitude", data.Lon);

            bool ins;

            if (data.TripId > 0)
            {
                if (stmt.DoSQL(String.Format("SELECT * FROM patrol_trip WHERE id = {0}", data.TripId)))
                    ins = false;
                else
                    ins = true;
            }
            else
            {
                if (stmt.DoSQL(String.Format("SELECT * FROM patrol_trip WHERE hand_code = {0} AND user_name = '{1}' AND report_time = {2}", data.HandCode, data.UserName, SqlHelper.timeStamp(data.ReportTime, SqlHelper.dbType.SqlServer))))
                {
                    data.TripId = stmt.ifetch("id");
                    ins = false;
                }
                else
                    ins = true;
            }

            if (ins)
            {
                stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO patrol_trip"));

                // get this record id ...
                stmt.DoSQL("SELECT IDENT_CURRENT('patrol_trip') AS temp");

                data.TripId = stmt.ifetch("temp");
            }                           // there is no UPDATE permitted here ...

            if (data.TripId > 0)
                return data.TripId;
            else
                return -1;
        }
        private bool SavePatrolTripDetailData(SqlStmt stmt, PatrolTripDetailData data)
        {
            // Internal to minimise duplicate code ...
            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

            sql.Add("trip_id", data.TripId);
            sql.Add("rec_type", data.Type);
            sql.Add("st_time", data.Start);
            sql.Add("st_lat", data.StLat);
            sql.Add("st_lon", data.StLon);
            sql.Add("fi_time", data.Finish);
            sql.Add("fi_lat", data.FiLat);
            sql.Add("fi_lon", data.FiLon);
            sql.Add("notes", data.Notes);

            bool ins;
            string where;

            if (data.Type == 0)
                where = String.Format("WHERE trip_id = {0} AND rec_type = 0", data.TripId);
            else
                where = String.Format("WHERE trip_id = {0} AND st_time = {1}", data.TripId, SqlHelper.timeStamp(data.Start, SqlHelper.dbType.SqlServer));

            if (stmt.DoSQL("SELECT * FROM patrol_trip_detail " + where))
                ins = false;
            else
                ins = true;

            if (ins)
                return stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO patrol_trip_detail"));
            else
                return stmt.DoSQL(sql.BuildUpdate("UPDATE patrol_trip_detail", where));
        }

        [WebMethod(Description = "Saves Patrol Trip Data to DB.  Returns 'TripId' when more than 0.")]
        public int SendPatrolTripData(Request req, PatrolTripData data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendPatrolTripData"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    Result = SavePatrolTripData(stmt, data);

                    if (Result <= 0)
                        Result = -4;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("SendPatrolTripData() report Data failed to send");
                InsertErrorRecord(req.CustCode, "SendPatrolTripData() " + ex.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Saves Patrol Trip Data to DB.  Returns 'TripId' when more than 0.")]
        public int SendPatrolTripXData(Request req, PatrolTripData data, PatrolTripDetailData trip)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendPatrolTripXData"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    Result = SavePatrolTripData(stmt, data);

                    if (Result > 0)
                    {
                        trip.TripId = Result;
                        SavePatrolTripDetailData(stmt, trip);
                    }
                    else
                        Result = -4;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("SendPatrolTripXData() report Data failed to send");
                InsertErrorRecord(req.CustCode, "SendPatrolTripXData() " + ex.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Saves Patrol Trip Detail Data to DB.")]
        public int SendPatrolTripDetailData(Request req, PatrolTripDetailData data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendPatrolTripDetailData"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    SavePatrolTripDetailData(stmt, data);

                    Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("SendPatrolTripDetailData() report Data failed to send");
                InsertErrorRecord(req.CustCode, "SendPatrolTripDetailData() " + ex.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "ValidateUserList.")]
        public bool ValidateUserList(Request req, int Version)
        {
            bool Result = false;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "ValidateUserList"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL("SELECT TOP 1 users FROM go_update"))
                            Result = (stmt.ifetch("users") != Version);
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("ValidateUserList() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    InsertErrorRecord(req.CustCode, "ValidateUserList() " + x.Message, req);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "ValidateVehicleList.")]
        public bool ValidateVehicleList(Request req, int Version)
        {
            bool Result = false;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "ValidateVehicleList"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL("SELECT TOP 1 vehicles FROM go_update"))
                            Result = (stmt.ifetch("vehicles") != Version);
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("ValidateUserList()HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    InsertErrorRecord(req.CustCode, "ValidateUserList() " + x.Message, req);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Get User table version number")]
        public int getUserListVersion(Request req)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "getUserListVersion"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL("SELECT TOP 1 users FROM go_update"))
                            Result = stmt.ifetch("users");
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("getUserListVersion() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    InsertErrorRecord(req.CustCode, "getUserListVersion() " + x.Message, req);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Get Vehicle table version number")]
        public int getVehicleListVersion(Request req)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "getVehicleListVersion"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL("SELECT TOP 1 vehicles FROM go_update"))
                            Result = stmt.ifetch("vehicles");
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("getVehicleListVersion() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    InsertErrorRecord(req.CustCode, "getVehicleListVersion() " + x.Message, req);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "SupportDashboardCheck")]
        public DateTime SupportDashboardCheck(Request req)
        {
            DateTime lastEvent = new DateTime();

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SupportDashboardCheck"))
            {

                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL(String.Format("select top 1 * from go_events order by idate desc")))
                            lastEvent = stmt.dtfetch("idate");
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("SupportDashboardCheck() Failed: " + ex.Message);
                    InsertErrorRecord(req.CustCode, "SupportDashboardCheck() " + ex.Message, req);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return lastEvent;
        }

        private string getBucket(string url)
        {
            string rc = url;
            int l = rc.IndexOf("//");
            if (l >= 0)
            {
                rc = rc.Substring(l + 2);
                l = rc.IndexOf(".");
                if (l > 0)
                    rc = rc.Substring(0, l);
            }
            if (rc.Length > 80)
                rc = rc.Substring(0, 79);
            return rc;
        }

        private PhotoData convertInstallPic(InstalledPictures picturedata)
        {
            PhotoData pData = new PhotoData();
            pData.latitude = (decimal)picturedata.lat;
            pData.longitude = (decimal)picturedata.lon;
            pData.username = picturedata.username;
            pData.repkey  = picturedata.InstallKey;
            pData.handcode = picturedata.handcode;
            pData.unitnum = picturedata.unitNum;
            pData.objKey = picturedata.objKey;
            pData.rdate = picturedata.pictureSent;
            pData.imgUrl = picturedata.imgUrl;
            pData.bucket = getBucket(picturedata.imgUrl);
            return pData;
        }

        private void storePicData(PhotoData pdata, SqlStmt stmt)
        {
            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

            sql.Add("rdate", pdata.rdate);
            // sql.Add("rdate", pdata.imgcreated); // remember to remove if we turn on collision utc
            sql.AddNow("idate");//, DateTime.UtcNow);
            sql.Add("hand_code", pdata.handcode);
            sql.Add("unit_num", pdata.unitnum);
            sql.Add("latitude", pdata.latitude);
            sql.Add("longitude", pdata.longitude);
            sql.Add("degrees", pdata.degrees);
            sql.Add("speed", pdata.speed);
            sql.Add("RepKey", pdata.repkey);
            sql.Add("username", pdata.username);
            sql.Add("img_url", pdata.imgUrl);
            sql.Add("cust_code", pdata.custCode);
            sql.Add("obj_key", pdata.objKey);
            sql.Add("bucket", pdata.bucket);
            sql.Add("gps_accuracy", pdata.gpsAccuracy);
            sql.Add("private", pdata.priv);
            sql.Add("install", pdata.intall);
            sql.Add("installkey", pdata.installKey);
            sql.Add("img_created", pdata.imgcreated); //Now is the image sent field
            sql.Add("sent", pdata.sent);
            sql.Add("sidewalk", pdata.sidewalk);
            sql.Add("deficiency", pdata.deficiency);
            sql.Add("phand_code", pdata.phandcode);
            sql.Add("evt_type", pdata.evtType);
            sql.Add("dash", pdata.dash);
            sql.Add("collision", pdata.collision);
            sql.Add("q_count", pdata.qCount);
            sql.Add("pic_q_count", pdata.picQCount);
            sql.Add("signal_str", pdata.SignalStr);
            sql.Add("net_type", pdata.NetworkType);

            string msg = string.Empty;
            string inner = string.Empty;
            bool excp = false;
            try
            {
                if (!stmt.DoSQLFormat("SELECT id FROM img_events WHERE obj_key='{0}'", pdata.objKey))
                    stmt.DoSQL(sql.BuildCmd("img_events", true));
            }
            catch(Exception ex)
            {
                // some versions of the tablet program take multiple pictures with the same rdate which will cause this error
                excp = (stmt.LastError.Contains("duplicate key") && stmt.LastError.Contains("unique_rdatehc"));
                if(!excp)
                    throw new System.Exception(ex.Message, ex.InnerException);
            }
            if (excp)
            {
                int max = 15;
                do
                {
                    pdata.rdate = pdata.rdate.AddSeconds(1);
                    if (max-- <= 0)
                        break;
                }
                while (stmt.DoSQLFormat("select id from img_events where hand_code={0} and rdate={1}", pdata.handcode, SqlHelper.timeStamp(pdata.rdate)));

                sql.Remove("rdate");
                sql.Add("rdate", pdata.rdate);
                stmt.DoSQL(sql.BuildCmd("img_events", true));
            }
        }

        [WebMethod(Description = "Push Camera Image")]
        public int SendPhotoData(Request req, PhotoData pdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendPhotoData"))
                return -2;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
#if OLDWAY
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    bool reeady = pdata.ready;
                    bool done = pdata.done;
           
                    sql.Add("rdate", pdata.rdate);
                    // sql.Add("rdate", pdata.imgcreated); // remeber to remove if we turn on collision utc
                    sql.Add("idate", DateTime.UtcNow);
                    sql.Add("hand_code", pdata.handcode);
                    sql.Add("unit_num", pdata.unitnum);
                    sql.Add("latitude", pdata.latitude);
                    sql.Add("longitude", pdata.longitude);
                    sql.Add("degrees", pdata.degrees);
                    sql.Add("speed", pdata.speed);
                    sql.Add("RepKey", pdata.repkey);
                    sql.Add("username", pdata.username);
                    sql.Add("img_url", pdata.imgUrl);
                    sql.Add("cust_code", pdata.custCode);
                    sql.Add("obj_key", pdata.objKey);
                    sql.Add("bucket", pdata.bucket);
                    sql.Add("gps_accuracy", pdata.gpsAccuracy);
                    sql.Add("private", pdata.priv);
                    sql.Add("install", pdata.intall);
                    sql.Add("installkey", pdata.installKey);
                    sql.Add("img_created", pdata.imgcreated); //Now is the image sent field
                    sql.Add("sent", pdata.sent);
                    sql.Add("sidewalk", pdata.sidewalk);
                    sql.Add("deficiency", pdata.deficiency);
                    sql.Add("phand_code", pdata.phandcode);
                    sql.Add("evt_type", pdata.evtType);
                    sql.Add("dash", pdata.dash);                 
                    sql.Add("collision", pdata.collision);
                    sql.Add("q_count", pdata.qCount);
                    sql.Add("pic_q_count", pdata.picQCount);
                    sql.Add("signal_str", pdata.SignalStr);
                    sql.Add("net_type", pdata.NetworkType);
              
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO img_Events"));

                    sql.Clear();
#else
                    storePicData(pdata, stmt);
#endif

                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Add("hand_code", pdata.phandcode);
                    sql.Add("truck_id", pdata.unitnum);
                    sql.Add("user_id", pdata.username);
                    sql.Add("signal_str", pdata.SignalStr);
                    sql.Add("net_type", pdata.NetworkType);
                    sql.Add("last_sent", DateTime.UtcNow);
                    sql.Add("q_count", pdata.qCount); // pic WEbService Q count
                    sql.Add("pic_q_count", pdata.picQCount);
                    sql.Add("event_q_count", pdata.eventQCount);

                    if (!stmt.DoSQLFormat("SELECT * FROM go_status WHERE hand_code = {0}", pdata.phandcode))
                    {
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO go_status"));
                    }
                    else
                    {
                        stmt.DoSQLFormat(sql.BuildUpdate("UPDATE go_status with (rowlock)", "WHERE hand_code = {0}"), pdata.phandcode);
                    }

                    Result = 1;
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendPhotoData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendPhotoData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Send Request to Commision Phone for roada App")]
        public int SendCommision(Request req, Comissioninfo comInfo)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendCommision"))
                return -2;

            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt("MainConn");
                stmt.Mess = Global.messLog;
                Result = -3;
                Global.messLog.Flush("Send comssison At select");

                Global.messLog.Flush("SendCommission() Guid {0}", comInfo.IMEI);
                if (!stmt.DoSQL(String.Format("SELECT * FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI)))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.Add("pc_code", comInfo.Owner);
                    sql.Add("client_guid", comInfo.IMEI);
                    sql.Add("description", comInfo.desc);
                    sql.Add("device_hardware", comInfo.hardware);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO soft_activate"));

                }

                stmt.DoSQLFormat("SELECT id FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI);

                Result = stmt.ifetch("id"); //returns the newly created ID for the phones Confiuration  
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendCommission() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendCommission() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }
        public static DateTime fromUTC(DateTime date)
        {
            // Retrieve the time zone for Eastern Standard Time (U.S. and Canada).
            TimeZoneInfo est;
            try
            {
                est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");     // TimeZone.CurrentTimeZone.StandardName);
            }
            catch (TimeZoneNotFoundException)
            {
                Global.messLog.Flush("Unable to retrieve EST time zone data!");
                return date;
            }

            return TimeZoneInfo.ConvertTimeFromUtc(date, est);
        }

        public static DateTime toUTC(DateTime date)
        {
            // Retrieve the time zone for Eastern Standard Time (U.S. and Canada).
            TimeZoneInfo est;
            try
            {
                est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");          // TimeZone.CurrentTimeZone.StandardName);
            }
            catch (TimeZoneNotFoundException)
            {
                Global.messLog.Flush("Unable to retrieve EST time zone data!");
                return date;
            }

            return TimeZoneInfo.ConvertTimeToUtc(date, est);
        }


        [WebMethod(Description = "Send Phone New Route and Operations")]
        public DispatchStatus UpdateDispatch(Request req, DispatchStatus info)
        {
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "UpdateDispatch"))
                return null;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            SqlStmt wstmt = null;
            try
            {
                if (GetStmt(connStr, out stmt) && GetStmt(connStr, out wstmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    bool rc = stmt.DoSQLFormat("SELECT * FROM go_events WHERE truck = '{0}' and comm_type = 1 and isnull(processed,0) = 0 order by event_time desc", info.unitNum);
                    info.routeId = stmt.ifetch("route_id");
                    info.operations = stmt.fetch("operations");
                    info.driver = stmt.fetch("driver");

                    while (rc)
                    {
                        wstmt.DoSQLFormat("UPDATE go_events with (rowlock) SET processed = 1 WHERE id = {0}", stmt.ifetch("id"));
                        rc = stmt.dbSkip();
                    }
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("UpdateDispatch() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "UpdateDispatch() " + x.Message, req);
                info = null;
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
                if (wstmt != null)
                    wstmt.Close();
            }
            return info;
        }

        public int GetDispatch(Request req, string truck, int routeId)
        {
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "GetDispatch"))
                return -1;
            int status = 0;
            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    stmt.DoSQLFormat("SELECT * FROM go_status WHERE truck = '{0}' and comm_type = 1 and isnull(processed,0) = 0 order by event_time desc", truck);

                    if (stmt.ifetch("event_type") == 1 && stmt.ifetch("route_id") > 0)
                    {
                        status = 1;
                    }

                }
            }
            catch (Exception x)
            {

                Global.messLog.Flush("SendImage() Excp: {0}", x.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return status;
        }
        [WebMethod(Description = "Retrieve Hos Events")]
        public HosEvent[] GetHosList(Request request, string user_name, DateTime currentDate)
        {
            HosEvent[] Result = null;

            if (!LowSecurityCheck(request, "GetHosList"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    List<HosEvent> hosevnts = new List<HosEvent>();
                    bool rc;
                    if (user_name != null && currentDate != null)
                        currentDate = currentDate.AddDays(-14);
                    rc = stmt.DoSQLFormat("SELECT * FROM go_hos WHERE user_name = '{0}' AND time_event >= {1} ORDER BY time_event DESC", user_name, SqlHelper.timeStamp(currentDate, SqlHelper.dbType.SqlServer));

                    while (rc)
                    {
                        HosEvent Hev = new HosEvent();
                        Hev.eventTime = stmt.dtfetch("time_event");
                        Hev.code = stmt.ifetch("event_code");
                        Hev.user_name = stmt.fetch("user_name");
                        Hev.duty = stmt.bfetch("duty");
                        Hev.driving = stmt.bfetch("driving");
                        hosevnts.Add(Hev);
                        rc = stmt.dbSkip();
                    }
                    Result = hosevnts.ToArray();
                    hosevnts.Clear();
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetHosEvents() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, "GetHosEvents() " + x.Message, request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }
        [WebMethod(Description = "Hos Events For go app")]
        public int SendHosEvent(Request req, HosEvent evnt)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendHosEvent"))
                return Result;


            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("r_date", now);
                    sql.Add("user_name", evnt.user_name);
                    sql.Add("time_event", evnt.eventTime);
                    sql.Add("event_code", evnt.code);
                    sql.Add("handcode", evnt.handcode);
                    sql.Add("duty", evnt.duty);
                    sql.Add("driving", evnt.driving);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_hos"));

                    Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("HOS Event Message Failed HOS HandCode: {0}", evnt.handcode.ToString());
                InsertErrorRecord(req.CustCode, string.Format("GetHosEvents() Handcode{0} Error{1}", req.Handcode, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Puts installed pictures away")]
        public int sendInstallerPics(Request req, InstalledPictures[] picturedata)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "sendInstallerPics"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    foreach (var data in picturedata)
                    {
                        PhotoData pData = convertInstallPic(data);
                        pData.installKey = data.InstallKey;     // convertInstallPic puts it in repkey
                        pData.repkey = null;
                        pData.custCode = req.CustCode;
                        pData.intall = true;

                        storePicData(pData, stmt);
#if OLDWAY
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        DateTime now = DateTime.Now.ToLocalTime();

                        sql.Add("install", 1);
                        sql.Add("hand_code", data.handcode);
                        sql.Add("unit_num", data.unitNum);
                        sql.Add("obj_key", data.objKey);
                        sql.Add("rdate", data.pictureSent);
                        sql.Add("idate", now);
                        sql.Add("installkey", data.InstallKey);
                        sql.Add("img_url", data.imgUrl);


                        if (!stmt.DoSQLFormat("SELECT id FROM Img_Events WHERE obj_key = '{0}'", data.objKey))
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO Img_Events"));

                        sql.Clear();
#endif
                    }

                    Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("Picture data Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("Picture data Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Puts installed Data Away")]
        public int SendInstallData(Request req, DeviceInfo data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendInstallData"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("comments", data.Comments);
                    sql.Add("intallKey", data.installkey);
                    sql.Add("devID", data.DevId);
                    sql.Add("serverDate", data.ServerDate);
                    sql.Add("writeDate", data.WriteDate);
                    sql.Add("loginDate", data.LoginDate);
                    sql.Add("gpsDate", data.GPSDate);
                    sql.Add("spreadDate", data.SpreadDate);
                    sql.Add("ecmDate", data.ECMDate);
                    sql.Add("lat", data.Latitude);
                    sql.Add("long", data.Longitude);
                    sql.Add("degrees", data.Degrees);
                    sql.Add("speed", data.Speed);
                    sql.Add("firmVers", data.FirmVers);
                    sql.Add("modemVers", data.ModemVers);
                    sql.Add("cellStrength", data.CellStrength);
                    sql.Add("plow", data.Plow);
                    sql.Add("wing", data.Wing);
                    sql.Add("tow", data.Tow);
                    sql.Add("roadTemp", data.RoadTemp);
                    sql.Add("airTemp", data.AirTemp);
                    sql.Add("spreadDry", data.SpreadDry);
                    sql.Add("spreadWet", data.SpreadWet);
                    sql.Add("dryRate", data.DryRate);
                    sql.Add("wetRate", data.WetRate);
                    sql.Add("rpm", data.RPM);
                    sql.Add("coolTemp", data.CoolTemp);
                    sql.Add("oilPress", data.OilPress);

                    if (stmt.DoSQL(String.Format("SELECT * FROM InstallData WHERE intallKey = '{0}'", data.installkey)))
                    {
                        stmt.DoSQL(sql.BuildUpdate("UPDATE InstallData", String.Format("WHERE intallKey = '{0}'", data.installkey)));
                        Result = 1;
                    }
                    else
                    {
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO InstallData"));
                        Result = 2;
                    }

                    //Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("SendInstallData() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("SendInstallData Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }




        [WebMethod(Description = "Puts sidewalk pictures away")]
        public int sendSidewalkPics(Request req, InstalledPictures picturedata)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "sendSidewalkPics"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    PhotoData pData = convertInstallPic(picturedata);
                    pData.custCode = req.CustCode;
                    pData.sidewalk = true;

                    storePicData(pData, stmt);
#if OLDWAY
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("sidewalk", 1);
                    sql.Add("latitude", picturedata.lat);
                    sql.Add("longitude", picturedata.lon);
                    sql.Add("username", picturedata.username);
                    sql.Add("RepKey", picturedata.InstallKey);
                    sql.Add("hand_code", picturedata.handcode);
                    sql.Add("unit_num", picturedata.unitNum);
                    sql.Add("obj_key", picturedata.objKey);
                    sql.Add("rdate", picturedata.pictureSent);
                    sql.Add("idate", now);
                    sql.Add("img_url", picturedata.imgUrl);

                    if (!stmt.DoSQLFormat("SELECT id FROM Img_Events WHERE obj_key = '{0}'", picturedata.objKey))
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO Img_Events"));

                    sql.Clear();
#endif
                    Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("sendSidewalkPics() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("sendSidewalkPics() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Puts sidewalk inspection Data Away")]
        public int SendSidewalkData(Request req, SideWalkData data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendSidewalkData"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {

                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("report_id", data.reportId);
                    sql.Add("hand_code", data.handCode);
                    sql.Add("address", data.address);
                    sql.Add("unit_num", data.UnitNum);
                    sql.Add("user_name", data.userName);
                    sql.Add("colour", data.Colour);
                    sql.Add("report_time", data.reportTime);
                    sql.Add("bays", data.bays);
                    sql.Add("blocks", data.blocks);
                    sql.Add("notes", data.notes);
                    sql.Add("def1", data.def1);
                    sql.Add("def2", data.def2);
                    sql.Add("latitude", data.lat);
                    sql.Add("longitude", data.lon);


                    if (stmt.DoSQL(String.Format("SELECT * FROM sidewalk_report WHERE report_id = '{0}'", data.reportId)))
                    {

                        stmt.DoSQL(sql.BuildUpdate("UPDATE sidewalk_report", String.Format("WHERE report_id = '{0}'", data.reportId)));

                        Result = 1;

                    }
                    else
                    {

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO sidewalk_report"));

                        Result = 2;
                    }

                    Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("sendSidewalkData() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("sendSidewalkData() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        #region Patrol Deficiency
        [WebMethod(Description = "Puts Deficiency pictures away")]
        public int SendDeficiencyPics(Request req, InstalledPictures picturedata)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendDeficiencyPics"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    PhotoData pData = convertInstallPic(picturedata);
                    pData.custCode = req.CustCode;
                    pData.deficiency = true;

                    storePicData(pData, stmt);
#if OLDWAY

                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();

                    sql.Add("deficiency", 1);
                    sql.Add("latitude", picturedata.lat);
                    sql.Add("longitude", picturedata.lon);
                    sql.Add("username", picturedata.username);
                    sql.Add("RepKey", picturedata.InstallKey);
                    sql.Add("hand_code", picturedata.handcode);
                    sql.Add("unit_num", picturedata.unitNum);
                    sql.Add("obj_key", picturedata.objKey);
                    sql.Add("rdate", picturedata.pictureSent);
                    sql.Add("idate", now);
                    sql.Add("img_url", picturedata.imgUrl);

                    if (!stmt.DoSQLFormat("SELECT id FROM Img_Events WHERE obj_key = '{0}'", picturedata.objKey))
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO Img_Events"));

                    sql.Clear();
#endif
                    Result = 1;
                }
            }
            catch
            {
                Result = -3;
                Global.messLog.Flush("Picture data Failed to send");
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        // defines the relationship between DeficiencyData class and deficiencies fields
        // first string is name in class, second is name in database
        string[,] deficiencyLayout = { { "ServerId", "id"},
                                       { "ReportId", "report_id"},
                                       { "RequestId", "RequestId"},
                                       { "HandCode", "hand_code"},
                                       { "UnitNum", "unit_num"},
                                       { "UserName", "user_name"},
                                       { "ReportTime", "report_time"},
                                       { "Type", "type"},
                                       { "Item", "item"},
                                       { "Issue", "issue"},
                                       { "Status", "status"},
                                       { "Length", "length"},
                                       { "Width", "width"},
                                       { "Depth", "depth"},
                                       { "Option", "opt"},
                                       { "Note", "notes" },
                                       { "Lat", "latitude" },
                                       { "Lon", "longitude" },
                                       { "Location", "location_desc"} };

        [WebMethod(Description = "Receive Deficiency Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDeficiencyJson(Request req, DeficiencyData data)
        {
            return SendDeficiencyData(req, data);
        }

        [WebMethod(Description = "Receive Deficiency Data")]
        public int SendDeficiencyData(Request req, DeficiencyData data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendDeficiencyData"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    procData.fillQuery(data, deficiencyLayout, sql);
                    
                    // these are only used when sending data TO tablets and MUST be removed...
                    sql.Remove("id");
                    sql.Remove("requestid");

                    string where = string.Format("WHERE report_id='{0}'", data.ReportId);

                    if (stmt.DoSQL("SELECT id FROM deficiencies " + where))
                    {
                        Result = stmt.ifetch("id");     // return the ID of the existing record...
                        stmt.DoSQL(sql.BuildCmd("deficiencies", false, where));
                    }
                    else
                    {
                        // MUST set second param of DoSQL() to true as this makes it run the insert 
                        // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted id
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO deficiencies", " output inserted.id VALUES "), true);
                        Result = stmt.ifetch("id");     // return the ID of the new record...
                    }
                    // OLD TABLETS EXPECT THIS:  Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("SendDeficiencyData() data failed to receive: " + ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("SendDeficiencyData() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Return Deficiency Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DeficiencyData[] GetDeficiencyJson(Request req, int daysBack)
        {
            return GetDeficiencyData(req, daysBack);
        }

        [WebMethod(Description = "Returns all Deficiency Data since daysBack")]
        public DeficiencyData[] GetDeficiencyData(Request req, int daysBack)
        {
            List<DeficiencyData> result = new List<DeficiencyData>();

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "GetDeficiencyData"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        bool rc = stmt.DoSQLFormat("select * from deficiencies where report_time>getdate()-{0}", daysBack);
                        while (rc)
                        {
                            DeficiencyData defData = new DeficiencyData();
                            procData.fillClass(defData, deficiencyLayout, stmt);
                            result.Add(defData);

                            rc = stmt.dbSkip();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetDeficiencyData() data failed to retrieve: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("GetDeficiencyData() data failed to retrieve HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return result.ToArray();
        }
        #endregion

        [WebMethod(Description = "Comissions Installer app")]
        public int ComissionInstaller(Request req, string imei, out bool commissioned, out string Customers)
        {
            Customers = "";
            commissioned = false;
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "ComissionInstaller"))
                return Result;

            SqlConn mConn = new SqlConn("MainConn");
            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (mConn.dbConnect())
                {
                    stmt = new SqlStmt(mConn);
                    if (!stmt.DoSQL(String.Format("SELECT * FROM soft_activate WHERE client_guid = '{0}'", imei)))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("client_guid", imei);
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO soft_activate"));
                    }

                    stmt.DoSQLFormat("SELECT * FROM soft_activate WHERE client_guid = '{0}'", imei);
                    Result = stmt.ifetch("id"); //returns the newly created ID for the phones Configuration    

                    commissioned = stmt.bfetch("installer");
                    if (commissioned)
                    {
                        Customers = stmt.fetch("db_codes");
                    }

                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("ComissionInstaller() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("ComissionInstaller() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
                Result = -3;
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
                if (mConn != null)
                    mConn.Close();
            }

            return Result;
        }

        private void UpdateGoStatus(evntRecord evnt, Request req)
        {
            SqlStmt stmt = null;
            try
            {
                string connStr = CustConnectionString(req);
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    if (evnt.last_lat != 0) //make sure we dont replave the last loc known with 0
                    {
                        sql.Add("last_lat", evnt.last_lat);
                        sql.Add("last_lon", evnt.last_lon);
                    }
                    sql.Add("memory_remaining", evnt.memory_remaining);
                    sql.Add("battery_level", evnt.BatteryLevel);
                    sql.Add("charging", evnt.Chargeing);
                    sql.Add("logged_in", evnt.LoggedIn_GS);
                    sql.Add("hand_code", evnt.handcode_GS);
                    sql.Add("truck_id", evnt.truck_GS);
                    sql.Add("version", evnt.version_GS);
                    sql.Add("user_id", evnt.driver_GS);
                    sql.Add("last_log", evnt.eventTime);
                    sql.Add("route", evnt.route);
                    sql.Add("pic_q_count", evnt.PicQCount_GS);
                    sql.Add("q_count", evnt.PicQCount_GS);
                    sql.Add("signal_str", evnt.SignalStr);
                    sql.Add("net_type", evnt.net_type_GS);
                    sql.Add("event_q_count", evnt.EventQCount_GS);
                    sql.Add("last_checkin", DateTime.UtcNow);

                    if (stmt.DoSQLFormat("SELECT top 1 idate FROM Img_Events WHERE phand_code = {0} order by idate desc", evnt.handcode)) //gets the last image time that was sent
                    {
                        DateTime lastImg = stmt.dtfetch("idate");
                        sql.Add("last_sent", lastImg);
                    }
                    if (stmt.DoSQLFormat("SELECT * FROM go_status with (rowlock) WHERE hand_code = {0}", evnt.handcode)) //checks to see if there is a record to update
                    {
                        stmt.DoSQLFormat(sql.BuildUpdate("UPDATE go_status with (rowlock)", "WHERE hand_code = {0}"), evnt.handcode);
                    }
                    else
                    {
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO go_status"));
                    }
                }

            }
            catch (Exception ex)
            {
                Global.messLog.Flush("UpdateGoStatus() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

        }
        private void InsertgoEvent(evntRecord evnt, Request req)
        {
            SqlStmt stmt = null;
            try
            {
                string connStr = CustConnectionString(req);
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Add("event_type", evnt.EventType);
                    sql.Add("event_time", evnt.eventTime);
                    sql.Add("operations", evnt.Ops);
                    sql.Add("echelon", evnt.ech);
                    sql.Add("latitude", evnt.lat);
                    sql.Add("longitude", evnt.lon);
                    sql.Add("event_mess", evnt.eventMessage);
                    sql.Add("driver", evnt.driver);
                    sql.Add("truck", evnt.truck);
                    sql.Add("hand_code", evnt.handcode);
                    sql.Add("route", evnt.route);
                    sql.Add("route_id", evnt.routeId);
                    sql.Add("waypoint", evnt.waypoint);
                    sql.Add("processed", 0);
                    sql.Add("comm_type", 0);
                    sql.Add("q_count", evnt.qCount);
                    sql.Add("signal_str", evnt.SignalStr);
                    sql.Add("net_type", evnt.net_type);
                    sql.Add("pic_q_count", evnt.PicQCount);
                    sql.Add("rdate", evnt.rdate);
                    sql.Add("idate", DateTime.UtcNow);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));

                    if (stmt.DoSQLFormat("SELECT * FROM vehicles with (rowlock) WHERE hand_code = {0}", evnt.dcdHC))
                    {
                        if (stmt.dtfetch("last_checkin") < evnt.eventTime)
                        {
                            if (evnt.handcode > 0)
                            {
                                stmt.DoSQLFormat("UPDATE vehicles with (rowlock) SET phone_handcode = null, user_name = null WHERE phone_handcode = {0}", evnt.handcode);
                                stmt.DoSQLFormat("UPDATE vehicles with (rowlock) SET phone_handcode = {0}, user_name = '{1}', last_checkin = '{2}'  WHERE hand_code = {3}", evnt.handcode, SqlHelper.FixUp(evnt.driver), evnt.eventTime, evnt.dcdHC);

                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("InsertgoEvent() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
        }

#if SQL
create table patrol_log
(
id int identity(1,1) primary key,
hand_code int,
unit_num varchar(16),
user_name VARCHAR(32),
start_patrol DATETIME,
start_latitude DECIMAL(10,6),
start_longitude DECIMAL(10,6),
end_patrol DATETIME,
end_latitude DECIMAL(10,6),
end_longitude DECIMAL(10,6)
);
create index unit_num on patrol_log(unit_num);
create index unit_start on patrol_log(unit_num,start_patrol);
#endif

        private void UpdateMessageIn(evntRecord evnt, Request req)
        {
            SqlStmt stmt = null;
            try
            {
                string connStr = CustConnectionString(req);
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    int handcode;

                    sql.Add("rdate", evnt.eventTime);
                    sql.Add("unit_num", evnt.truck);
                    sql.Add("latitude", evnt.lat);
                    sql.Add("longitude", evnt.lon);
                    sql.Add("last_lat", evnt.lat);
                    sql.Add("last_long", evnt.lon);
                    sql.Add("status_v", evnt.handcode);

                    if (stmt.DoSQL(String.Format("SELECT hand_code FROM vehicles WHERE unit_num = '{0}'", evnt.truck)))
                    {
                        handcode = stmt.ifetch("hand_code"); //store it to update
                        sql.Add("hand_code", handcode);
                        stmt.DoSQL(sql.BuildUpdate("UPDATE handheld with (rowlock)", string.Format(" WHERE hand_code = {0}", handcode)));
                    }
                    else
                    {
                        sql.Add("hand_code", stmt.ifetch("hand_code"));
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO handheld"));
                    }


                    sql.Clear();
                    stmt.DoSQL(String.Format("SELECT hand_code FROM vehicles WHERE unit_num = '{0}'", evnt.truck));
                    sql.Add("idate", DateTime.Now);
                    sql.Add("rdate", evnt.eventTime);
                    sql.Add("unit_num", evnt.truck);
                    sql.Add("latitude", evnt.lat);
                    sql.Add("longitude", evnt.lon);
                    sql.Add("speed", evnt.speed);
                    sql.Add("degrees", evnt.heading);
                    sql.Add("status_v", evnt.handcode); //Cell phones hand code use incase vehicle not set and records were sent
                    sql.Add("hand_code", stmt.ifetch("hand_code"));
                    sql.Add("cell_strength", evnt.CStr);
                    sql.Add("network_type", evnt.net_type);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO MessageIn"));
                }

            }
            catch (Exception ex)
            {
                Global.messLog.Flush("UpdateMessageIn() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

        }

        [WebMethod(Description = "Gets The Operation out of bounds events")]
        public evntOut GetEventOps(Request req, string driver, string truck, int DCDHc)
        {
            evntOut result = null;
            SqlStmt stmt = null;

            try
            {
                string connStr = CustConnectionString(req);
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);


                    if (!string.IsNullOrEmpty(truck) && !string.IsNullOrEmpty(driver))
                    {
                        result = new evntOut();
                        DateTime eventthres = new DateTime();
                        eventthres = DateTime.Now.AddDays(-3);
                        //Global.messLog.Flush("event3 date range: {0}", SqlHelper.timeStamp(eventthres));
                        bool rc = stmt.DoSQLFormat("SELECT event_type FROM vehicle_events with (nolock) WHERE driver = '{0}' AND s_date >= {1}", SqlHelper.FixUp(driver), SqlHelper.timeStamp(eventthres));
                        while (rc)
                        {
                            switch (stmt.ifetch("event_type"))
                            {
                                case 10:
                                    result.speedEvents++;
                                    break;
                                case 11:
                                    result.outOfBounds++;
                                    break;
                                case 12:
                                    result.outOfOpp++;
                                    break;
                                default:
                                    break;
                            }
                            rc = stmt.dbSkip();
                        }
                        stmt.Clear();
                        if (DCDHc > 0)
                        {
                            if (stmt.DoSQLFormat("SELECT op_code , route_name FROM dash with (nolock) WHERE hand_code = {0}", DCDHc))
                            {
                                result.plan = stmt.ifetch("op_code");
                                result.routeName = stmt.fetch("route_name");
                            }
                        }
                        else
                        {
                            if (stmt.DoSQLFormat("SELECT op_code , route_name FROM dash with (nolock) WHERE unit_num = '{0}'", DCDHc))
                            {
                                result.plan = stmt.ifetch("op_code");
                                result.routeName = stmt.fetch("route_name");
                            }
                        }
                        stmt.Clear();
                    }
                }

            }
            catch (Exception ex)
            {

                Global.messLog.Flush("GetEventOps() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("GetEventOps() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return result;

        }

        [WebMethod(Description = "Consumes Event records send from Roada Go")]
        public int SendEventRecord(Request req, evntRecord evnt)
        {
            int Result = -1;

            if (evnt == null)
            {
                return -2;
            }

            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendEventRecord"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;

            try
            {

                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    InsertgoEvent(evnt, req); //inserts the Recored to go events and updates the vehicle table if needed
                    Result = 1;
                    if (stmt.DoSQLFormat("SELECT * FROM go_status with (rowlock) WHERE hand_code = {0}", evnt.handcode)) //checks to see if there is a record to update
                    {
                        if (stmt.dtfetch("last_checkin") < evnt.rdate)
                        {
                            UpdateGoStatus(evnt, req); //updates the Go Status Table if needed
                        }
                    }
                    if (evnt.EventType == 9)
                    {
                        UpdateMessageIn(evnt, req); //only does this if phone is acting as DCD (configureable on the DMT manager)
                    }

                }
            }
            catch (Exception x)
            {
                Result = -1;
                Global.messLog.Flush("SendEventRecord() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, string.Format("SendEventRecord() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }


        [WebMethod(Description = "returns the Last time device was logged in")]
        public LogginData GetLoginStatus(Request req, int handcode)
        {
            LogginData data = new LogginData();
            data.online = 0;

            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "GetLoginStatus"))
                return data;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {

                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    stmt.DoSQLFormat("SELECT * FROM go_status WHERE hand_code = '{0}'", handcode);

                    data.online = stmt.ifetch("logged_in");
                    data.truck = stmt.fetch("truck_id");
                    data.user = stmt.fetch("user_id");

                    stmt.DoSQLFormat("SELECT password FROM users WHERE username = '{0}'", data.user);
                    data.password = stmt.fetch("password");
                    Global.messLog.Flush("GetLoginStatus() HC: {0} Cust: {1} Truck: {2} LoggedIn: {3}", handcode, req.CustCode, data.truck, data.online);
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetLoginStatus() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, string.Format("GetLoginStatus() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return data;
        }

        [WebMethod(Description = "Retrieve vehicle list, optionally filtered by fleetId, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Vehicle[] GetVehicleListJson(Request req, int? fleetId)
        {
            // can't do OUT parameters in JSON, as far as I've been able to find...
            int vv = 0;
            return GetVehicleList(req, fleetId, out vv);
        }

        [WebMethod(Description = "Retrieve vehicle listing")]
        public Vehicle[] GetVehicleList(Request request, int? fleetId, out int VehicleVersion)
        {
            Vehicle[] Result = null;
            VehicleVersion = 0;
            if (LowSecurityCheck(request, "GetVehicleList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL("SELECT TOP 1 vehicles FROM go_update"))
                            VehicleVersion = stmt.ifetch("vehicles");

                        List<Vehicle> vehicles = new List<Vehicle>();
                        bool rc;
                        if (fleetId != null)
                            rc = stmt.DoSQLFormat("SELECT * FROM vehicles WHERE fleet = {0} ORDER BY unit_num", fleetId);
                        else
                            rc = stmt.DoSQL("SELECT * FROM vehicles ORDER BY unit_num");
                        while (rc)
                        {
                            Vehicle v = new Vehicle();
                            v.fleetID = stmt.ifetch("fleet");
                            v.DevId = stmt.ifetch("hand_code");
                            v.UnitNum = stmt.fetch("unit_num");
                            v.License = stmt.fetch("license");
                            v.Type = stmt.ifetch("type");
                            vehicles.Add(v);

                            rc = stmt.dbSkip();
                        }
                        Result = vehicles.ToArray();
                        vehicles.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetVehicleList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetVehicleList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve mobile user list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public GoUser[] GetGoUsersJson(Request req)
        {
            // can't do OUT parameters in JSON, as far as I've been able to find...
            int vv = 0;
            return GetGoUsers(req, out vv);
        }

        [WebMethod(Description = "Retrieve Mobile Users listing")]
        public GoUser[] GetGoUsers(Request req, out int userVersion)
        {
            GoUser[] Result = null;
            userVersion = 0;

            if (LowSecurityCheck(req, "GetGoUsers"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQL("SELECT TOP 1 users FROM go_update"))
                            userVersion = stmt.ifetch("users");

                        List<GoUser> users = new List<GoUser>();

                        bool rc = stmt.DoSQLFormat("SELECT * FROM users WHERE mobile_user = {0}", 1);
                        while (rc)
                        {
                            GoUser gu = new GoUser();
                            gu.userRole = stmt.ifetch("mobile_role");
                            gu.username = stmt.fetch("username");
                            gu.password = stmt.fetch("password");
                            gu.email = stmt.fetch("email");
                            gu.firstname = stmt.fetch("first_name");
                            gu.lastname = stmt.fetch("last_name");
                            gu.phonenumber = stmt.fetch("phone_number");
                            gu.title = stmt.fetch("title");
                            users.Add(gu);

                            rc = stmt.dbSkip();
                        }
                        Result = users.ToArray();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetGouserList() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    InsertErrorRecord(req.CustCode, string.Format("GetGouserList() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message), req);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Mobile Users Roles")]
        public MobileRoles[] GetMobileRoles(Request request)
        {
            MobileRoles[] Result = null;

            if (!LowSecurityCheck(request, "GetMobileRoles"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    List<MobileRoles> mrs = new List<MobileRoles>();
                    bool rc;

                    rc = stmt.DoSQL("SELECT * FROM go_user_roles");

                    while (rc)
                    {
                        MobileRoles mr = new MobileRoles();
                        mr.RoleId = stmt.ifetch("id");
                        mr.RoleName = stmt.fetch("role_name");
                        mr.statusPerm = stmt.bfetch("status_perm");
                        mr.Collision = stmt.bfetch("ic_perm");
                        mr.routing = stmt.bfetch("routing_perm");
                        mr.weather = stmt.bfetch("weather_perm");
                        mr.diag = stmt.bfetch("diag_perm");
                        mr.hosPerm = stmt.bfetch("hos_perm");
                        mr.super = stmt.bfetch("super_user");
                        mr.camera = stmt.bfetch("cam_perm");
                        mrs.Add(mr);
                        rc = stmt.dbSkip();
                    }
                    Result = mrs.ToArray();
                    mrs.Clear();
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetMobileRoleList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, string.Format("GetMobileRoleList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve DCs Lat And lons For Phones to see if they are on a trip")]
        public DCenterData[] GetDCLatLongs(Request request)
        {
            DCenterData[] Result = null;

            if (!LowSecurityCheck(request, "GetDCLatLongs"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    List<DCenterData> DCenters = new List<DCenterData>();
                    bool rc;

                    rc = stmt.DoSQLFormat("SELECT * FROM distribution_center WHERE Latitude is not null");

                    while (rc)
                    {
                        DCenterData dc = new DCenterData();
                        dc.ID = stmt.ifetch("id");
                        dc.Name = stmt.fetch("Name");
                        dc.lat = stmt.dfetch("Latitude");
                        dc.lon = stmt.dfetch("Longitude");
                        dc.YardNo = stmt.ifetch("yard_no");
                        DCenters.Add(dc);
                        rc = stmt.dbSkip();
                    }
                    Result = DCenters.ToArray();
                    DCenters.Clear();
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetDCLatLongs() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, string.Format("GetDCLatLongs() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve vehicle types listing")]
        public VehicleType[] GetVehicleTypes(Request request)
        {
            VehicleType[] Result = null;

            if (!LowSecurityCheck(request, "GetVehicleTypes"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    List<VehicleType> vehicleTypes = new List<VehicleType>();
                    bool rc = stmt.DoSQL("SELECT * FROM vehicle_type ORDER BY type");
                    while (rc)
                    {
                        VehicleType v = new VehicleType();
                        v.Type = stmt.ifetch("type");
                        v.HasPlow = stmt.tfetch("has_plow");
                        v.HasSpreader = stmt.tfetch("has_spreader");
                        v.HasTow = stmt.tfetch("has_tow");
                        v.HasWing = stmt.tfetch("has_wing");
                        v.TypeDesc = stmt.fetch("description");
                        vehicleTypes.Add(v);
                        rc = stmt.dbSkip();
                    }
                    Result = vehicleTypes.ToArray();
                    vehicleTypes.Clear();
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetVehicleTypes() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, string.Format("GetVehicleTypes() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve go Alter table statements")]
        public ColumnCheckData[] GetGoAlters(Request request)
        {
            ColumnCheckData[] Result = null;
            if (LowSecurityCheck(request, "GetGoAlters", true))
            {
                //SqlConn mConn = new SqlConn("MainConn");
                SqlStmt stmt = new SqlStmt("MainConn");
                try
                {

                    List<ColumnCheckData> createStmts = new List<ColumnCheckData>();
                    bool rc = stmt.DoSQL("SELECT * FROM roadaAltertables");
                    while (rc)
                    {
                        ColumnCheckData cs = new ColumnCheckData();
                        cs.idField = stmt.ifetch("id");
                        cs.SqlStr = stmt.fetch("sqlstring");
                        cs.TableName = stmt.fetch("tablename");
                        cs.ColumnName = stmt.fetch("columnname");

                        createStmts.Add(cs);
                        rc = stmt.dbSkip();
                    }
                    Result = createStmts.ToArray();
                    createStmts.Clear();
                }

                catch (Exception x)
                {
                    Global.messLog.Flush("GetGoAlters() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetGoAlters() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve go create table statements")]
        public goTables[] GetGoTables(Request request)
        {
            goTables[] Result = null;
            if (LowSecurityCheck(request, "GetGoTables", true))
            {
                SqlStmt stmt = new SqlStmt("MainConn");
                try
                {
                    List<goTables> createStmts = new List<goTables>();
                    bool rc = stmt.DoSQL("SELECT * FROM go_create_tables");
                    while (rc)
                    {
                        goTables cs = new goTables();
                        cs.id = stmt.ifetch("id");
                        cs.sqlStatement = stmt.fetch("sqlstring");

                        createStmts.Add(cs);
                        rc = stmt.dbSkip();
                    }
                    Result = createStmts.ToArray();
                    createStmts.Clear();
                }

                catch (Exception x)
                {
                    Global.messLog.Flush("GetGoTables() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetGoTables() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        // defines the relationship between LoginData class and patrol_log fields
        // first string is name in class, second is name in database
        string[,] loginLayout = { { "HandCode", "hand_code" },
                                  { "UnitNum", "unit_num" },
                                  { "UserName", "user_name" },
                                  { "StartTime", "start_patrol" },
                                  { "StartLatitude", "start_latitude" },
                                  { "StartLongitude", "start_longitude" },
                                  { "EndTime", "end_patrol" },
                                  { "EndLatitude", "end_latitude" },
                                  { "EndLongitude", "end_longitude" } };

        [WebMethod(Description = "Receive Login Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendTabletLoginJson(Request req, LoginData data)
        {
            return SendTabletLogin(req, data);
        }

        [WebMethod(Description = "Receive Login Data from Tablet")]
        public int SendTabletLogin(Request req, LoginData data)
        {
            int rc = 0;

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SendTabletLogin"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    rc = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        procData.fillQuery(data, loginLayout, sql);

                        string where = string.Format("WHERE hand_code={0} and start_patrol={1}", data.HandCode, SqlHelper.timeStamp(data.StartTime));

                        if (stmt.DoSQL("SELECT id FROM patrol_log " + where))
                        {
                            rc = stmt.ifetch("id");     // return the ID of the existing record...
                            stmt.DoSQL(sql.BuildCmd("patrol_log", false, where));
                        }
                        else
                        {
                            //Global.messLog.Flush("SendLoginData WHERE: " + where);
                            //if (stmt.LastError != string.Empty)
                            //    Global.messLog.Flush("SendLoginData ERROR: " + stmt.LastError);

                            // MUST set second param of DoSQL() to true as this makes it run the insert 
                            // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted id
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO patrol_log", " output inserted.id VALUES "), true);
                            rc = stmt.ifetch("id");     // return the ID of the new record...
                        }
                    }
                }
                catch (Exception ex)
                {
                    rc = -3;
                    Global.messLog.Flush("SendTabletLogin() data failed to receive: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("SendTabletLogin() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
                }
                finally
                {
                    if(stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }

            }
            return rc;
        }

#if SQL
create table tablet_sql
(
id int identity(1,1) primary key,
sqlstring varchar(max),
tablename varchar(128),
indexname varchar(128),
columnname varchar(128)
)
#endif
        [WebMethod(Description = "Retrieve all create/alter table / index statements for the RoadaTabPatrol app")]
        public tabletTables[] setupTabletDB(Request request)
        {
            tabletTables[] Result = null;
            if (LowSecurityCheck(request, "setupTabletDB", true))
            {
                SqlStmt stmt = new SqlStmt("MainConn");
                try
                {
                    List<tabletTables> sqlCmds = new List<tabletTables>();
                    bool rc = stmt.DoSQL("SELECT * FROM tablet_sql");
                    while (rc)
                    {
                        tabletTables tb = new tabletTables();
                        tb.sqlCmd = stmt.fetch("sqlstring");
                        tb.tablename = stmt.fetch("tablename");
                        tb.indexname = stmt.fetch("indexname");
                        tb.columnname = stmt.fetch("columnname");
                        sqlCmds.Add(tb);

                        rc = stmt.dbSkip();
                    }

                    Result = sqlCmds.ToArray();
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("setupTabletDB() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, string.Format("setupTabletDB() Failed sending HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message), request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                    stmt = null;
                }
            }
            return Result;
        }

        [WebMethod(Description = "Prepare Frequency Page")]
        public string setupFreqPage(Request req, string data, string mdata)
        {
            string Result = string.Empty;
            if (LowSecurityCheck(req, "setupFreqPage"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        Result = Guid.NewGuid().ToString();

                        SqlHelper query = new SqlHelper();
                        query.Add("lookup", Result);
                        query.Add("name", data);
                        query.Add("dkey", mdata);
                        query.AddNow("start");

                        stmt.DoSQL(query.BuildCmd("loguser", true));
                    }
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        private bool GetStmt(string connStr, out SqlStmt stmt)
        {
            stmt = new SqlStmt();
            stmt.Mess = Global.messLog;
            stmt.ConnectionData = connStr;
            bool Result = stmt.dbConnect();
            if (!Result)
                Global.messLog.Flush("Failed to connect to {0}", connStr);
            return Result;
        }

        [WebMethod(Description = "Retrieve current GPS readings")]
        public GPSData[] GPSDataRequest(Request request, bool firstFlag, int devId, string unitNum)
        {
            GPSData[] Result = null;

            if (!LowSecurityCheck(request, "GPSDataRequest"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    if (unitNum != string.Empty)
                    {
                        if (stmt.DoSQLFormat("SELECT hand_code FROM vehicles WHERE unit_num = '{0}'", stmt.FixQuote(unitNum)))
                            devId = stmt.ifetch("hand_code");
                    }
                    if (stmt.DoSQLFormat("SELECT * FROM handheld WHERE hand_code = {0}", devId))
                    {
                        DateTime lastDate = stmt.dtfetch("last_date");
                        if (firstFlag)
                            lastDate = lastDate.AddHours(-12.0);
                        bool rc = stmt.DoSQLFormat("SELECT TOP 6 * FROM messagein WHERE hand_code = {0} and rdate > {{ts '{1}'}} ORDER BY rdate DESC", devId, lastDate.ToString(LONG_DATE));
                        if (rc)
                        {
                            List<GPSData> gpsdata = new List<GPSData>();
                            while (rc)
                            {
                                GPSData g = new GPSData();
                                g.DevId = devId;
                                g.UTCDate = stmt.dtfetch("rdate");
                                g.SOG = stmt.ifetch("speed");
                                g.COG = stmt.ifetch("degrees");
                                g.Latitude = stmt.dfetch("latitude");
                                g.Longitude = stmt.dfetch("longitude");
                                g.PlowInp = stmt.tfetch("plow");
                                g.WingInp = stmt.tfetch("wing");
                                g.TowInp = stmt.tfetch("tow");
                                g.TempAir = stmt.dfetch("temp_air");
                                g.TempSurface = stmt.dfetch("temp_road");
                                g.CellStrength = stmt.ifetch("cell_string");
                                g.IDate = stmt.dtfetch("idate");
                                gpsdata.Add(g);

                                rc = stmt.dbSkip();
                            }
                            Result = gpsdata.ToArray();
                            gpsdata.Clear();
                        }
                    }
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GPSDataRequest() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, string.Format("GPSDataRequest() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Retrieves replication data from the server")]
        public ReplicationData[] GetReplicationDataX(Request request,
                                                      string dbCode, string region, int histMax, int startRecord)
        {
            return GetReplicationDataY(request, dbCode, region, histMax, startRecord, 0);
        }

        [WebMethod(Description = "Retrieves replication data from the server")]
        public ReplicationData[] GetReplicationDataY(Request request,
                                                      string dbCode, string region, int histMax, int startRecord, int limit)
        {
            /* Note:    For DB that have multiple regions (and a lot of data across them), we can narrow what we
             *          replicate by "region" (see 'yard' table, 'name' field).  As well, should there be a lot
             *          of data in a database, and we only want more recent material, we can limit by how many
             *          months maximum (in "histMax") we want (value of -1 will have this parameter ignored).
             */
            ReplicationData[] Result = new ReplicationData[0];
            if (LowSecurityCheck(request, "GetReplicationDataY"))
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
                        CheckSQLForRegion xRegion = null;                       // New mode added by KRS 8/27/2012.
                        try
                        {
                            if (cConn.dbConnect())
                            {
                                stmt = new SqlStmt(cConn);

                                DateTime max = DateTime.MinValue;

                                if (histMax > -1)
                                {
                                    if (stmt.DoSQL("SELECT GETDATE() AS date"))
                                    {
                                        max = stmt.dtfetch("date");

                                        max = max.AddMonths(0 - histMax);
                                    }
                                    if (max < new DateTime(2000, 1, 1))
                                        histMax = -1;       // Turn data sync maximum off ...
                                }

                                if (region != string.Empty)                     // New mode added by KRS 8/27/2012.
                                {
                                    xRegion = new CheckSQLForRegion();

                                    xRegion.PreloadRegion(stmt, region);
                                }

                                int count = 0;

                                repList = new List<ReplicationData>();

                                string sql = "SELECT ";
                                if (limit > 0)      // added a limit
                                    sql += string.Format("TOP {0} ", limit);
                                sql += string.Format("* FROM datarep WHERE record_id > {0} and client_id != {1}",
                                                        startRecord, request.CustId);
                                if (histMax > -1)
                                    sql += string.Format(" AND idate >= '{0}'", max.ToString("yyyy-MM-dd"));
                                sql += " ORDER BY record_id";
                                bool rc = stmt.DoSQL(sql);
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

                                    if (region != string.Empty)                 // New mode added by KRS 8/27/2012.
                                        xRegion.CheckRegion(region, ref r);

                                    if (r != null)
                                    {
                                        repList.Add(r);

                                        count++;

                                        if (histMax > -1 || region != string.Empty)
                                            if (count >= MaxDownloadBatchSize)
                                            {
                                                Global.messLog.Flush(string.Format("GetReplication hit the Maximum download Batch Size of {0} records; please sync the client again to get the rest.", MaxDownloadBatchSize));
                                                break;
                                            }
                                    }

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
                            if (xRegion != null)
                                xRegion.Clear();
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

    public class MQueue
    {
        public string CustCode;
        public string ConnectionString;
        public string UserName;
        public string Password;
        public string QName;
        public string Name;
        public Message MyMessage;
        public MessageQueueTransaction MessageTx;
        public MessageQueue MessageQ;
    }
}
