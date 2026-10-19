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
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections;

using Newtonsoft.Json;

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
                g.DevId = myConvert.ToInt(data.UnitID);
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
                        sql.Add("ac_snow_accum", patrol.ACSnowAccum);
                        sql.Add("ac_swsnow_accum", patrol.ACSwSnowAccum);
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

        [WebMethod(Description = "Store signature data.")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendSignatureDataJson(Request req, Employee emp, Sigdata sig)
        {
            return SendSignatureData(req, emp, sig);
        }

        [WebMethod(Description = "Store signature data.")]
        public int SendSignatureData(Request req, Employee emp, Sigdata sig)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendSignatureData"))
                return -2;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Add("employee_id", emp.Id);
                    sql.Add("employee_num", emp.employee_num);
                    sql.Add("first_name", emp.first_name);
                    sql.Add("last_name", emp.last_name);
                    sql.Add("dc_id", emp.dc_id);
                    sql.Add("datetime", sig.datetime);
                    sql.Add("signature", sig.signature);//.SelectMany(BitConverter.GetBytes).ToArray());

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO signature_data"));

                    Result = 1;
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendSignatureData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendSignatureData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve employee list, optionally filtered by cd_id, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Employee[] GetEmployeeListJson(Request req, int? dc_id)
        {
            return GetEmployeeList(req, dc_id);
        }

        [WebMethod(Description = "Retrieve employee listing")]
        public Employee[] GetEmployeeList(Request request, int? dc_id)
        {
            Employee[] Result = null;
            if (LowSecurityCheck(request, "GetEmployeeList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Employee> employees = new List<Employee>();
                        bool rc;
                        if (dc_id != null)
                            rc = stmt.DoSQLFormat("SELECT * FROM employee WHERE dc_id = {0} ORDER BY first_name ASC, last_name ASC", dc_id);
                        else
                            rc = stmt.DoSQL("SELECT * FROM employee ORDER BY first_name ASC, last_name ASC");
                        while (rc)
                        {
                            Employee e = new Employee();
                            e.Id = stmt.ifetch("id");
                            e.employee_num = stmt.fetch("employee_num");
                            e.first_name = stmt.fetch("first_name");
                            e.last_name = stmt.fetch("last_name");
                            e.dc_id = stmt.ifetch("dc_id");

                            employees.Add(e);

                            rc = stmt.dbSkip();
                        }
                        Result = employees.ToArray();
                        employees.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetEmployeeList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetEmployeeList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve only open issue tracking tickets as json")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public IssueInfo[] GetOpenIssuesJson(Request req)
        {
            return GetIssues(req, true, DateTime.MinValue);
        }

        [WebMethod(Description = "Retrieve any issue tracking tickets from > sdate as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public IssueInfo[] GetAllIssuesJson(Request req, DateTime sdate)
        {
            return GetIssues(req, false, sdate);
        }

        [WebMethod(Description = "Retrieve a single issue by id as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public IssueInfo GetIssueJson(Request req, int issueId)
        {
            IssueInfo Result = null;
            if (LowSecurityCheck(req, "GetIssueJson"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                SqlStmt stmt3 = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        GetStmt(connStr, out stmt2);
                        GetStmt(connStr, out stmt3);
                        if (stmt.DoSQLFormat("SELECT * FROM issue_tickets WHERE id = {0}", issueId))
                            Result = GetIssue(stmt, stmt2, stmt3);
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetIssueJson() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                    if (stmt2 != null)
                        stmt.Close();
                    if (stmt3 != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        private IssueInfo GetIssue(SqlStmt stmt, SqlStmt stmt2, SqlStmt stmt3)
        {
            IssueInfo info = new IssueInfo();
            info.IssueId = stmt.ifetch("id");
            info.DevId = stmt.ifetch("hand_code");
            info.UnitNum = stmt.fetch("unit_num");
            info.IssueDate = stmt.dtfetch("issue_date");
            info.ClosedFlag = stmt.tfetch("closed_flag");
            info.WhoClosed = stmt.fetch("who_closed");
            info.WhoCreated = stmt.fetch("who_created");
            info.ReasonCode = stmt.ifetch("reason_code");
            info.OtherReason = stmt.fetch("other_reason");
            info.Comments = stmt.fetch("comments");
            info.RepKey = stmt.fetch("repkey");

            int id = stmt.ifetch("id");
            List<ImgInfo> images = new List<ImgInfo>();
            bool rc2;
            if (info.RepKey.Length > 0)
            {
                rc2 = stmt2.DoSQLFormat("SELECT * FROM Img_Events WHERE repkey = '{0}'", info.RepKey);
                while (rc2)
                {
                    ImgInfo img = new ImgInfo();
                    img.EvtType = stmt2.ifetch("evt_type");
                    img.rdate = stmt2.dtfetch("rdate");
                    img.ImgUrl = stmt2.fetch("img_url");
                    images.Add(img);
                    rc2 = stmt2.dbSkip();
                }
            }
            info.Images = images.ToArray();
            images.Clear();
            rc2 = stmt2.DoSQLFormat("SELECT * FROM issue_sched WHERE issue_tick_id = {0} ORDER BY id", id);
            List<IssueSched> scheds = new List<IssueSched>();
            while (rc2)
            {
                int sid = stmt2.ifetch("id");
                IssueSched sched = new IssueSched();
                sched.IssueId = id;
                sched.Id = sid;
                sched.SchedDate = stmt2.dtfetch("sched_date");
                sched.WhoCreated = stmt2.fetch("who_created");
                sched.CancelFlag = stmt2.tfetch("cancel_flag");
                sched.CancelDate = stmt2.dtfetch("cancel_date");
                sched.CancelReason = stmt2.fetch("cancel_reason");
                sched.Comments = stmt2.fetch("comments");
                sched.SchedTech = stmt2.fetch("sched_tech");
                bool rc3 = stmt3.DoSQLFormat("SELECT * FROM issue_item WHERE issue_tick_id = {0} and issue_sched_id = {1} ORDER BY id", id, sid);
                List<IssueItem> items = new List<IssueItem>();
                while (rc3)
                {
                    IssueItem item = new IssueItem();
                    item.IssueId = id;
                    item.SchedId = sid;
                    item.TimeStart = stmt3.dtfetch("time_start");
                    item.WhoCreated = stmt3.fetch("who_created");
                    item.Technician = stmt3.fetch("technician");
                    item.HrsOnSite = stmt3.dfetch("hrs_onsite");
                    item.TravelKm = stmt3.ifetch("travel_km");
                    item.Comments = stmt3.fetch("comments");
                    items.Add(item);
                    rc3 = stmt3.dbSkip();
                }
                sched.Items = items.ToArray();
                items.Clear();
                scheds.Add(sched);
                rc2 = stmt2.dbSkip();
            }
            info.Scheduled = scheds.ToArray();
            scheds.Clear();
            return info;
        }

        [WebMethod(Description = "Retrieve issue tracking system tickets")]
        public IssueInfo[] GetIssues(Request request, bool open, DateTime sdate)
        {
            IssueInfo[] Result = null;
            if (LowSecurityCheck(request, "GetOpenIssues"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                SqlStmt stmt3 = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        GetStmt(connStr, out stmt2);
                        GetStmt(connStr, out stmt3);
                        List<IssueInfo> issues = new List<IssueInfo>();
                        bool rc;
                        if (open)
                            rc = stmt.DoSQL("SELECT * FROM issue_tickets WHERE isnull(closed_flag, 0) = 0 ORDER BY id");
                        else
                            rc = stmt.DoSQLFormat("SELECT * FROM issue_tickets WHERE issue_date >= {0} ORDER BY id", SqlHelper.timeStamp(sdate));
                        while (rc)
                        {
                            IssueInfo info = GetIssue(stmt, stmt2, stmt3);
                            issues.Add(info);

                            rc = stmt.dbSkip();
                        }
                        Result = issues.ToArray();
                        issues.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetOpenIssues() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetOpenIssues() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                    if (stmt2 != null)
                        stmt.Close();
                    if (stmt3 != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve issue reasons as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public IssueReason[] GetIssueReasonsJson(Request req)
        {
            return GetIssueReasons(req);
        }


        [WebMethod(Description = "Retrieve issue tracking reason codes")]
        public IssueReason[] GetIssueReasons(Request request)
        {
            IssueReason[] Result = null;
            if (LowSecurityCheck(request, "GetIssueReasons"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<IssueReason> reasons = new List<IssueReason>();
                        bool rc = stmt.DoSQL("SELECT * FROM reasons ORDER BY code");
                        while (rc)
                        {
                            IssueReason r = new IssueReason();
                            r.code = stmt.ifetch("code");
                            r.label = stmt.fetch("reason_text");
                            r.value = stmt.fetch("reason_text");
                            reasons.Add(r);
                            rc = stmt.dbSkip();
                        }
                        Result = reasons.ToArray();
                        reasons.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetIssueReasons() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetIssueReaons() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }
        [WebMethod(Description = "Create an issue tracking system item JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int CloseIssueTicketJson(Request req, IssueInfo info)
        {
            int Result = -9;
            if (LowSecurityCheck(req, "CloseIssueTicket"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("closed_flag", true);
                        sql.AddNow("closed_date");
                        sql.Add("who_closed", info.WhoClosed);
                        stmt.DoSQL(sql.BuildUpdate("UPDATE issue_tickets", " WHERE id = " + info.IssueId.ToString()));
                        if (stmt.LastError == string.Empty)
                            Result = 0;
                        else
                            Result = -3;
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("CloseIssueTicket() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    Result = -4;
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;

        }



        [WebMethod(Description = "Create an Issue Ticket as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int CreateIssueTicketJson(Request req, IssueInfo info)
        {
            return CreateIssueTicket(req, info);
        }

        [WebMethod(Description = "Create issue tracking system ticket")]
        public int CreateIssueTicket(Request request, IssueInfo info)
        {
            int Result = -9;
            if (LowSecurityCheck(request, "CreateIssueTicket"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        stmt.Conn.ignoreDupErr = true;
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("hand_code", info.DevId);
                        sql.Add("unit_num", info.UnitNum);
                        sql.Add("reason_code", info.ReasonCode);
                        sql.Add("other_reason", info.OtherReason);
                        stmt.Add("@comments", info.Comments);
                        sql.Add("comments", "@comments", false);
                        int id;
                        if (info.IssueId > 0)
                        {
                            stmt.DoSQL(sql.BuildUpdate("UPDATE issue_tickets", " WHERE id = " + info.IssueId.ToString()));
                            id = info.IssueId;
                        }
                        else
                        {
                            sql.Add("issue_date", info.IssueDate);
                            sql.Add("who_created", info.WhoCreated);
                            sql.Add("repkey", info.RepKey);
                            sql.AddNow("idate");
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO issue_tickets", " output inserted.id VALUES "), true);
                            id = stmt.ifetch("id");
                        }
                        if (stmt.LastError.Contains("duplicate"))
                            Result = -2;
                        else if (stmt.LastError == string.Empty)
                            Result = id;
                        else
                            Result = -3;
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("CreateIssueTicket() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("CreateIssueTicket() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                    Result = -4;
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Create issue tracking system schedule record JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int CreateIssueSchedJson(Request req, IssueSched sched)
        {
            return CreateIssueSched(req, sched);
        }

        [WebMethod(Description = "Create issue tracking system schedule record")]
        public int CreateIssueSched(Request request, IssueSched sched)
        {
            int Result = -9;
            if (LowSecurityCheck(request, "CreateIssueSched"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        stmt.Conn.ignoreDupErr = true;
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("issue_tick_id", sched.IssueId);
                        sql.Add("sched_date", sched.SchedDate);
                        sql.AddNow("idate");
                        sql.Add("who_created", sched.WhoCreated);
                        sql.Add("cancel_flag", sched.CancelFlag);
                        sql.Add("cancel_date", sched.CancelDate);
                        sql.Add("cancel_reason", sched.CancelReason);
                        stmt.Add("comments", sched.Comments);
                        sql.Add("comments", "@comments", false);
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO issue_sched", " output inserted.id VALUES "), true);
                        if (stmt.LastError.Contains("duplicate"))
                            Result = -2;
                        else if (stmt.LastError == string.Empty)
                            Result = stmt.ifetch("id");
                        else
                            Result = -3;
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("CreateIssueSched() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("CreateIssueSched() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                    Result = -4;
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Update issue tracking system schedule record JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int UpdateIssueSchedJson(Request req, IssueSched sched)
        {
            return UpdateIssueSched(req, sched);
        }

        [WebMethod(Description = "Update issue tracking system schedule record")]
        public int UpdateIssueSched(Request request, IssueSched sched)
        {
            int Result = -9;
            if (LowSecurityCheck(request, "UpdateIssueSched"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        stmt.Conn.ignoreDupErr = true;
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("cancel_flag", sched.CancelFlag);
                        sql.Add("cancel_date", sched.CancelDate);
                        sql.Add("cancel_reason", sched.CancelReason);
                        stmt.Add("comments", sched.Comments);
                        sql.Add("comments", "@comments", false);
                        stmt.DoSQL(sql.BuildUpdate("UPDATE issue_sched", string.Format(" WHERE issue_tick_id = {0} and sched_date = {1}", sched.IssueId, SqlHelper.timeStamp(sched.SchedDate))));
                        if (stmt.LastError == string.Empty)
                            Result = 0;
                        else
                            Result = -3;
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("UpdateIssueSched() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("UpdateIssueSched() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                    Result = -4;
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Create an issue tracking system item JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int CreateIssueItemJson(Request req, IssueItem item)
        {
            return CreateIssueItem(req, item);
        }

        [WebMethod(Description = "Create an issue tracking system item record")]
        public int CreateIssueItem(Request request, IssueItem item)
        {
            int Result = -9;
            if (LowSecurityCheck(request, "CreateIssueItem"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("issue_tick_id", item.IssueId);
                        sql.Add("issue_sched_id", item.SchedId);
                        sql.Add("time_start", item.TimeStart);
                        sql.AddNow("idate");
                        sql.Add("who_created", item.WhoCreated);
                        sql.Add("hrs_onsite", item.HrsOnSite);
                        sql.Add("technician", item.Technician);
                        sql.Add("travel_km", item.TravelKm);
                        stmt.Add("comments", item.Comments);
                        sql.Add("comments", "@comments", false);
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO issue_item"));
                        if (stmt.LastError == string.Empty)
                            Result = 0;
                        else
                            Result = -3;
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("CreateIssueItem() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("CreateIssueItem() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                    Result = -4;
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }


        [WebMethod(Description = "Update an issue tracking system item JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int UpdateIssueItemJson(Request req, IssueItem item)
        {
            return UpdateIssueItem(req, item);
        }

        [WebMethod(Description = "Update an issue tracking system item record")]
        public int UpdateIssueItem(Request request, IssueItem item)
        {
            int Result = -9;
            if (LowSecurityCheck(request, "UpdateIssueItem"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        if (item.TimeStart != null)
                            sql.Add("time_start", item.TimeStart);
                        sql.AddNow("idate");
                        if (item.HrsOnSite > 0)
                            sql.Add("hrs_onsite", item.HrsOnSite);
                        if (item.Technician != null)
                            sql.Add("technician", item.Technician);
                        if (item.TravelKm > 0)
                            sql.Add("travel_km", item.TravelKm);
                        if (item.Comments != null)
                        {
                            stmt.Add("comments", item.Comments);
                            sql.Add("comments", "@comments", false);
                        }
                        stmt.DoSQL(sql.BuildUpdate("UPDATE issue_item", string.Format("WHERE issue_tick_id = {0} and issue_sched_id = {1}", item.IssueId, item.SchedId)));
                        if (stmt.LastError == string.Empty)
                            Result = 0;
                        else
                            Result = -3;
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("UpdateIssueItem() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("UpdateIssueItem() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                    Result = -4;
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve list of yards as JSON filtered by roles for a user (optional)")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Yard[] GetYardListJsonRole(Request req, string username)
        {
            List<Yard> dcs = new List<Yard>(RequestYards(req));
            if (dcs.Count > 0)
            {
                // if there are 2 fleet records pointing to same dc record you end up with dups, this removes them
                dcs = dcs.Distinct(new YardEqualityComparer()).ToList();
                if (username != null && username.Length > 0 && username.ToLower() != "dmtadm")
                {
                    Role usersRoles = new Role();
                    usersRoles.username = username;

                    string connStr = CustConnectionString(req);
                    SqlStmt stmt = null;
                    try
                    {
                        if (GetStmt(connStr, out stmt))
                        {
                            getRoleResources(stmt, usersRoles);
                            for (int i = 0; i < dcs.Count; i++)
                                if (!usersRoles.DClist.Contains(dcs[i].Dc_Id.ToString()))
                                    dcs.RemoveAt(i--);
                        }
                    }
                    catch (Exception x)
                    {
                        Global.messLog.Flush("GetYardListJsonRole() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                        InsertErrorRecord(req.CustCode, string.Format("GetYardListJsonRole() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message), req);
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
            }

            return dcs.ToArray();
        }

        [WebMethod(Description = "Retrieve list of yards as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Yard[] RequestYardsJson(Request req)
        {
            return RequestYards(req);
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
                            y.Id = stmt.ifetch("id");
                            y.Name = stmt.fetch("name");
                            y.Address = stmt.fetch("address");
                            y.Province = stmt.fetch("province");
                            y.Postal = stmt.fetch("postal");
                            y.Phone = stmt.fetch("phone");
                            y.Latitude = stmt.dfetch("latitude");
                            y.Longitude = stmt.dfetch("longitude");
                            y.GEOFence = stmt.fetch("geofence_locations");
                            y.BrineDensity = stmt.dfetch("brine_density");
                            y.PerSodium = stmt.dfetch("per_sodium");
                            y.Visible = stmt.tfetch("visible");
                            y.FleetId = stmt.ifetch("fid");
                            y.Dc_Id = stmt.ifetch("id");
                            y.ContractId = stmt.ifetch("yard_no");

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

        [WebMethod(Description = "Get server info JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public AppServerInfo GetServerInfoJson(Request req)
        {
            AppServerInfo Result = null;
            if (GetServerInfo(req, out Result) == 1)
                return Result;
            else
                return null;
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
                    ServerInfo.Name = stmt.fetch("name");
                    ServerInfo.SidewalkConditions = stmt.ifetch("sidewalk_cond");
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
        // DEPRECATED
        // GetDVIRJson is deprecated and will be removed soon. Use GetDVIRReportJSON instead
        [WebMethod(Description = "DEPRECATED Retrieve DVIR Data from React App JSON using vehicle id for")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DVIRHeader[] GetDVIRJson(Request req, int veh, int rows)
        {
            DVIRHeader[] Result = null;
            if (LowSecurityCheck(req, "GetDVIRJson"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        GetStmt(connStr, out stmt2);
                        List<DVIRHeader> dvirs = new List<DVIRHeader>();
                        bool rc = stmt.DoSQLFormat("SELECT TOP {0} * FROM dvirheader WHERE vehicle_id = {1} ORDER BY rdate DESC", rows, veh);
                        int hdrId;
                        while (rc)
                        {
                            hdrId = stmt.ifetch("id");
                            DVIRHeader dh = new DVIRHeader();
                            dh.Company = stmt.fetch("company");
                            dh.TractorPlate = stmt.fetch("tractor_plate");
                            dh.Odom = stmt.dfetch("odom");
                            dh.rdate = stmt.dtfetch("rdate");
                            dh.lat = stmt.dfetch("lat");
                            dh.lon = stmt.dfetch("lon");
                            dh.Inspector = stmt.fetch("inspector");
                            dh.Comments = stmt.fetch("comments");
                            dh.CommId = stmt.ifetch("comm_id");
                            dh.RepKey = stmt.fetch("RepKey");
                            dh.done = stmt.tfetch("done");

                            bool rc2 = stmt2.DoSQLFormat("SELECT * FROM dvirdetail WHERE header_id = {0}", hdrId);
                            List<DVIRDetail> detList = new List<DVIRDetail>();
                            while (rc2)
                            {
                                DVIRDetail dd = new DVIRDetail();
                                dd.CategoryId = stmt2.ifetch("category_id");
                                if (!stmt2.IsNull("minor_defects"))
                                {
                                    List<int> mdInts = stmt2.lifetch("minor_defects");
                                    if (mdInts.Count > 0)
                                        dd.MinorDefects = mdInts.ToArray();
                                }
                                if (!stmt2.IsNull("major_defects"))
                                {
                                    List<int> mdInts = stmt2.lifetch("major_defects");
                                    if (mdInts.Count > 0)
                                        dd.MajorDefects = mdInts.ToArray();
                                }
                                detList.Add(dd);
                                rc2 = stmt2.dbSkip();
                            }
                            dh.Details = detList.ToArray();
                            dvirs.Add(dh);
                            rc = stmt.dbSkip();
                        }
                        Result = dvirs.ToArray();
                        dvirs.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetDVIRJson() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                    InsertErrorRecord(req.CustCode, "GetDVIRJson() " + ex.Message, req);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                    if (stmt2 != null)
                        stmt2.Close();
                }
            }
            return Result;
        }

        // DEPRECATED
        // SaveDVIRJson is deprecated and will be removed soon. Use SaveDVIRReportJson instead
        [WebMethod(Description = "DEPRECATED Save DVIR Data from React App JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SaveDVIRJson(Request req, DVIRHeader hdr)
        {
            int rc = -4;

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SaveDVIRJson"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    rc = -2;
                    if (GetStmt(connStr, out stmt))
                    {

                        if (stmt.DoSQL(string.Format("SELECT id FROM DVIRHeader Where RepKey = '{0}'", hdr.RepKey)))
                        {
                            stmt.DoSQL(string.Format("UPDATE DVIRHeader SET done = 1 WHERE RepKey = '{0}'", hdr.RepKey));
                        }
                        else
                        {
                            SqlHelper sql = procData.fillQuery(hdr, dvirHeaderLayout);
                            if (!string.IsNullOrEmpty(hdr.sigBase64))
                                sql.Add("signature", myConvert.ToByteArray(base64SigToBmp(hdr.sigBase64)));
                            else
                                sql.Add("signature", hdr.Signature);

                            sql.AddNow("idate");
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO DVIRHeader", " output inserted.id VALUES "), true);
                            int id = stmt.ifetch("id");
                            if (hdr.Details != null)
                            {
                                foreach (DVIRDetail det in hdr.Details)
                                {
                                    sql.Clear();
                                    sql.Add("category_id", det.CategoryId);
                                    sql.Add("header_id", id);
                                    List<string> defects = new List<string>();
                                    if (det.MajorDefects != null)
                                    {
                                        foreach (int majd in det.MajorDefects)
                                            defects.Add(majd.ToString());
                                        if (defects.Count > 0)
                                            sql.Add("major_defects", string.Join(",", defects.ToArray()));
                                        defects.Clear();
                                    }
                                    if (det.MinorDefects != null)
                                    {
                                        foreach (int mind in det.MinorDefects)
                                            defects.Add(mind.ToString());
                                        if (defects.Count > 0)
                                            sql.Add("minor_defects", string.Join(",", defects.ToArray()));
                                        defects.Clear();
                                    }
                                    stmt.DoSQL(sql.BuildInsert("INSERT INTO dvirdetail"));
                                }
                            }
                            rc = 1;
                        }

                    }
                }
                catch (Exception ex)
                {
                    rc = -3;
                    Global.messLog.Flush("SaveDVIRJson() data failed to receive: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("SaveDVIRJson() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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
            return rc;
        }

        [WebMethod(Description = "Get DVIR report by vehicle id, limit by number of rows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DVIRReport[] GetDVIRReportsJson(Request request, int vehicleId, int numberOfRows)
        {
            DVIRReport[] result = null;
            if (LowSecurityCheck(request, "GetDVIRReportsJson"))
            {
                string connectionString = CustConnectionString(request);
                SqlStmt headerStatement = null;
                SqlStmt defectsStatement = null;
                try
                {
                    if (GetStmt(connectionString, out headerStatement) && GetStmt(connectionString, out defectsStatement))
                    {
                        List<DVIRReport> reports = new List<DVIRReport>();
                        bool haveMoreHeaders = headerStatement.DoSQLFormat(
                            "SELECT TOP {0} * FROM dvirheader WHERE vehicle_id = {1} ORDER BY rdate DESC", numberOfRows, vehicleId
                            );

                        while (haveMoreHeaders)
                        {
                            int headerId = headerStatement.ifetch("id");
                            DVIRReport report = new DVIRReport();
                            report.Id = headerId;
                            report.Company = headerStatement.fetch("company");
                            report.TractorPlate = headerStatement.fetch("tractor_plate");
                            report.Odom = headerStatement.dfetch("odom");
                            report.rdate = headerStatement.dtfetch("rdate");
                            report.lat = headerStatement.dfetch("lat");
                            report.lon = headerStatement.dfetch("lon");
                            report.Inspector = headerStatement.fetch("inspector");
                            report.Comments = headerStatement.fetch("comments");
                            report.CommId = headerStatement.ifetch("comm_id");
                            report.RepKey = headerStatement.fetch("RepKey");
                            report.done = headerStatement.tfetch("done");


                            bool haveMoreDefects = defectsStatement.DoSQLFormat(
                                "SELECT defectId FROM DVIRDefectMapping WHERE reportId = {0} ORDER BY defectId DESC", headerId
                                );
                            report.DefectIds = new List<int>();

                            while (haveMoreDefects)
                            {
                                report.DefectIds.Add(defectsStatement.ifetch("defectId"));
                                haveMoreDefects = defectsStatement.dbSkip();
                            }

                            reports.Add(report);
                            haveMoreHeaders = headerStatement.dbSkip();
                        }
                        result = reports.ToArray();

                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetDVIRReportsJson() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, "GetDVIRReportsJson() " + ex.Message, request);
                }
                finally
                {
                    if (headerStatement != null)
                        headerStatement.Close();
                    if (defectsStatement != null)
                        defectsStatement.Close();
                }
            }
            return result;
        }

        [WebMethod(Description = "Get entire Insp_Details table")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public InspDetails[] GetInspDetailsJson(Request request)
        {
            InspDetails[] result = null;
            if (LowSecurityCheck(request, "GetInspDetailsJson"))
            {
                string connectionString = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connectionString, out stmt))
                    {
                        List<InspDetails> details = new List<InspDetails>();
                        bool rc = stmt.DoSQLFormat("SELECT * FROM insp_details ORDER BY id");
                        while (rc)
                        {
                            InspDetails report = new InspDetails();
                            report.Id = stmt.ifetch("id");
                            report.Type = stmt.ifetch("type");
                            report.Sequence = stmt.ifetch("sequence");
                            if (!stmt.IsNull("section"))
                                report.Section = stmt.fetch("section");
                            report.Category = stmt.fetch("category");
                            report.Issue = stmt.fetch("issue");
                            if (!stmt.IsNull("display"))
                                report.Display = stmt.ifetch("display");
                            if (!stmt.IsNull("pdf_field"))
                                report.PdfField = stmt.fetch("pdf_field");
                            details.Add(report);
                            rc = stmt.dbSkip();
                        }
                        result = details.ToArray();

                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetInspDetailsJson() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, "GetInspDetailsJson() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return result;
        }

        [WebMethod(Description = "Save DVIR report with defects")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SaveDVIRReportJson(Request request, DVIRReport report)
        {
            int rc = -4;

            if (LowSecurityCheck(request, "SaveDVIRJson"))
            {
                string connectionString = CustConnectionString(request);

                SqlStmt reportStatement = null;

                try
                {
                    rc = -2;
                    if (GetStmt(connectionString, out reportStatement))
                    {
                        rc = -3;
                        if (report.done && reportStatement.DoSQL(string.Format("SELECT * FROM DVIRHeader WHERE RepKey = '{0}'", report.RepKey)))
                        {
                            reportStatement.DoSQL(string.Format("UPDATE DVIRHeader SET done = 1 WHERE RepKey = '{0}'", report.RepKey));
                            rc = 1;
                        }
                        else
                        {
                            SqlHelper sql = procData.fillQuery(report, dvirHeaderLayout);
                            if (!string.IsNullOrEmpty(report.sigBase64))
                                sql.Add("signature", myConvert.ToByteArray(base64SigToBmp(report.sigBase64)));
                            else
                                sql.Add("signature", report.Signature);

                            sql.AddNow("idate");
                            reportStatement.DoSQL(sql.BuildInsert("INSERT INTO DVIRHeader", " output inserted.id VALUES "), true);
                            int id = reportStatement.ifetch("id");
                            if (report.DefectIds != null)
                            {
                                foreach (int defectId in report.DefectIds)
                                {
                                    sql.Clear();
                                    sql.Add("reportId", id);
                                    sql.Add("defectId", defectId);
                                    reportStatement.DoSQL(sql.BuildInsert("INSERT INTO DVIRDefectMapping"));
                                }
                            }
                            rc = 1;

                        }

                    }
                }
                catch (Exception ex)
                {
                    rc = -3;
                    Global.messLog.Flush("SaveDVIRReportJson() data failed to receive: " + ex.Message);
                    InsertErrorRecord(request.CustCode, string.Format("SaveDVIRReportJson() data failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message), request);
                }
                finally
                {
                    if (reportStatement != null)
                    {
                        reportStatement.Close();
                        reportStatement = null;
                    }
                }

            }
            return rc;
        }

        [WebMethod(Description = "Get DVIR defect types and sections")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DVIRDefectSection[] GetDVIRDefectTypes(Request request)
        {
            DVIRDefectSection[] result = null;
            if (LowSecurityCheck(request, "DVIRDefectSection"))
            {
                string connectionString = CustConnectionString(request);
                SqlStmt sectionsStatement = null;
                SqlStmt typesStatement = null;

                try
                {
                    if (GetStmt(connectionString, out sectionsStatement) && GetStmt(connectionString, out typesStatement))
                    {
                        bool haveSections = sectionsStatement.DoSQL("SELECT * FROM DVIRDefectSections");
                        List<DVIRDefectSection> sections = new List<DVIRDefectSection>();
                        while (haveSections)
                        {
                            DVIRDefectSection section = new DVIRDefectSection();
                            section.id = sectionsStatement.ifetch("id");
                            section.referenceId = sectionsStatement.fetch("referenceId");
                            section.description = sectionsStatement.fetch("description");

                            List<DVIRDefectType> types = new List<DVIRDefectType>();
                            bool haveTypes = typesStatement.DoSQLFormat("SELECT * FROM DVIRDefectTypes WHERE sectionId = {0}", section.id);
                            while (haveTypes)
                            {
                                DVIRDefectType type = new DVIRDefectType();
                                type.id = typesStatement.ifetch("id");
                                type.referenceId = typesStatement.fetch("referenceId");
                                type.description = typesStatement.fetch("description");
                                type.isMajor = typesStatement.bfetch("isMajor");
                                type.isMinor = typesStatement.bfetch("isMinor");
                                type.sectionId = typesStatement.ifetch("sectionid");

                                types.Add(type);
                                haveTypes = typesStatement.dbSkip();
                            }
                            section.defectTypes = types.ToArray();
                            sections.Add(section);
                            haveSections = sectionsStatement.dbSkip();
                        }
                        result = sections.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetDVIRDefectTypesJson() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, ex.Message);
                    InsertErrorRecord(request.CustCode, "GetDVIRDefectTypesJson() " + ex.Message, request);
                }
                finally
                {
                    if (sectionsStatement != null)
                        sectionsStatement.Close();
                    if (typesStatement != null)
                        typesStatement.Close();
                }
            }

            return result;
        }


        [WebMethod(Description = "Check for activation for React App JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public GoUser WebLogJson(Request req, string data)
        {
            return PhoneLogin(req, data);
        }

        [WebMethod(Description = "Login request for phone app")]
        public GoUser PhoneLogin(Request request, string data)
        {
            GoUser response = new GoUser();
            response.username = string.Empty;

            // added security!!
            if (LowSecurityCheck(request, "PhoneLogin"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string[] passUser = data.Split('¿');
                        if (passUser.Length > 1)
                        {
                            if (stmt.DoSQLFormat("select * from users where username='{0}'", passUser[1]))
                            {
                                if (passUser[0] == stmt.fetch("password"))
                                {
                                    response.username = stmt.fetch("username");
                                    response.email = stmt.fetch("email");
                                    response.firstname = stmt.fetch("first_name");
                                    response.lastname = stmt.fetch("last_name");
                                    response.phonenumber = stmt.fetch("phone_number");
                                    response.title = stmt.fetch("title");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("PhoneLogin" + ex.Message);
                    InsertErrorRecord(request.CustCode, "PhoneLogin() " + ex.Message, request);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return response;
        }


        [WebMethod(Description = "Check for activation for React App JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public CommResponse PhoneLoginJson(Request req, string imei)
        {
            return PhoneSoft(req, imei);
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
                        bool rc = stmt.DoSQL(string.Format("SELECT * FROM soft_activate WHERE client_guid='{0}'", guid));

                        //Global.messLog.Flush("AutoCommission = " + (myConvert.ToBoolean(WebConfigurationManager.AppSettings["AutoCommission"]) ? "True" : "False"));

                        if ((!rc || stmt.fetch("db_codes") == string.Empty) && myConvert.ToBoolean(WebConfigurationManager.AppSettings["AutoCommission"]))
                        {
                            int HandCode = 0;
                            string where = string.Empty;

                            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                            sql.AddNow("comm_date");
                            sql.Add("phone_version", request.Version);
                            sql.Add("db_codes", "DEMO");
                            sql.Add("pc_code", "DEMO");
                            sql.Add("description", "Auto Commissioned ROADAReact V2");

                            if (rc)
                            {
                                HandCode = stmt.ifetch("hand_code");
                                where = string.Format("WHERE id={0}", stmt.ifetch("id"));
                            }
                            else
                                sql.Add("client_guid", guid);

                            if (HandCode == 0)
                            {
                                stmt.DoSQL("select max(hand_code) as handCode from soft_activate");
                                sql.Add("hand_code", stmt.ifetch("handCode") + 1);
                            }

                            stmt.DoSQL(sql.BuildCmd("soft_activate", !rc, where));
                            stmt.DoSQL(string.Format("SELECT * FROM soft_activate WHERE client_guid='{0}'", guid));
                        }


                        if (stmt.fetch("db_codes") == "")
                        {
                            response.Mesg = ("Device has not yet been Assigned Contact DM&T");
                        }
                        else
                        {
                            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                            sql.AddNow("last_checkin");
                            sql.Add("phone_version", request.Version);

                            int rowId = stmt.ifetch("id");
                            string pcCode = stmt.fetch("pc_code");
                            string dbCodes = stmt.fetch("db_codes");
                            string desc = stmt.fetch("description");
                            int HandCode = stmt.ifetch("hand_code");

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
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DeviceInfo GetDeviceInfoJson(Request req, int devId)
        {
            return GetDeviceInfo(req, devId);
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
                        dInfo.Pdop = stmt.dfetch("pdop");
                        dInfo.Hdop = stmt.dfetch("hdop");
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

                        bool rc = stmt.DoSQLFormat("SELECT TOP 5 * FROM img_events WHERE hand_code = {0} ORDER BY rdate DESC", devId);
                        List<ImgInfo> imgs = new List<ImgInfo>();
                        while (rc)
                        {
                            ImgInfo img = new ImgInfo();
                            img.EvtType = stmt.ifetch("evt_type");
                            img.rdate = stmt.dtfetch("rdate");
                            img.ImgUrl = stmt.fetch("img_url");
                            imgs.Add(img);
                            rc = stmt.dbSkip();
                        }
                        dInfo.Images = imgs.ToArray();
                        imgs.Clear();
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

        [WebMethod(Description = "Retrieve DCD device info for an array of vehicle's (POST)")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DeviceInfo[] GetDeviceInfoArrayJson(Request req, int[] devIds)
        {
            return GetDeviceInfoArray(req, devIds);
        }

        [WebMethod(Description = "Retrieve DCD device info for an array of vehicle's")]
        public DeviceInfo[] GetDeviceInfoArray(Request request, int[] devIds)
        {
            DeviceInfo[] dInfos = null;

            if (!LowSecurityCheck(request, "GetDeviceInfoArray"))
                return dInfos;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    TList<DeviceInfo> devList = new TList<DeviceInfo>();
                    foreach (int devId in devIds)
                    {
                        if (stmt.DoSQLFormat("SELECT *, vehicles.unit_num as vunit FROM handheld left join vehicles on vehicles.vehicle = handheld.vehicle_id WHERE handheld.hand_code = {0} and vehicles.vehicle > 0", devId))
                        {
                            DeviceInfo dInfo = new DeviceInfo();
                            dInfo.truck = stmt.fetch("vunit");
                            dInfo.DevId = devId;
                            dInfo.ServerDate = DateTime.UtcNow;
                            dInfo.LoginDate = stmt.dtfetch("log_date");
                            dInfo.GPSDate = stmt.dtfetch("last_date");
                            dInfo.Latitude = stmt.dfetch("last_lat");
                            dInfo.Longitude = stmt.dfetch("last_long");
                            dInfo.Pdop = stmt.dfetch("pdop");
                            dInfo.Hdop = stmt.dfetch("hdop");
                            dInfo.Speed = stmt.ifetch("speed");
                            dInfo.FirmVers = stmt.fetch("firmware_version");
                            dInfo.ModemVers = stmt.fetch("modem_version");
                            if (!stmt.IsNull("cell_strength"))
                                dInfo.CellStrength = stmt.ifetch("cell_strength");
                            if (devId > 0 && dInfo.GPSDate != DateTime.MinValue)
                            {
                                if (stmt.DoSQLFormat("SELECT TOP 1 * FROM messagein WHERE hand_code = {0} and rdate >= {1}", devId, SqlHelper.timeStamp(dInfo.GPSDate)))
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
                            }
                            /*
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

                                                        bool rc = stmt.DoSQLFormat("SELECT TOP 5 * FROM img_events WHERE hand_code = {0} ORDER BY rdate DESC", devId);
                                                        List<ImgInfo> imgs = new List<ImgInfo>();
                                                        while (rc)
                                                        {
                                                            ImgInfo img = new ImgInfo();
                                                            img.EvtType = stmt.ifetch("evt_type");
                                                            img.rdate = stmt.dtfetch("rdate");
                                                            img.ImgUrl = stmt.fetch("img_url");
                                                            imgs.Add(img);
                                                            rc = stmt.dbSkip();
                                                        }
                                                        dInfo.Images = imgs.ToArray();
                                                        imgs.Clear(); */
                            devList.Add(dInfo);

                        }
                    }
                    dInfos = devList.ToArray();
                    devList.Clear();
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetDeviceInfoArray() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                InsertErrorRecord(request.CustCode, "GetDeviceInfoArray() " + x.Message, request);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return dInfos;
        }

        [WebMethod(Description = "Push WRR Report Data Json")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendWrrJson(Request req, wrrData wdata)
        {
            return SendWrr(req, wdata);
        }
        private int SendWrr(Request req, wrrData wdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendWrr"))
                return -2;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Clear();
                    sql.Add("username", wdata.username);
                    sql.Add("date", wdata.date);
                    sql.Add("handcode", wdata.handcode);
                    sql.Add("unitnum", wdata.unitnum);
                    sql.Add("yard", wdata.yard);
                    sql.Add("avlgps", wdata.avlgps);
                    sql.Add("avlnote", wdata.avlnote);
                    sql.Add("antenna", wdata.antenna);
                    sql.Add("antennaNote", wdata.antennaNote);
                    sql.Add("camera", wdata.camera);
                    sql.Add("cameraNote", wdata.cameraNote);
                    sql.Add("gpslocation", wdata.gpslocation);
                    sql.Add("gpsNote", wdata.gpsNote);
                    sql.Add("modem", wdata.modem);
                    sql.Add("modemNote", wdata.modemNote);
                    sql.Add("ignition", wdata.ignition);
                    sql.Add("ignitionNote", wdata.ignitionNote);
                    sql.Add("plowup", wdata.plowup);
                    sql.Add("plowupNote", wdata.plowupNote);
                    sql.Add("plowdown", wdata.plowdown);
                    sql.Add("plowdownNote", wdata.plowdownNote);
                    sql.Add("wingup", wdata.wingup);
                    sql.Add("wingupNote", wdata.wingupNote);
                    sql.Add("wingdown", wdata.wingdown);
                    sql.Add("wingdownNote", wdata.wingdownNote);
                    sql.Add("ECM", wdata.ECM);
                    sql.Add("ECMnote", wdata.ECMnote);
                    sql.Add("spreader", wdata.spreader);
                    sql.Add("spreaderNote", wdata.spreaderNote);
                    sql.Add("spreaderMatch", wdata.spreaderMatch);
                    sql.Add("spreaderMatchNote", wdata.spreaderMatchNote);
                    sql.Add("material", wdata.material);
                    sql.Add("materialNote", wdata.materialNote);
                    sql.Add("blue", wdata.blue);
                    sql.Add("blueNote", wdata.blueNote);
                    sql.Add("amber", wdata.amber);
                    sql.Add("amberNote", wdata.amberNote);
                    sql.Add("airtemp", wdata.airtemp);
                    sql.Add("airNote", wdata.airNote);
                    sql.Add("roadtemp", wdata.roadtemp);
                    sql.Add("roadNote", wdata.roadNote);
                    sql.Add("camerapower", wdata.camerapower);
                    sql.Add("camerapowerNote", wdata.camerapowerNote);
                    sql.Add("cameramount", wdata.cameramount);
                    sql.Add("cameramountNote", wdata.cameramountNote);
                    sql.Add("camerareport", wdata.camerareport);
                    sql.Add("camerareportNote", wdata.camerareportNote);
                    sql.Add("testimage", wdata.testimage);
                    sql.Add("testimageNote", wdata.testimageNote);


                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO winter_ready"));

                    Result = 1;

                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendWrrReport() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendWrrReport() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push Work Report Data Json")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendWorkReportJson(Request req, WorkReport wrdata)
        {
            return SendWorkReport(req, wrdata);
        }
        private int SendWorkReport(Request req, WorkReport wrdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendWorkReport"))
                return -2;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Clear();
                    sql.Add("username", wrdata.userName);
                    sql.Add("rdate", wrdata.reportDate);
                    sql.Add("install", wrdata.install);
                    sql.Add("notes", wrdata.notes);
                    sql.Add("unitnum", wrdata.unitNum);
                    sql.Add("photo_id", wrdata.photoId);
                    sql.AddNow("idate");



                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO work_report"));

                    Result = 1;

                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendWorkReport() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendWorkReport() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }


        [WebMethod(Description = "Push Weather Report Data Json")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendWeatherReportJson(Request req, WeatherData wdata)
        {
            return SendWeather(req, wdata);
        }

        [WebMethod(Description = "Push Weather Report")]
        public int SendWeatherReport(Request req, WeatherData wdata)
        {
            return SendWeather(req, wdata);
        }




        private int SendWeather(Request req, WeatherData wdata)
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
                    string opString = "";

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
                        sql.Add("snow_accum", wdata.snowAccum);
                        sql.Add("swsnow_accum", wdata.swsnowAccum);
                        sql.Add("visibility", wdata.visibility);
                        sql.Add("cloud", wdata.cloudCondition);
                        sql.Add("fog", wdata.fogLvl);
                        sql.Add("drifting", wdata.drifting);
                        sql.Add("road", wdata.roadCondition);
                        sql.Add("road2", wdata.road2ndCondition);
                        sql.Add("sidewalk", wdata.sidewalkCondition);
                        sql.Add("latitude", wdata.CurrentLat);
                        sql.Add("longitude", wdata.CurrentLong);
                        sql.Add("calc_lat", wdata.calcLat);
                        sql.Add("calc_long", wdata.calcLong);
                        sql.Add("temp_air", myConvert.ToDouble(wdata.tempAir));
                        sql.Add("temp_road", wdata.tempRoad);
                        sql.Add("valid_loc", wdata.validLoc);
                        sql.Add("address", wdata.address);
                        sql.Add("comment", wdata.comment);
                        sql.Add("action_taken", wdata.action);
                        sql.Add("work_order", wdata.workOrder);
                        sql.Add("road_service", wdata.roadService);
                        sql.Add("sidewalk_service", wdata.sidewalkService);

                        if (wdata.action != null && wdata.action.Length > 0)
                        {
                            sql.Add("action_taken", wdata.action);
                        }
                        else
                        {
                            if (wdata.plowOperation > 0)
                            {
                                opString += "Plowing (OP),";
                            }
                            if (wdata.sandingOperation > 0)
                            {
                                opString += "Sanding (OSA),";
                            }
                            if (wdata.saltingOperation > 0)
                            {
                                opString += "Salting (OSG),";
                            }
                            if (wdata.icingOperation > 0)
                            {
                                opString += "Anti-Icing (OAI),";
                            }
                            if (opString.Length > 0)
                            {
                                sql.Add("action_taken", opString);
                            }
                        }


                        sql.AddNow("idate");
                        sql.Add("vehicle_id", getVehicleId(stmt, wdata.handCode, wdata.unitNum));

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO weather_condition"));
                        Result = 1;

                        //added updated the temp field in the collsion report due to the fact that the weather report getting sent after collsion
                        if (stmt.DoSQL(string.Format("SELECT ci_id FROM collision_incident WHERE ci_id = '{0}'", wdata.reportId)))
                        {
                            sql.Clear();
                            opString = "";
                            sql.Add("temp", myConvert.ToDouble(wdata.tempAir));
                            stmt.DoSQL(sql.BuildUpdate("UPDATE collision_incident", string.Format("WHERE ci_id = '{0}'", wdata.reportId)));
                            Result = 2;
                        }



                        
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

        [WebMethod(Description = "Send Test Signature as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendTestDataJson(Request req, TestData data)
        {
            int len = -1;
            if (data != null)
                if (data.Signature != null)
                    len = data.Signature.Count;
            Global.messLog.Flush("SendTestData() HandCode:{0} Version:{1} Length:{2}", req.Handcode, req.Version, len);
            return 0;
        }


        [WebMethod(Description = "Send Collision Data as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendCollisionDataJson(Request req, ReportData data)
        {
            return SendReportSig(req, data);
        }

        private UInt32 GetUInt32BigEndian(byte[] buf)
        {
            UInt32 Result = 0;
            try
            {
                byte b = buf[0];
                buf[0] = buf[3];
                buf[3] = b;
                b = buf[1];
                buf[1] = buf[2];
                buf[2] = b;
                Result = BitConverter.ToUInt32(buf, 0);
            }
            catch { }

            return Result;
        }


        [WebMethod(Description = "Push Collision Report")]
        public int SendReportNew(Request req, ReportData rdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendReportNew (Collision)"))
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
                    if (rdata.Signature != null && rdata.Signature.Count > 0)
                        sql.Add("sig_data", rdata.Signature);
                    else
                        sql.Add("sig_data", rdata.Signiture);   // this misspelled one is byte[], possibly for others?
                    sql.Add("Temp", rdata.Temp);
                    sql.Add("Address", rdata.Address);

                    //adds and updates main report table 
                    if (stmt.DoSQL(string.Format("SELECT ci_id FROM collision_incident WHERE ci_id = '{0}'", rdata.reportTime)))
                    {
                        stmt.DoSQL(sql.BuildUpdate("UPDATE collision_incident", string.Format("WHERE ci_id = '{0}'", rdata.reportTime)));
                        Result = 1;
                    }
                    else
                    {
                        sql.Add("ci_id", rdata.reportTime);

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_incident"));
                        Result = 2;
                    }

                    // updates adds update files to update table 
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

                    logUser(stmt, req, rdata.repPrep, true);
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
        public int SendReportSig(Request req, ReportData rdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendReportNew (Collision)"))
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

                    if (!string.IsNullOrEmpty(rdata.sigbase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(rdata.sigbase64)));
                    else
                        sql.Add("sig_data", rdata.sigbase64);

                    sql.Add("Temp", rdata.Temp);
                    sql.Add("Address", rdata.Address);

                    //adds and updates main report table 
                    if (stmt.DoSQL(string.Format("SELECT ci_id FROM collision_incident WHERE ci_id = '{0}'", rdata.reportTime)))
                    {
                        stmt.DoSQL(sql.BuildUpdate("UPDATE collision_incident", string.Format("WHERE ci_id = '{0}'", rdata.reportTime)));
                        Result = 1;
                    }
                    else
                    {
                        sql.Add("ci_id", rdata.reportTime);

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_incident"));
                        Result = 2;
                    }

                    // updates adds update files to update table 
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
                    sql.Add("Temp", rdata.Temp);

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
                    if (stmt.DoSQL(String.Format("SELECT unit_num,vehicle FROM vehicles with (nolock) WHERE hand_code = {0}", imgRec.HC)))
                    {
                        sql.Add("unit_num", stmt.fetch("unit_num"));
                        sql.Add("vehicle_id", stmt.ifetch("vehicle"));
                    }
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


        [WebMethod(Description = "IsDcdWorking.")]
        public bool IsDcdWorking(Request req, DateTime InUTC, int handcode)
        {
            bool Result = false;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "IsDcdWorking"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        if (stmt.DoSQLFormat("SELECT TOP 1 last_date FROM handheld WHERE hand_code = {0} order by last_date desc", handcode))
                            Result = (stmt.dtfetch("last_date").AddMinutes(15) > InUTC);
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("IsDcdWorking() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                    InsertErrorRecord(req.CustCode, "IsDcdWorking() " + x.Message, req);
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

        // Apr/2025  changed S3 servers used to be https://bucket.s3-nubav...
        //           now it's https://xxxxxxx.ca/bucket/....
        private string getBucket(string url)
        {
            string rc = url;
            int l = rc.IndexOf("//");
            if (l >= 0)
            {
                rc = rc.Substring(l + 2);
                l = rc.IndexOf("/");
                if (l > 0)
                {
                    rc = rc.Substring(l + 1);
                    l = rc.IndexOf("/");
                    if (l > 0)
                        rc = rc.Substring(0, l);
                }
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
            pData.repkey = picturedata.InstallKey;
            pData.handcode = picturedata.handcode;
            pData.unitnum = picturedata.unitNum;
            pData.objKey = picturedata.objKey;
            pData.rdate = picturedata.pictureSent;
            pData.imgUrl = picturedata.imgUrl;
            pData.bucket = getBucket(picturedata.imgUrl);
            pData.imgcreated = picturedata.pictureSent;

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
            sql.Add("issue", pdata.issue);
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
            sql.Add("vehicle_id", getVehicleId(stmt, pdata.handcode, pdata.unitnum));

            // for York, we need to update the trip's uDate if the trip has been completed so the API's will know to send the img_event data!
            if (stmt.DoSQLFormat("SELECT TOP 1 id, end_trip FROM trip WHERE start_trip < {0} and hand_code = {1} ORDER BY start_trip DESC", SqlHelper.timeStamp(pdata.rdate), pdata.handcode))
            {
                int tripId = stmt.ifetch("id");
                // Drew wants trip id for the service that monitors conditions
                sql.Add("trip_id", tripId);
                // if the end_trip is populated, the trip is complete and we need to update uDate
                if (!stmt.IsNull("end_trip"))
                {
                    // must be in range of the trip
                    if (pdata.rdate <= stmt.dtfetch("end_trip"))
                        stmt.DoSQLFormat("UPDATE trip with (rowlock) SET uDate = GetDate() WHERE id = {0}", tripId);
                }
            }

            bool excp = false;
            try
            {
                if (!stmt.DoSQLFormat("SELECT id FROM img_events WHERE obj_key='{0}'", pdata.objKey))
                    stmt.DoSQL(sql.BuildCmd("img_events", true));
            }
            catch (Exception ex)
            {
                // some versions of the tablet program take multiple pictures with the same rdate which will cause this error
                //                                                                                     changed the order of the fields and thus the index name
                excp = (stmt.LastError.Contains("duplicate key") && (stmt.LastError.Contains("unique_rdatehc") || stmt.LastError.Contains("unique_hcrdate")));
                if (!excp)
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
            return SendPhotoData2(req, pdata);
        }

        [WebMethod(Description = "Push Camera Image Data JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendPhotoDataJson(Request req, PhotoData pdata)
        {
            return SendPhotoData2(req, pdata);
        }

        private int SendPhotoData2(Request req, PhotoData pdata)
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
                    storePicData(pdata, stmt);

                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Add("truck_id", pdata.unitnum);
                    sql.Add("user_id", pdata.username);
                    sql.Add("signal_str", pdata.SignalStr);
                    sql.Add("net_type", pdata.NetworkType);
                    sql.Add("last_sent", DateTime.UtcNow);
                    sql.Add("q_count", pdata.qCount); // pic WEbService Q count
                    sql.Add("pic_q_count", pdata.picQCount);
                    sql.Add("event_q_count", pdata.eventQCount);
                    sql.Add("imei", pdata.imei);
                    //sql.Add("in_yard", pdata.inyard);
                    sql.AddNow("last_checkin");

                    // this should be changed to phandcode_GS (same as in SendEventRecord)
                    int hc = pdata.phandcode;

                    string wh = string.Format("WHERE hand_code={0}", hc);
                    bool ins = !stmt.DoSQL("SELECT * FROM go_status " + wh);
                    if (ins)
                        sql.Add("hand_code", hc);
                    stmt.DoSQL(sql.BuildCmd("go_status", ins, wh, true));

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

        [WebMethod(Description = "Send Request to Commision React Phone App JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendCommissionJson(Request req, Comissioninfo comInfo)
        {
            return SendCommision(req, comInfo);
        }

        [WebMethod(Description = "Send Request to Commision Phone for roada App")]
        public int SendCommision(Request req, Comissioninfo comInfo)
        {
            int Result = -5;
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
                if (!stmt.DoSQL(String.Format("SELECT id FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI)))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.Add("pc_code", comInfo.Owner);
                    sql.Add("client_guid", comInfo.IMEI);
                    sql.Add("description", comInfo.desc);
                    sql.Add("device_hardware", comInfo.hardware);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO soft_activate"));
                    if (stmt.LastError != string.Empty)
                        Result = -6;
                    else
                    {
                        stmt.DoSQLFormat("SELECT id FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI);
                        Result = stmt.ifetch("id"); //returns the newly created ID for the phones Confiuration  
                    }
                }
                else
                    Result = stmt.ifetch("id");
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
            return RetrieveHOS(request, user_name, currentDate);
        }

        [WebMethod(Description = "Retrieve Hos Events JSON (PUSH)")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public HosEvent[] GetHosListJson(Request request, string user_name, DateTime currentDate)
        {
            return RetrieveHOS(request, user_name, currentDate);
        }

        private HosEvent[] RetrieveHOS(Request request, string user_name, DateTime currentDate)
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
                    rc = stmt.DoSQLFormat("SELECT * FROM go_hos WHERE hide is null AND user_name = '{0}' AND rdate >= {1} ORDER BY rdate DESC", user_name, SqlHelper.timeStamp(currentDate, SqlHelper.dbType.SqlServer));

                    while (rc)
                    {
                        HosEvent Hev = new HosEvent();
                        Hev.eventTime = stmt.dtfetch("rdate");
                        Hev.code = stmt.ifetch("event_code");
                        Hev.user_name = stmt.fetch("user_name");
                        Hev.duty = stmt.bfetch("duty");
                        Hev.driving = stmt.bfetch("driving");
                        Hev.phandcode = stmt.ifetch("phand_code");
                        Hev.serverID = stmt.ifetch("id");
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

        [WebMethod(Description = "Retrieve Work Orders")]
        public YrkWorkOrder[] GetWorkOrders(Request request, DateTime date)
        {
            return RetrieveWorkOrders(request, date);
        }

        [WebMethod(Description = "Retrieve Work Orders JSON (PUSH)")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public YrkWorkOrder[] GetWorkOrdersJson(Request request, DateTime date)
        {
            return RetrieveWorkOrders(request, date);
        }

        private YrkWorkOrder[] RetrieveWorkOrders(Request request, DateTime date)
        {
            YrkWorkOrder[] Result = null;

            if (!LowSecurityCheck(request, "GetWorkOrders"))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    List<YrkWorkOrder> workorders = new List<YrkWorkOrder>();
                    bool rc;
                    rc = stmt.DoSQLFormat("SELECT * FROM wonumbers WHERE projstartdate = '{0}'", date.ToString("yyyy-MM-dd"));

                    while (rc)
                    {
                        YrkWorkOrder wo = new YrkWorkOrder();
                        wo.Id = stmt.ifetch("id");
                        wo.District = stmt.fetch("district");
                        wo.ProjStartDate = stmt.dtfetch("projstartdate");
                        wo.WOType = stmt.ifetch("wotype");
                        wo.WorkOrderId = stmt.fetch("workorderid");
                        workorders.Add(wo);
                        rc = stmt.dbSkip();
                    }
                    Result = workorders.ToArray();
                    workorders.Clear();
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetWorkOrders() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
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
            return SaveHosEvent(req, evnt);
        }

        [WebMethod(Description = "Hos Events For go app Json (PUSH)")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendHosEventJson(Request req, HosEvent evnt)
        {
            return SaveHosEvent(req, evnt);
        }

        private int SaveHosEvent(Request req, HosEvent evnt)
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

                    sql.Add("idate", now);
                    sql.Add("user_name", evnt.user_name);
                    sql.Add("rdate", evnt.eventTime);
                    sql.Add("event_code", evnt.code);
                    sql.Add("handcode", evnt.handcode);
                    sql.Add("duty", evnt.duty);
                    sql.Add("driving", evnt.driving);
                    sql.Add("phand_code", evnt.phandcode);
                    stmt.DoSQL(sql.BuildInsert("INSERT INTO go_hos"));
                    stmt.DoSQL(string.Format("SELECT id FROM go_hos WHERE rdate = '{0}' and user_name = '{1}'", evnt.eventTime, evnt.user_name));
                    Result = stmt.ifetch("id");

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
        [WebMethod(Description = "creates and updates exsiting hos events Json (PUSH)")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public ServerIDs SendHosOffWorkEventJson(Request req, HosEvent FromEvent, HosEvent ToEvent)
        {
            return SendHosOffWorkEvent(req, FromEvent, ToEvent);
        }

        [WebMethod(Description = "creates and updates exsiting hos events")]
        public ServerIDs SendHosOffWorkEvent(Request req, HosEvent FromEvent, HosEvent ToEvent)
        {
            List<int> ids = new List<int>();
            ServerIDs Result = null;
            // low security check, don't want hackers!
            Global.messLog.Flush("SendHosOffWorkEvent");
            if (!LowSecurityCheck(req, "SendHosOffWorkEvent"))
                return Result;


            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {

                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    DateTime now = DateTime.Now.ToLocalTime();


                    bool rc = stmt.DoSQLFormat("SELECT id FROM go_hos WHERE rdate >= '{0}' AND rdate <= '{1}'", FromEvent.eventTime, ToEvent.eventTime);
                    while (rc)
                    {
                        ids.Add(stmt.ifetch("id"));
                        rc = stmt.dbSkip();
                    }

                    if (ids.Count > 0)
                    {
                        foreach (var item in ids)
                        {
                            stmt.DoSQL(string.Format("UPDATE go_hos SET hide = 1 WHERE id = {0}", item));
                        }
                    }
                    sql.Add("idate", now);
                    sql.Add("user_name", FromEvent.user_name);
                    sql.Add("rdate", FromEvent.eventTime);
                    sql.Add("event_code", FromEvent.code);
                    sql.Add("handcode", FromEvent.handcode);
                    sql.Add("duty", FromEvent.duty);
                    sql.Add("driving", FromEvent.driving);
                    sql.Add("phand_code", FromEvent.phandcode);
                    stmt.DoSQL(sql.BuildInsert("INSERT INTO go_hos"));

                    sql.Clear();


                    sql.Add("idate", now);
                    sql.Add("user_name", ToEvent.user_name);
                    sql.Add("rdate", ToEvent.eventTime);
                    sql.Add("event_code", ToEvent.code);
                    sql.Add("handcode", ToEvent.handcode);
                    sql.Add("duty", ToEvent.duty);
                    sql.Add("driving", ToEvent.driving);
                    sql.Add("phand_code", ToEvent.phandcode);
                    stmt.DoSQL(sql.BuildInsert("INSERT INTO go_hos"));
                    ServerIDs si = new ServerIDs();
                    si.serverIds = ids.ToArray();
                    Result = si;

                }

            }
            catch (Exception ex)
            {

                Global.messLog.Flush("HOS Event Message Failed HOS VS: {0}", req.Version);
                InsertErrorRecord(req.CustCode, string.Format("SendHosOffWorkEvent() Handcode{0} Error{1}", req.Handcode, ex.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "send installer pics via Json for mobileinstaller")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int sendInstallerPicsJson(Request req, InstalledPictures[] picturedata)
        {
            return sendInstallerPics(req, picturedata);
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


        [WebMethod(Description = "Save Sidewalk Pics from React App JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int sendSidewalkPicsJson(Request req, InstalledPictures picturedata)
        {
            return sendSidewalkPics(req, picturedata);
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

        [WebMethod(Description = "Save Sidewalk Data from React App JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendSidewalkDataJSon(Request req, SideWalkData data)
        {
            return SendSidewalkData(req, data);
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
                    if (data.def2 == -1)
                        sql.Add("def2", "null", false);
                    else
                        sql.Add("def2", data.def2);
                    sql.Add("latitude", data.lat);
                    sql.Add("longitude", data.lon);
                    sql.Add("pic_count", data.pic_count);
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

        [WebMethod(Description = "Save Wildlife Data from React App JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendWildlifeDataJSon(Request req, WildlifeData data)
        {
            return SendWildlifeData(req, data);
        }

        [WebMethod(Description = "Puts wildlife inspection Data Away")]
        public int SendWildlifeData(Request req, WildlifeData data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendWildlifeData"))
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

                    sql.Add("reportId", data.reportID);
                    sql.Add("hand_code", data.handcode);
                    sql.Add("address", data.address);
                    sql.Add("unit_num", data.unitnum);
                    sql.Add("userName", data.username);
                    sql.Add("direction", data.direction);
                    sql.Add("reportTime", data.reportTime);
                    sql.Add("town", data.town);
                    sql.Add("age", data.age);
                    sql.Add("gender", data.gender);
                    sql.Add("species", data.species);
                    sql.Add("weather", data.weather);
                    sql.Add("visibility", data.visibility);
                    sql.Add("highway", data.highway);
                    sql.Add("rowt", data.rowt);
                    sql.Add("comments", data.comments);
                    sql.Add("wLog", data.log);
                    sql.Add("lat", data.lat);
                    sql.Add("lon", data.lon);

                    if (stmt.DoSQL(String.Format("SELECT * FROM wildlife WHERE reportId = '{0}'", data.reportID)))
                    {
                        stmt.DoSQL(sql.BuildUpdate("UPDATE wildlife", String.Format("WHERE reportId = '{0}'", data.reportID)));

                        Result = 1;
                    }
                    else
                    {
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO wildlife"));

                        Result = 2;
                    }

                    Result = 1;
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("sendWildlifeData() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("sendWildlifeData() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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
                                       { "RequestId", "requestid"},
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
                                       { "District", "wo_district" },
                                       { "Location", "location_desc"},
                                       { "Closed", "closed" },
                                       { "Finished", "finished" },
                                       { "WO_Id", "workorderid" },
                                       { "WO_No", "workorderno" } };

        // defines the relationship between UserNote class and usernotes fields
        // first string is name in class, second is name in database
        string[,] userNotesLayout = {  { "HandCode", "hand_code"},
                                       { "UnitNum", "unit_num"},
                                       { "UserName", "user_name"},
                                       { "ReportTime", "rdate"},
                                       { "Notes", "notes"} };

        // these are only used when sending data TO tablets and MUST be removed when receiving...
        string[] defSendOnly = new string[] { "id", "requestid", "closed", "workorderid", "workorderno" };

        // Allow setting 'finish' with the added note
        [WebMethod(Description = "Receive Deficiency Additional Note as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDefAddNoteExJson(Request req, int id, string addNote, int finish)
        {
            return SendDefAddNote(req, id, addNote, finish);
        }

        // needed to use Post on this function (UseHttpGet = false)
        [WebMethod(Description = "Receive Deficiency Additional Note as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDefAddNoteJsonP(Request req, int id, string addNote)
        {
            return SendDefAddNote(req, id, addNote, 0);
        }

        [WebMethod(Description = "Receive Deficiency Additional Note as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDefAddNoteJson(Request req, int id, string addNote)
        {
            return SendDefAddNote(req, id, addNote, 0);
        }

        [WebMethod(Description = "Receive Deficiency Additional Note")]
        public int SendDefAddNote(Request req, int id, string addNote, int finish)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SendDefAddNote"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    Result = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        string where = string.Format(" WHERE id={0}", id);
                        if (stmt.DoSQL("SELECT * FROM deficiencies" + where))
                        {
                            Result = 0;
                            string note = stmt.fetch("add_notes");
                            if (note != string.Empty)
                            {
                                if (!(note.EndsWith("|") || addNote.StartsWith("|")))
                                    note += "|";
                                addNote = note + addNote;
                            }

                            stmt.DoSQLFormat("update deficiencies set add_notes='{0}',finished={1},udate=GetDate()" + where, SqlHelper.FixUp(addNote), finish);
                        }
                        else
                            Global.messLog.Flush("LastError - " + stmt.LastError);
                    }
                }
                catch (Exception ex)
                {
                    Result = -3;
                    Global.messLog.Flush("SendDefAddNote() data failed to receive: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("SendDefAddNote() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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

        // needed to use Post on this function (UseHttpGet = false)
        [WebMethod(Description = "Send Deficiency Location Changes as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDefLocChangeJson(Request req, DeficiencyData data)
        {
            return SendDefLocChange(req, data);
        }

        [WebMethod(Description = "Receive Deficiency Location Changes")]
        public int SendDefLocChange(Request req, DeficiencyData data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SendDefLocChange"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    Result = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        string where = string.Format(" WHERE id={0}", data.ReportId);
                        stmt.DoSQLFormat("select * from deficiencies {0}", where);
                        int requestId = stmt.ifetch("requestid");
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        sql.Add("latitude", data.Lat);
                        sql.Add("longitude", data.Lon);
                        sql.Add("location_desc", data.Location);
                        string sqlStr = sql.BuildUpdate("update deficiencies", where);
                        stmt.DoSQL(sqlStr);
                        Global.messLog.Flush(sqlStr);
                        double utmNorthing, utmEasting;
                        string zoneStr;
                        utm.FromLatLong(data.Lat, data.Lon, out utmNorthing, out utmEasting, out zoneStr);
                        // we need to update the service request as well for York, if it exists
                        sql.Clear();
                        sql.Add("sry", utmNorthing);
                        sql.Add("srx", utmEasting);
                        sql.Add("problocation", data.Location);
                        string sqlStr2 = sql.BuildUpdate("update servicerequest", string.Format(" where requestid={0}", requestId));
                        stmt.DoSQL(sqlStr2);
                        Global.messLog.Flush(sqlStr2);
                        Result = 0;
                    }
                }
                catch (Exception ex)
                {
                    Result = -3;
                    Global.messLog.Flush("SendDefLocChange() data failed to receive: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("SendDefLocChange() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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

        // created this to allow using POST as the one below has UseHttpGet = true and is already in use by other programs
        [WebMethod(Description = "Receive Deficiency Data as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDeficiencyJsonP(Request req, DeficiencyData data)
        {
            return SendDeficiencyData(req, data);
        }

        // created this to allow using POST as the one below has UseHttpGet = true and is already in use by other programs
        [WebMethod(Description = "Receive Deficiency Data as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendDeficiencyJsonReact(Request req, DeficiencyData data)
        {
            return SendDeficiencyData(req, data);
        }

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
                    SqlHelper sql = procData.fillQuery(data, deficiencyLayout);

                    // these are only used when sending data TO tablets and MUST be removed here...
                    foreach (string fld in defSendOnly)
                        sql.Remove(fld);

                    string where = string.Format("WHERE report_id='{0}'", data.ReportId);
                    sql.AddNow("udate");        // required for integration (BWG)
                    if (stmt.DoSQL("SELECT id FROM deficiencies " + where))
                    {
                        Result = stmt.ifetch("id");     // return the ID of the existing record...
                        stmt.DoSQL(sql.BuildCmd("deficiencies", false, where, true));
                    }
                    else
                    {
                        sql.Add("vehicle_id", getVehicleId(stmt, data.HandCode, data.UnitNum));
                        // MUST set second param of DoSQL() to true as this makes it run the insert 
                        // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted id
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO deficiencies", " output inserted.id VALUES "), true);
                        Result = stmt.ifetch("id");     // return the ID of the new record...
                    }

                    if (req.Handcode == 0 && string.IsNullOrEmpty(req.Version))
                        Result = 1;  // OLD TABLETS EXPECT THIS, can remove once all RHILL tablets updated...
                    logUser(stmt, req, data.UserName, true);
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

        [WebMethod(Description = "Receive UserNotes as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendUserNotesJson(Request req, UserNotes data)
        {
            return SendUserNotes(req, data);
        }

        [WebMethod(Description = "Receive User Notes from tablet")]
        public int SendUserNotes(Request req, UserNotes data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendUserNotes"))
                return Result;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -2;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = procData.fillQuery(data, userNotesLayout);

                    string where = string.Format("WHERE hand_code = {0} and rdate = {1}", data.HandCode, SqlHelper.timeStamp(data.ReportTime));
                    sql.AddNow("idate");
                    if (stmt.DoSQL("SELECT id FROM usernotes " + where))
                    {
                        Result = stmt.ifetch("id");     // return the ID of the existing record...
                    }
                    else
                    {
                        sql.Add("vehicle_id", getVehicleId(stmt, data.HandCode, data.UnitNum));
                        // MUST set second param of DoSQL() to true as this makes it run the insert 
                        // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted id
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO usernotes", " output inserted.id VALUES "), true);
                        Result = stmt.ifetch("id");     // return the ID of the new record...
                    }

                    logUser(stmt, req, data.UserName, true);
                }
            }
            catch (Exception ex)
            {
                Result = -3;
                Global.messLog.Flush("SendUserNotes() data failed to receive: " + ex.Message);
                InsertErrorRecord(req.CustCode, string.Format("SendUserNotes() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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
                        string exWh = string.Empty;
                        if (daysBack < 0)
                        {
                            exWh = " and isnull(closed,0)=0";
                            daysBack = -daysBack;
                        }
                        bool rc = stmt.DoSQLFormat("select * from deficiencies where report_time>getdate()-{0} {1} order by report_time desc", daysBack, exWh);
                        while (rc)
                        {
                            DeficiencyData defData = new DeficiencyData();
                            procData.fillClass(defData, deficiencyLayout, stmt);

                            // if there is still something in add_notes, add it to .Note
                            string aNote = stmt.fetch("add_notes");
                            if (aNote != string.Empty)
                            {
                                if (string.IsNullOrEmpty(defData.Note))
                                    defData.Note = string.Empty;
                                else
                                {
                                    if (!(aNote.StartsWith("|") || defData.Note.EndsWith("|")))
                                        aNote = "|" + aNote;
                                }
                                defData.Note += aNote;
                            }

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

        string[,] rwisLayout = { { "LocId", "LocId"},
                                       { "TempDate", "tdate"},
                                       { "SurfaceTemp", "SurfaceTemp"},
                                       { "AirTemp", "AirTemp"},
                                       { "DewPoint", "DewPoint"},
                                       { "Humidity", "Humidity"},
                                       { "ImgDate", "fdate"},
                                       { "ImageUrl", "img_url"} };

        [WebMethod(Description = "Returns all RWIS Data since hrsBack")]
        public RWISData[] GetRWISData(Request req)
        {
            List<RWISData> result = new List<RWISData>();

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "GetRWISData"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        bool rc = stmt.DoSQL("select * from rwis_images");
                        while (rc)
                        {
                            RWISData rwisData = new RWISData();
                            procData.fillClass(rwisData, rwisLayout, stmt);

                            result.Add(rwisData);

                            rc = stmt.dbSkip();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetRWISData() data failed to retrieve: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("GetRWISData() data failed to retrieve HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }


            return result.ToArray();
        }

        [WebMethod(Description = "Return RWIS Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public RWISData[] GetRWISDataJson(Request req)
        {
            return GetRWISData(req);
        }

        #endregion

        [WebMethod(Description = "Return Deficiency Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public DefiPicData[] GetDeficiencyPictureJson(Request req, DeficiencyData[] data)
        {
            return GetDeficiencyPictureData(req, data);
        }

        [WebMethod(Description = "Returns all Deficiency pic Data")]
        public DefiPicData[] GetDeficiencyPictureData(Request req, DeficiencyData[] data)
        {
            List<DefiPicData> result = new List<DefiPicData>();

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "GetDeficiencyPictureData"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        foreach (var item in data)
                        {
                            if (item.ReportId != null && item.ReportId.Length > 0)
                            {
                                DefiPicData pdata = new DefiPicData();
                                List<string> templ = new List<string>();
                                bool rc = stmt.DoSQLFormat("select * from img_events where repkey = '{0}'", item.ReportId);
                                while (rc)
                                {
                                    string repkey = "";
                                    repkey = stmt.fetch("repkey");
                                    templ.Add(repkey);
                                    rc = stmt.dbSkip();
                                }
                                pdata.photos = templ.ToArray();
                                result.Add(pdata);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetDeficiencyPictureData() data failed to retrieve: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("GetDeficiencyPictureData() data failed to retrieve HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return result.ToArray();
        }

        [WebMethod(Description = "Return Deficiency Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendNoteDeficiencyJson(Request req, DeficiencyData data)
        {
            return SendNoteDeficiency(req, data);
        }

        [WebMethod(Description = "Returns all Deficiency pic Data")]
        public int SendNoteDeficiency(Request req, DeficiencyData data)
        {
            int result = -1;

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SendNoteDeficiency"))
            {
                string connStr = CustConnectionString(req);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        result = -2;

                        if (stmt.DoSQL(String.Format("SELECT * FROM deficiencies WHERE report_id = '{0}'", data.ReportId)))
                        {
                            SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                            sql.Add("add_notes", data.addNotes);
                            if (data.Finished)
                                sql.Add("finished", data.Finished);
                            sql.AddNow("udate");
                            stmt.DoSQL(sql.BuildUpdate("UPDATE deficiencies with (rowlock)", string.Format(" WHERE report_id = '{0}'", data.ReportId)));
                            result = 1;

                        }


                    }
                }
                catch (Exception ex)
                {
                    result = -4;
                    Global.messLog.Flush("SendNoteDeficiency() data failed to retrieve: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("SendNoteDeficiency() data failed to retrieve HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return result;
        }

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
                    sql.Add("truck_id", evnt.truck_GS);
                    sql.Add("version", evnt.version_GS);
                    sql.Add("user_id", evnt.driver_GS);
                    sql.Add("last_log", evnt.eventTime);
                    sql.Add("route", evnt.route);
                    sql.Add("pic_q_count", evnt.PicQCount_GS);
                    sql.Add("q_count", evnt.qCount_GS);
                    sql.Add("signal_str", evnt.SignalStr);
                    sql.Add("net_type", evnt.net_type_GS);
                    sql.Add("event_q_count", evnt.EventQCount_GS);
                    sql.AddNow("last_checkin");
                    //sql.Add("in_yard", evnt.inYard);
                    sql.Add("imei", evnt.imei);

                    int hc = evnt.handcode_GS;      // some older phones may not send this, so have to use evnt.handcode
                    if (hc == 0)
                        hc = evnt.handcode;

                    if (stmt.DoSQLFormat("SELECT top 1 idate FROM Img_Events WHERE phand_code = {0} order by idate desc", hc)) //gets the last image time that was sent
                        sql.Add("last_sent", stmt.dtfetch("idate"));

                    string wh = string.Format("WHERE hand_code={0}", hc);
                    bool ins = !stmt.DoSQL("SELECT * FROM go_status " + wh);
                    if (ins)
                        sql.Add("hand_code", hc);
                    stmt.DoSQL(sql.BuildCmd("go_status", ins, wh, true));
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
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "InsertgoEvent"))
                return;

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
                    sql.Add("signature", evnt.signature);
                    sql.Add("q_count", evnt.qCount);
                    sql.Add("signal_str", evnt.SignalStr);
                    sql.Add("net_type", evnt.net_type);
                    sql.Add("pic_q_count", evnt.PicQCount);
                    sql.Add("rdate", evnt.rdate);
                    sql.Add("idate", DateTime.UtcNow);
                    sql.Add("vehicle_id", getVehicleId(stmt, evnt.handcode, evnt.truck));

                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));

                    if (stmt.DoSQLFormat("SELECT * FROM vehicles with (rowlock) WHERE hand_code = {0}", evnt.dcdHC))
                    {
                        if (stmt.dtfetch("last_checkin") < evnt.eventTime)
                        {
                            if (evnt.handcode > 0)
                            {
                                stmt.DoSQLFormat("UPDATE vehicles with (rowlock) SET phone_handcode = null, user_name = null WHERE phone_handcode = {0}", evnt.handcode);
                                stmt.DoSQLFormat("UPDATE vehicles with (rowlock) SET phone_handcode = {0}, user_name = '{1}', last_checkin = {2}  WHERE hand_code = {3}", evnt.handcode, SqlHelper.FixUp(evnt.driver), SqlHelper.timeStamp(evnt.eventTime), evnt.dcdHC);

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

        private void UpdateMessageIn(evntRecord evnt, Request req)
        {
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "UpdateMessageIn"))
                return;

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

            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "GetEventOps"))
                return result;

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
                        DateTime eventthres = DateTime.Now.AddDays(-3);
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

                        string where;
                        if (DCDHc > 0)
                            where = string.Format("dash.hand_code={0}", DCDHc);
                        else
                            where = string.Format("dash.unit_num='{0}'", truck);

                        if (stmt.DoSQL("SELECT dash.op_code, dash.route_id, dash.route_name, routes.name FROM dash with (nolock) left join routes on routes.id = dash.route_id WHERE " + where))
                        {
                            result.plan = stmt.ifetch("op_code");
                            result.routeName = stmt.fetch("name");
                            result.routeID = stmt.ifetch("route_id");
                            result.routeNameYork = stmt.fetch("route_name");//adjusted for MTO pilot 11/06/2018 -jl
                        }
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
                {
                    stmt.Close();
                    stmt = null;
                }
            }

            return result;
        }

        [WebMethod(Description = "Consumes Event records send from Roada Go")]
        public int SendEventRecord(Request req, evntRecord evnt)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendEventRecord"))
                return Result;

            if (evnt == null)
                return -2;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;

            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    InsertgoEvent(evnt, req); //inserts the Recored to go events and updates the vehicle table if needed
                    Result = 1;

                    int hc = evnt.handcode_GS;
                    if (hc == 0)
                        hc = evnt.handcode;

                    if (stmt.DoSQLFormat("SELECT * FROM go_status with (rowlock) WHERE hand_code = {0}", hc)) //checks to see if there is a record to update
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


        [WebMethod(Description = "returns list of amazon urls")]
        public AmazonURL[] GetAmazonUrls(Request req, string RepKey)
        {
            List<AmazonURL> urls = new List<AmazonURL>();
            AmazonURL[] result = null;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "GetAmazonUrls"))
                return null;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    bool rc = stmt.DoSQLFormat("SELECT * FROM Img_Events WHERE RepKey = '{0}' order by id desc", RepKey);

                    while (rc)
                    {
                        AmazonURL url = new AmazonURL();

                        url.Id = stmt.ifetch("id");
                        url.ReportID = RepKey;
                        url.urlString = stmt.fetch("img_url");

                        urls.Add(url);
                        rc = stmt.dbSkip();
                    }

                    result = urls.ToArray();
                }

            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetAmazonUrls() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, string.Format("GetAmazonUrls() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();

            }
            return result;
        }

        [WebMethod(Description = "returns list of amazon image urls, pass repkey in binpassword")]
        public AmazonURL[] GetImageUrls(Request req, string RepId)
        {
            List<AmazonURL> urls = new List<AmazonURL>();
            AmazonURL[] result = null;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "GetImageUrls"))
                return null;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    bool rc = stmt.DoSQLFormat("SELECT * FROM Img_Events WHERE RepKey = '{0}' order by id desc", SqlHelper.FixUp(RepId));

                    while (rc)
                    {
                        AmazonURL url = new AmazonURL();

                        url.Id = stmt.ifetch("id");
                        url.ReportID = RepId;
                        url.urlString = stmt.fetch("img_url");
                        url.rdate = stmt.dtfetch("rdate");
                        url.Latitude = stmt.cfetch("latitude");
                        url.Longitude = stmt.cfetch("longitude");

                        urls.Add(url);
                        rc = stmt.dbSkip();
                    }

                    result = urls.ToArray();
                }

            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetImageUrls() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, string.Format("GetImageUrls() Failed to send HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message), req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();

            }
            return result;
        }
        [WebMethod(Description = "Retrieve vehicle list, optionally filtered by fleetId, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public string GetAppVersionJson(Request req)
        {
            return GetAppVersion(req);
        }

        [WebMethod(Description = "Retrieve vehicle locations, filtered by username for roles, and by contact and/or yard as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public VehicleInfo[] GetVehicleLocationJsonRole(Request req, DateTime lastDate, string username, int contract_no, int yard_no)
        {
            return GetVehicleLocationRole(req, lastDate, username, contract_no, yard_no);
        }

        [WebMethod(Description = "Retrieve customer list")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public AppServerInfo[] GetCustomerListJson(Request req)
        {
            return GetCustomerList(req);
        }

        private AppServerInfo[] GetCustomerList(Request req)
        {
            List<AppServerInfo> Result = new List<AppServerInfo>();
            if (LowSecurityCheck(req, "GetCustomerList"))
            {
                SqlStmt stmt = new SqlStmt("MainConn");
                try
                {
                    stmt.Mess = Global.messLog;

                    bool rc = stmt.DoSQL("SELECT * FROM customer WHERE isnull(go_weburl,'')<>''");
                    while (rc)
                    {
                        AppServerInfo ServerInfo = new AppServerInfo();

                        ServerInfo.customerCode = stmt.fetch("cust_code");
                        ServerInfo.Name = stmt.fetch("name");
                        ServerInfo.WebServiceUrl = stmt.fetch("go_weburl");
                        ServerInfo.PhotoBucket = stmt.fetch("go_photo_bucket");
                        ServerInfo.PickupBucket = stmt.fetch("go_pickup_bucket");
                        ServerInfo.ReportBucket = stmt.fetch("go_report_bucket");
                        ServerInfo.ObjStoreUrl = stmt.fetch("obj_store_url");
                        ServerInfo.AmazonSecretKey = stmt.fetch("amz_secret_key");
                        ServerInfo.AmazonAccessKey = stmt.fetch("amz_access_key");
                        ServerInfo.patrolFrqURL = stmt.fetch("patrol_frq");
                        ServerInfo.SidewalkConditions = stmt.ifetch("sidewalk_cond");

                        Result.Add(ServerInfo);

                        rc = stmt.dbSkip();
                    }
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("GetCustomerList() {0}", ex.Message);
                }
                finally
                {
                    stmt.Close();
                    stmt = null;
                }
            }
            return Result.ToArray();
        }

        [WebMethod(Description = "Retrieve vehicle list, optionally filtered by fleetId, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Vehicle[] GetVehicleListJson(Request req, int? fleetId)
        {
            // can't do OUT parameters in JSON, as far as I've been able to find...
            int vv = 0;
            return GetVehicleList(req, fleetId, out vv);
        }

        [WebMethod(Description = "Retrieve vehicle list, optionally filtered by fleetId, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Vehicle[] GetVehicleListJsonEX(Request req, int? fleetId)
        {
            // can't do OUT parameters in JSON, as far as I've been able to find...
            int vv = 0;
            return GetVehicleListEx(req, fleetId, true, out vv);
        }

        [WebMethod(Description = "Retrieve vehicle list, optionally filtered by fleetId and username for roles, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Vehicle[] GetVehicleListJsonRoleEx(Request req, string username, bool? patrol)
        {
            bool pPatrol = false;
            if (patrol != null)
                pPatrol = (bool)patrol;
            // can't do OUT parameters in JSON, as far as I've been able to find...
            int vv = 0;
            return GetVehicleListRole(req, null, pPatrol, username, out vv);
        }

        [WebMethod(Description = "Retrieve PATROL ONLY vehicle list, optionally filtered by fleetId and username for roles, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Vehicle[] GetVehicleListJsonRole(Request req, string username)
        {
            //, int? fleetId, string username
            // can't do OUT parameters in JSON, as far as I've been able to find...
            int vv = 0;
            return GetVehicleListRole(req, null, true, username, out vv);
        }

        [WebMethod(Description = "Retrieve highway listas JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Highway[] GetHighwayListJson(Request req)
        {
            return GetHighwayList(req);
        }

        [WebMethod(Description = "Retrieve Mobile Role lists JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Role[] GetRoleListJson(Request req)
        {
            return GetRoleList(req);
        }

        [WebMethod(Description = "get photo urls")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public AmazonURL[] GetAmazonUrlJson(Request req, string RepId)
        {
            return GetAmazonUrls(req, RepId);
        }

        [WebMethod(Description = "get photo urls, note the post because RepId has slashes")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public AmazonURL[] GetImageUrlsJson(Request req, string RepId)
        {
            return GetImageUrls(req, RepId);
        }

        [WebMethod(Description = "Retrieve vehicle listing")]
        public Vehicle[] GetVehicleList(Request request, int? fleetId, out int VehicleVersion)
        {
            return GetVehicleListEx(request, fleetId, false, out VehicleVersion);
        }

        [WebMethod(Description = "Retrieve vehicle listing")]
        public Vehicle[] GetVehicleListEx(Request request, int? fleetId, bool patrol, out int VehicleVersion)
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
                        string whereStr = string.Empty;
                        if (fleetId != null)
                        {
                            whereStr = string.Format(" WHERE vehicles.fleet = {0} ", fleetId);
                            if (patrol)
                                whereStr += " AND patrol = 1 ";
                        }
                        else
                        {
                            if (patrol)
                                whereStr = " WHERE patrol = 1 ";
                        }
                        rc = stmt.DoSQLFormat("SELECT vehicles.*,handheld.last_lat, handheld.last_long, handheld.id as hid FROM vehicles LEFT JOIN handheld ON handheld.hand_code = vehicles.hand_code {0} ORDER BY vehicles.unit_num", whereStr);
                        while (rc)
                        {
                            Vehicle v = new Vehicle();
                            v.Id = stmt.ifetch("vehicle");
                            v.fleetID = stmt.ifetch("fleet");
                            v.DevId = stmt.ifetch("hand_code");
                            v.UnitNum = stmt.fetch("unit_num");
                            v.License = stmt.fetch("license");
                            v.Type = stmt.ifetch("type");
                            v.Odom = stmt.dfetch("gps_odom");
                            v.LastLat = stmt.dfetch("last_lat");
                            v.LastLong = stmt.dfetch("last_long");
                            v.HHFlag = !stmt.IsNull("hid");        // new mobile viewer app needs to know if a handheld record exists, if not it ignores the vehicle
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

        private static void getRoleResources(SqlStmt stmt, Role usersRoles)
        {
            getRoleResources(stmt, usersRoles, 0, 0);

        }

        private static void getRoleResources(SqlStmt stmt, Role usersRoles, int contract_no, int dc_no)
        {
            string[] alist = new string[0];
            string[] ylist = new string[0];
            string[] vlist = new string[0];
            string[] nxt;
            bool rc = stmt.DoSQLFormat("SELECT role_access_general.* FROM user_roles LEFT JOIN role_access_general ON role_access_general.vehicles=role_access_general.vehicles where user_roles.role=role_access_general.role and user_roles.username='{0}'", usersRoles.username);
            while (rc)
            {
                nxt = stmt.fetch("areas").Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                alist = alist.Union(nxt).ToArray();
                nxt = stmt.fetch("yards").Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                ylist = ylist.Union(nxt).ToArray();
                nxt = stmt.fetch("vehicles").Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                vlist = vlist.Union(nxt).ToArray();

                rc = stmt.dbSkip();
            }

            if (contract_no + dc_no > 0)
            {
                // get the 'link' between vehicle/yard and vehicle/contract
                List<string> units = new List<string>(vlist);
                rc = stmt.DoSQL("select v.unit_num,d.id as dc_no,y.yard_no as contract_no from vehicles v left join fleet f on v.fleet=f.id left join distribution_center d on f.distribution_id=d.id left join yard y on d.yard_no=y.yard_no");
                while (rc)
                {
                    string unit = stmt.fetch("unit_num");

                    if (units.Contains(unit))
                    {
                        int cont = stmt.ifetch("contract_no");
                        int dc = stmt.ifetch("dc_no");

                        if ((contract_no > 0 && cont != contract_no) || (dc_no > 0 && dc != dc_no))
                            units.Remove(unit);
                    }

                    rc = stmt.dbSkip();
                }
                vlist = units.ToArray();
            }
            usersRoles.Clist = alist;
            usersRoles.DClist = ylist;
            usersRoles.Vlist = vlist;
        }

        [WebMethod(Description = "Retrieve vehicle locations")]
        public VehicleInfo[] GetVehicleLocationRole(Request request, DateTime lastDate, string username, int contract_no, int yard_no)
        {
            VehicleInfo[] Result = null;
            Role usersRoles = new Role();
            usersRoles.username = string.Empty;
            if (LowSecurityCheck(request, "GetVehicleLocationRole"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<VehicleInfo> vehicles = new List<VehicleInfo>();

                        SqlHelper qwhere = new SqlHelper();
                        if (username != null && username.Length > 0 && username.ToLower() != "dmtadm")
                        {
                            usersRoles.username = username;

                            getRoleResources(stmt, usersRoles, contract_no, yard_no);

                            qwhere.Add("v.unit_num", new ArrayList(usersRoles.Vlist));        // unitWhere = "unit_num in ('" + string.Join("','", usersRoles.Vlist) + "')";
                        }
                        else
                        {
                            if (contract_no + yard_no > 0)
                            {
                                if (yard_no > 0)
                                    qwhere.Add("d.id", yard_no);    //exWhere = string.Format("dc_no={0}", yard_no);
                                else if (contract_no > 0)
                                    qwhere.Add("y.yard_no", contract_no); //exWhere = string.Format("contract_no={0}", contract_no);
                            }
                        }
                        DateTime chkDate = DateTime.MinValue;
                        // new Date(0) in Javascript (React) is Jan 1st 1970
                        if (lastDate != null && lastDate != DateTime.MinValue && lastDate != new DateTime(1970, 1, 1))
                        {
                            // faster to do the select without this 'where' and filter out when reading
                            chkDate = lastDate;
                            //int ndx = qwhere.Add("last_date", SqlHelper.timeStamp(lastDate), false);      // where += string.Format("WHERE last_date>={0}", SqlHelper.timeStamp(lastDate));
                            //qwhere.SetCompare(ndx - 1, ">=");
                        }


                        string where = qwhere.BuildWhere();
                        if (where != string.Empty)
                            where = " WHERE " + where;

                        bool rc = stmt.DoSQL("select h.id as hid,h.hand_code,h.last_lat,h.last_long,h.last_date,v.vehicle,v.unit_num,v.gps_odom,m.degrees,m.speed,m.temp_air,m.temp_road from vehicles v " +
                                                 "LEFT JOIN handheld h on v.hand_code=h.hand_code " +
                                                 "LEFT JOIN messagein m with (nolock) ON m.hand_code=v.hand_code AND m.rdate=h.last_date " +
                                                 "LEFT JOIN fleet f on v.fleet=f.id " +
                                                 "LEFT JOIN distribution_center d on f.distribution_id=d.id " +
                                                 "LEFT JOIN yard y on d.yard_no=y.yard_no" + where);
                        while (rc)
                        {
                            DateTime rdate = stmt.dtfetch("last_date");
                            if (chkDate == DateTime.MinValue || rdate >= lastDate)
                            {
                                VehicleInfo v = new VehicleInfo();
                                v.Id = stmt.ifetch("vehicle");
                                v.UnitNum = stmt.fetch("unit_num");
                                v.LastGPS = rdate;
                                v.LastLat = stmt.dfetch("last_lat");
                                v.LastLong = stmt.dfetch("last_long");
                                v.degrees = stmt.ifetch("degrees");
                                v.speed = stmt.ifetch("speed");
                                v.tempAir = stmt.dfetch("temp_air");
                                v.tempRoad = stmt.dfetch("temp_road");
                                v.Odom = stmt.dfetch("gps_odom");

                                vehicles.Add(v);
                            }
                            rc = stmt.dbSkip();
                        }
                        Result = vehicles.ToArray();
                        vehicles.Clear();
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

        private void vehicleSql(SqlStmt stmt, string whereStr, string para, List<Vehicle> vehicles)
        {
            bool rc = stmt.DoSQLFormat("SELECT vehicles.*,handheld.id as hid,handheld.last_lat,handheld.last_long,handheld.last_date,fleet.distribution_id as dc_id FROM vehicles LEFT JOIN handheld ON handheld.hand_code=vehicles.hand_code LEFT JOIN fleet on vehicles.fleet=fleet.id " + whereStr + " ORDER BY vehicles.unit_num", para);
            while (rc)
            {
                Vehicle v = new Vehicle();
                v.Id = stmt.ifetch("vehicle");
                v.fleetID = stmt.ifetch("fleet");
                v.DcId = stmt.ifetch("dc_id");
                v.DevId = stmt.ifetch("hand_code");
                v.UnitNum = stmt.fetch("unit_num");
                v.License = stmt.fetch("license");
                v.Type = stmt.ifetch("type");
                v.Odom = stmt.dfetch("gps_odom");
                v.LastGPS = stmt.dtfetch("last_date");
                v.LastLat = stmt.dfetch("last_lat");
                v.LastLong = stmt.dfetch("last_long");
                v.HHFlag = !stmt.IsNull("hid");        // new mobile viewer app needs to know if a handheld record exists, if not it ignores the vehicle

                vehicles.Add(v);

                rc = stmt.dbSkip();
            }
        }

        [WebMethod(Description = "Retrieve vehicle listing")]
        public Vehicle[] GetVehicleListRole(Request request, int? fleetId, bool patrol, string username, out int VehicleVersion)
        {
            Role usersRoles = new Role();
            usersRoles.username = string.Empty;
            Vehicle[] Result = null;
            VehicleVersion = 0;
            if (LowSecurityCheck(request, "GetVehicleListRole"))
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
                        if (username != null && username.Length > 0 && username.ToLower() != "dmtadm")
                        {
                            usersRoles.username = username;

                            getRoleResources(stmt, usersRoles);

                            // if a username is sent that has NO vehicles in it's roles then it would get ALL vehicles!?
                            // it SHOULD send NO vehicles and now it will...

                            //}
                            //if (usersRoles.Vlist.Length > 0)                
                            //{

                            string whereStr = "WHERE vehicles.unit_num in ('{0}')";
                            if (fleetId != null)
                                whereStr += string.Format(" AND vehicles.fleet={0}", fleetId);
                            if (patrol)
                                whereStr += " AND patrol=1";

                            // some users have HUNDREDS of vehicles!?  This could be a bit slow...
                            // foreach (var unit in usersRoles.Vlist)
                            // so do it in 'chunks'
                            int size = 255;

                            string[] buffer;
                            for (int i = 0; i < usersRoles.Vlist.Length; i += size)
                            {
                                if (i + size > usersRoles.Vlist.Length)
                                    size = usersRoles.Vlist.Length - i;

                                buffer = new string[size];
                                Array.Copy(usersRoles.Vlist, i, buffer, 0, size);

                                string units = string.Join("','", buffer);

                                vehicleSql(stmt, whereStr, units, vehicles);
                            }

                            Result = vehicles.ToArray();
                            vehicles.Clear();
                        }
                        else
                        {
                            string whereStr = string.Empty;
                            if (fleetId != null)
                            {
                                whereStr = string.Format("WHERE vehicles.fleet={0} ", fleetId);
                                if (patrol)
                                    whereStr += " AND patrol=1";
                            }
                            else
                            {
                                if (patrol)
                                    whereStr = "WHERE patrol=1";
                            }

                            vehicleSql(stmt, whereStr, "", vehicles);

                            Result = vehicles.ToArray();
                            vehicles.Clear();
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetVehicleListRole() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetVehicleListRole() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve list of yards as JSON filtered by roles for a user (optional)")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Contract[] GetContractListJsonRole(Request req, string username)
        {
            return GetContractListRole(req, username);
        }

        [WebMethod(Description = "Retrieve Contract listing")]
        public Contract[] GetContractListRole(Request request, string username)
        {
            Role usersRoles = new Role();
            usersRoles.username = string.Empty;
            Contract[] Result = null;
            if (LowSecurityCheck(request, "GetContractListRole"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Contract> contracts = new List<Contract>();
                        string whereStr = string.Empty;
                        if (username != null && username.Length > 0 && username.ToLower() != "dmtadm")
                        {
                            usersRoles.username = username;

                            getRoleResources(stmt, usersRoles);
                            if (usersRoles.Clist.Length > 0)
                                whereStr = string.Format("WHERE yard_no in ({0})", string.Join(",", usersRoles.Clist));
                            else
                                whereStr = "WHERE 1=0";     // if none found, show none?
                        }

                        bool rc = stmt.DoSQLFormat("SELECT * FROM yard " + whereStr + " ORDER BY yard_no");
                        while (rc)
                        {
                            Contract v = new Contract();
                            v.Id = stmt.ifetch("yard_no");
                            v.Name = stmt.fetch("name");

                            contracts.Add(v);

                            rc = stmt.dbSkip();
                        }
                        Result = contracts.ToArray();
                        contracts.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetContractListRole() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetContractListRole() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve highway listing")]
        public Highway[] GetHighwayList(Request request)
        {
            Highway[] Result = null;
            if (LowSecurityCheck(request, "GetHighwayList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Highway> highways = new List<Highway>();
                        bool rc = stmt.DoSQL("SELECT * FROM highways ORDER BY id");
                        while (rc)
                        {
                            Highway w = new Highway();
                            w.key = stmt.fetch("id");
                            w.value = stmt.fetch("name");
                            highways.Add(w);

                            rc = stmt.dbSkip();
                        }
                        Result = highways.ToArray();
                        highways.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetHighwayList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetHighwayList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Role listing")]
        public Role[] GetRoleList(Request request)
        {
            //need to get user_roles and role_access_general to get a list of the vehicles as per role
            Role[] Result = null;
            if (LowSecurityCheck(request, "GetRoleList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Role> role = new List<Role>();
                        bool rc = stmt.DoSQL("SELECT * FROM user_roles ORDER BY id");
                        while (rc)
                        {
                            Role r = new Role();
                            r.username = stmt.fetch("username");
                            r.role = stmt.fetch("role");
                            role.Add(r);

                            rc = stmt.dbSkip();
                        }
                        Result = role.ToArray();
                        role.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetRoleList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetRoleList() Failed! HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve App Version Info")]
        public string GetAppVersion(Request request)
        {
            string Result = string.Empty;
            if (LowSecurityCheck(request, "GetAppVersion"))
            {
                SqlStmt stmt = null;
                try
                {
                    stmt = new SqlStmt("MainConn");
                    stmt.Mess = Global.messLog;
                    stmt.DoSQL("SELECT TOP 1 app_version FROM react_settings ORDER BY id ");
                    Result = stmt.fetch("app_version");
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetAppVersion() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

        // first string is name in class, second is name in database
        string[,] bridgeLayout = { { "bridge", "bridge" },
                                   { "structure", "structure" },
                                   { "road", "road" },
                                   { "location", "location" },
                                   { "name", "name" },
                                   { "site", "site" },
                                   { "type","type" },
                                   { "type_code","type_code" },
                                   { "spans", "spans"},
                                   { "length", "length" },
                                   { "width", "width" },
                                   { "latitude", "latitude" },
                                   { "longitude", "longitude" },
                                   { "notes", "notes" } };

        // first string is name in class, second is name in database
        string[,] HighmastLayout = {
                                       { "structure", "structure" },
                                       { "powerSupplyLocation", "power_supply_location" },
                                       { "poleNumber", "pole_number" },
                                       { "contractNum", "contract_num" },
                                       { "poleManu", "pole_manu" },
                                       { "dateManu","date_manu" },
                                       { "luminaireManu","luminaire_manu" },
                                       { "luminaireDist", "luminaire_dist"},
                                       { "loweringDevice", "lowering_device" },
                                       { "latching", "latching" },
                                       { "drive", "drive" },
                                       { "lineVolts", "line_volts" },
                                       { "phase", "phase" },
                                       { "numFixtures", "num_fixtures" },
                                       { "raisedLowerd", "raised_lowerd" } ,
                                       { "luminaireVolt", "luminaire_volt" } ,
                                       { "breakerSize", "breaker_size" } ,
                                       { "notes", "notes" } ,
                                       { "type", "type" } ,
                                       { "latitude", "latitude" },
                                       { "longitude", "longitude" }

                                   };

        [WebMethod(Description = "Add a HighMast")]
        public int AddHighMast(Request request, HighMast data)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddHighMast"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (data != null && data.structure != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a highmast, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset__high_mast_lite WHERE structure='{0}'", data.structure))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(data, HighmastLayout);
                                sql.AddNow("idate");

                                if (stmt.DoSQL(sql.BuildCmd("asset__high_mast_lite", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddHighMast() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddHighMast() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Bridge fillBridge(SqlStmt stmt)
        {
            Bridge b = new Bridge();
            b.bridge = stmt.fetch("bridge");
            b.structure = stmt.fetch("structure");
            b.location = stmt.fetch("location");
            b.name = stmt.fetch("name");
            b.site = stmt.fetch("site");
            b.type = stmt.fetch("type");
            b.type_code = stmt.ifetch("type_code");
            b.spans = stmt.ifetch("bridge");
            b.length = stmt.dfetch("bridge");
            b.width = stmt.dfetch("width");
            b.latitude = stmt.dfetch("latitude");
            b.longitude = stmt.dfetch("longitude");
            b.notes = stmt.fetch("notes");
            b.date = stmt.dtfetch("date");

            return b;
        }

        private HighMast fillHighmast(SqlStmt stmt)
        {
            HighMast hm = new HighMast();


            hm.structure = stmt.fetch("structure");
            hm.powerSupplyLocation = stmt.fetch("power_supply_location");
            hm.poleNumber = stmt.fetch("pole_number");
            hm.contractNum = stmt.fetch("contract_num");
            hm.poleManu = stmt.fetch("pole_manu");
            hm.dateManu = stmt.dtfetch("date_manu");
            hm.luminaireManu = stmt.fetch("luminaire_manu");
            hm.luminaireDist = stmt.fetch("luminaire_dist");
            hm.loweringDevice = stmt.fetch("lowering_device");
            hm.phase = stmt.fetch("phase");
            hm.numFixtures = stmt.ifetch("num_fixtures");
            hm.raisedLowerd = stmt.ifetch("raisedL_lowerd");
            hm.luminaireVolt = stmt.fetch("luminaire_volt");
            hm.breakerSize = stmt.fetch("breaker_size");
            hm.note = stmt.fetch("notes");
            hm.latitude = stmt.dfetch("latitude");
            hm.longitude = stmt.dfetch("longitude");


            return hm;
        }

        [WebMethod(Description = "Add a Bridge")]
        public int AddBridge(Request request, Bridge bdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddBridge"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (bdata != null && bdata.bridge != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a bridge, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_bridge WHERE bridge='{0}'", bdata.bridge))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(bdata, bridgeLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_bridge", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddBridge() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddBridge() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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


        [WebMethod(Description = "Retrieve Bridges")]
        public Bridge[] GetBridgeList(Request request, int type)
        {
            Bridge[] Result = null;

            if (LowSecurityCheck(request, "GetBridgeList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Bridge> Bridges = new List<Bridge>();

                        string sqlStr = string.Format("SELECT * FROM asset_bridge WHERE type_code = {0} ORDER BY bridge", type);
                        bool rc = stmt.DoSQL(sqlStr);

                        while (rc)
                        {
                            Bridges.Add(fillBridge(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Bridges.ToArray();
                        Bridges.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetBridgeList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBridgeList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private string getSQLNear(string table, string structure, string searchStr, double lat, double lon, int numRows)
        {
            return getSQLNear(table, structure, searchStr, lat, lon, numRows, string.Empty, 0);
        }

        private string getSQLNear(string table, string structure, string searchStr, double lat, double lon, int numRows, string type)
        {
            return getSQLNear(table, structure, searchStr, lat, lon, numRows, string.Empty, 0);
        }

        private string getSQLNear(string table, string structure, string searchStr, double lat, double lon, int numRows, string type, int contract)
        {
            string whereClause = String.Empty;
            if (searchStr != string.Empty)
                whereClause += string.Format(" WHERE a.{0} LIKE '{1}'", structure, SqlHelper.FixUp(searchStr));
            if (type != string.Empty)
                whereClause += ((whereClause == string.Empty) ? " WHERE" : " AND") + (" a." + type);
            if (lat != 0.0 && lon != 0.0)
                whereClause += ((whereClause == string.Empty) ? " WHERE" : " AND") + " (a.latitude is not null and a.longitude is not null)";
            if (contract > 0)
                whereClause += ((whereClause == string.Empty) ? " WHERE" : " AND") + string.Format(" (contract={0})", contract);

            string orderBy = " ORDER BY " + ((lat != 0.0 && lon != 0.0) ? string.Format(" geography::Point({0}, {1}, 4326).STDistance(geography::Point(a.latitude, a.longitude, 4326)),a.id", lat, lon) : "a." + structure);
            string distField = (lat != 0.0 && lon != 0.0) ? string.Format(",geography::Point({0}, {1}, 4326).STDistance(geography::Point(a.latitude, a.longitude, 4326)) as dist", lat, lon) : string.Empty;

            // don't include walkabouts in the inspection_xxx join
            string inspTypeFilter = string.Empty;
            if (table == "bridge")
                inspTypeFilter = " where type_code<>20";
            else if (table == "culvert")
                inspTypeFilter = " where type<>25";
            // NOTE: i.date has to be first as all the asset_xxx tables also have a field called date,
            //       but fetch("date") will always get the first one in the result set
            return string.Format("SELECT TOP {0} i.date,a.*{1} FROM asset_{2} a left join (select max(date) as date,{3} from inspection_{2}{4} group by {3}) as i on a.{3}=i.{3}", numRows, distField, table, structure, inspTypeFilter) + whereClause + orderBy;
        }

        private string OLDgetSQLNear(string table, string structure, string searchStr, double lat, double lon, int numRows, string type)
        {
            string whereClause = " WHERE i2.date is null";
            if (searchStr != string.Empty)
                whereClause += string.Format(" AND a.{0} LIKE '{1}'", structure, SqlHelper.FixUp(searchStr));
            if (type != string.Empty)
                whereClause += (" AND a." + type);
            if (lat != 0.0 && lon != 0.0)
                whereClause += " AND (a.latitude is not null and a.longitude is not null)";
            string orderBy = " ORDER BY " + ((lat != 0.0 && lon != 0.0) ? string.Format(" geography::Point({0}, {1}, 4326).STDistance(geography::Point(a.latitude, a.longitude, 4326)),a.id", lat, lon) : "a." + structure);
            string distField = (lat != 0.0 && lon != 0.0) ? string.Format(",geography::Point({0}, {1}, 4326).STDistance(geography::Point(a.latitude, a.longitude, 4326)) as dist", lat, lon) : string.Empty;

            // all this 'gookyness' is to get the most recent inspection date for each asset
            // NOTE: i.date has to be first as all the asset_xxx tables also have a field called date,
            //       but fetch("date") will always get the first one in the result set
            return string.Format("SELECT TOP {0} i.date,a.*{1} FROM asset_{2} a inner join inspection_{2} i on a.{3}=i.{3} left outer join inspection_{2} i2 on a.{3}=i2.{3} AND i2.date>i.date", numRows, distField, table, structure) + whereClause + orderBy;
        }

        [WebMethod(Description = "Retrieve Highmast closest to lat/lon pair for maxRows")]
        public HighMast[] GetHighMastListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            HighMast[] Result = null;

            if (LowSecurityCheck(request, "GetHighMastListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sqlStr = getSQLNear("_high_mast_lite", "structure", searchStr, lat, lon, maxRows + startRow, string.Empty);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                            {
                                if (typeCode != string.Empty)
                                    typeCode = typeCode.Replace("AND ", "WHERE ");
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_bridge {1} ORDER BY bridge", maxRows + startRow, typeCode);
                            }
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_bridge WHERE bridge LIKE '{1}' {2} ORDER BY bridge", maxRows + startRow, SqlHelper.FixUp(searchStr), typeCode);
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_bridge WHERE latitude is not null and longitude is not null {3} ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, typeCode);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_bridge WHERE latitude is not null and longitude is not null and bridge LIKE '{3}' {4} ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr), typeCode);
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<HighMast> Highmast = new List<HighMast>();
                        while (rc)
                        {
                            Highmast.Add(fillHighmast(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Highmast.ToArray();
                        Highmast.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetHighMastListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetHighMastListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Route Segment list")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Segment[] RequestRoutesJson(Request req, string routeId)
        {
            return GetRouteSegments(req, routeId);
        }

        [WebMethod(Description = "Retrieve Route Names/Id list")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public string[][] RequestRouteNamesJson(Request req)
        {
            return GetRouteNames(req);
        }

        public string[][] GetRouteNames(Request request)
        {
            string[][] Result = new string[0][];

            if (LowSecurityCheck(request, "GetRouteSegments"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<string[]> routeList = new List<string[]>();
                        bool rc = stmt.DoSQL("select * from roadaRoutes");
                        while (rc)
                        {
                            routeList.Add(new string[] { stmt.fetch("route_name"), stmt.fetch("route_id") });

                            rc = stmt.dbSkip();
                        }

                        Result = routeList.ToArray();
                        routeList.Clear();
                    }
                }
                finally
                {
                    stmt.Close();
                    stmt = null;
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Road Segment list")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Segment[] RequestSegmentsJson(Request req)
        {
            return GetSegmentList(req);
        }

        private class APoint
        {
            public double latitude { get; set; }
            public double longitude { get; set; }
        }

        private string convertGeogText(string Geog)
        {
            string rc = string.Empty;
            List<APoint> aPoints = new List<APoint>();
            double llat = 0.0, llon = 0.0;
            string[] points = Geog.Replace("), (", "),(").Replace("LINESTRING (", "LINESTRING(").Replace("MULTILINESTRING(", "").Replace("LINESTRING(", "").Split(new char[] { ')', ',', '(' });
            foreach (string point in points)
            {
                string[] ll = point.Trim(' ').Split(' ');
                if (ll.Length > 1)
                {
                    double latitude = Math.Round(myConvert.ToDouble(ll[1]), 5, MidpointRounding.AwayFromZero);
                    double longitude = Math.Round(myConvert.ToDouble(ll[0]), 5, MidpointRounding.AwayFromZero);
                    if (latitude != llat && longitude != llon)
                    {
                        llat = latitude;
                        llon = longitude;
                        aPoints.Add(new APoint() { latitude = llat, longitude = llon });
                    }
                }
            }
            return JsonConvert.SerializeObject(aPoints);
        }

       /*
         * create table roadaRouting (
        id int identity(1,1),
        route_id varchar(64),
        id_field varchar(64),
        seg_name_full varchar(255),
        next_segment varchar(255),
        seg_info varchar(255),
        geog geometry
        );*/
        public Segment[] GetRouteSegments(Request request, string routeId)
        {
            Segment[] Result = null;

            if (LowSecurityCheck(request, "GetRouteSegments"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Segment> Segments = new List<Segment>();

                        bool rc = stmt.DoSQLFormat("select route_id,id_field,seg_name_full,next_segment,seg_info,geography::STGeomFromWKB(geog.STAsBinary(),4326).STAsText() as geog from roadaRouting where route_id='{0}'", routeId);
                        while (rc)
                        {
                            string seg = convertGeogText(stmt.fetch("geog"));
                            Segments.Add(new Segment() { routeId = stmt.fetch("route_id"), segmentId = stmt.fetch("id_field"), name = stmt.fetch("seg_name_full"), nextSegment = stmt.fetch("next_segment"), segmentInfo = stmt.fetch("seg_info"), segment = seg });

                            rc = stmt.dbSkip();
                        }

                        Result = Segments.ToArray();
                        Segments.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetRouteSegments() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetRouteSegments() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        public Segment[] GetSegmentList(Request request)
        {
            Segment[] Result = null;

            if (LowSecurityCheck(request, "GetSegmentList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Segment> Segments = new List<Segment>();
                        //bool rc = stmt.DoSQL("select seg_name_full,geog.STAsText() as geog from baseSegments");
                        //
                        bool rc = stmt.DoSQL("select id_field,seg_name_full,geography::STGeomFromWKB(geog.STAsBinary(),4326).STAsText() as geog from baseSegments");
                        while (rc)
                        {
                            string seg = convertGeogText(stmt.fetch("geog"));
                            Segments.Add(new Segment() { segmentId = stmt.fetch("id_field"), name = stmt.fetch("seg_name_full"), segment = seg });

                            rc = stmt.dbSkip();
                        }

                        Result = Segments.ToArray();
                        Segments.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetSegmentList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetSegmentList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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


        [WebMethod(Description = "Retrieve Bridges closest to lat/lon pair for maxRows")]
        public Bridge[] GetBridgeListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows, int type)
        {
            Bridge[] Result = null;

            if (LowSecurityCheck(request, "GetBridgeListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string typeCode = (type > 0) ? string.Format("type_code={0}", type) : string.Empty;
                        string sqlStr = getSQLNear("bridge", "bridge", searchStr, lat, lon, maxRows + startRow, typeCode);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                            {
                                if (typeCode != string.Empty)
                                    typeCode = typeCode.Replace("AND ", "WHERE ");
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_bridge {1} ORDER BY bridge", maxRows + startRow, typeCode);
                            }
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_bridge WHERE bridge LIKE '{1}' {2} ORDER BY bridge", maxRows + startRow, SqlHelper.FixUp(searchStr), typeCode);
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_bridge WHERE latitude is not null and longitude is not null {3} ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, typeCode);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_bridge WHERE latitude is not null and longitude is not null and bridge LIKE '{3}' {4} ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr), typeCode);
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<Bridge> Bridges = new List<Bridge>();
                        while (rc)
                        {
                            Bridges.Add(fillBridge(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Bridges.ToArray();
                        Bridges.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetBridgeListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBridgeListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Wall fillWall(SqlStmt stmt)
        {
            Wall p = new Wall();
            p.location = stmt.fetch("location");
            p.site = stmt.fetch("structure");
            p.name = stmt.fetch("name");
            p.road = stmt.fetch("road");
            p.latitude = stmt.dfetch("latitude");
            p.longitude = stmt.dfetch("longitude");
            p.notes = stmt.fetch("notes");
            p.date = stmt.dtfetch("date");

            return p;
        }

        [WebMethod(Description = "Retrieve Walls")]
        public Wall[] GetWallList(Request request)
        {
            Wall[] Result = null;

            if (LowSecurityCheck(request, "GetWallList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Wall> Walls = new List<Wall>();
                        bool rc = stmt.DoSQL("SELECT * FROM asset_wall ORDER BY structure");
                        while (rc)
                        {
                            Walls.Add(fillWall(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Walls.ToArray();
                        Walls.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetWallList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetWallList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Walls closest to lat/lon pair for maxRows")]
        public Wall[] GetWallListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            Wall[] Result = null;

            if (LowSecurityCheck(request, "GetWallListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sqlStr = getSQLNear("wall", "structure", searchStr, lat, lon, maxRows + startRow);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_wall ORDER BY structure", maxRows + startRow);
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_wall WHERE structure LIKE '{1}' ORDER BY structure", maxRows + startRow, SqlHelper.FixUp(searchStr));
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_wall WHERE latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_wall WHERE latitude is not null and longitude is not null and structure LIKE '{3}' ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr));
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<Wall> Walls = new List<Wall>();
                        while (rc)
                        {
                            Walls.Add(fillWall(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Walls.ToArray();
                        Walls.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetWallListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetWallListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        // first string is name in class, second is name in database
        string[,] wallLayout = { { "site", "structure" },
                                 { "road", "road" },
                                 { "location", "location" },
                                 { "name", "name" },
                                 { "length", "length" },
                                 { "latitude", "latitude" },
                                 { "longitude", "longitude" },
                                 { "notes", "notes" } };

        [WebMethod(Description = "Add a Retaining Wall")]
        public int AddWall(Request request, Wall wdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddWall"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (wdata != null && wdata.site != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a bridge, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_wall WHERE structure='{0}'", wdata.site))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(wdata, wallLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_wall", true)))
                                    Result = 1;
                            }
                        }
                        else
                            Global.messLog.Flush("AddWall() HandCode:{0} wdata or wdata.site are empty", request.Handcode);
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddWall() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddWall() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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


        // first string is name in class, second is name in database
        string[,] pondLayout = { { "structure", "structure" },
                                 { "road", "road" },
                                 { "location", "location" },
                                 { "name", "name" },
                                 { "site", "site" },
                                 { "area","area" },
                                 { "size", "size" },
                                 { "latitude", "latitude" },
                                 { "longitude", "longitude" },
                                 { "notes", "notes" } };

        [WebMethod(Description = "Add a Mngmt Pond")]
        public int AddPond(Request request, Pond pdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddPond"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (pdata != null && pdata.structure != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a bridge, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_pond WHERE structure='{0}'", pdata.structure))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(pdata, pondLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_pond", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddPond() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddPond() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Pond fillPond(SqlStmt stmt)
        {
            Pond p = new Pond();
            p.structure = stmt.fetch("structure");
            p.location = stmt.fetch("location");
            p.site = stmt.fetch("site");
            p.area = stmt.fetch("area");
            p.size = stmt.fetch("size");
            p.name = stmt.fetch("name");
            p.latitude = stmt.dfetch("latitude");
            p.longitude = stmt.dfetch("longitude");
            p.notes = stmt.fetch("notes");
            p.date = stmt.dtfetch("date");

            return p;
        }

        [WebMethod(Description = "Retrieve Ponds")]
        public Pond[] GetPondList(Request request)
        {
            Pond[] Result = null;

            if (LowSecurityCheck(request, "GetPondList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Pond> Ponds = new List<Pond>();
                        bool rc = stmt.DoSQL("SELECT * FROM asset_pond ORDER BY structure");
                        while (rc)
                        {
                            Ponds.Add(fillPond(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Ponds.ToArray();
                        Ponds.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetPondList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetPondList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Ponds closest to lat/lon pair for maxRows")]
        public Pond[] GetPondListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            Pond[] Result = null;

            if (LowSecurityCheck(request, "GetPondListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sqlStr = getSQLNear("pond", "structure", searchStr, lat, lon, maxRows + startRow);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_pond ORDER BY structure", maxRows + startRow);
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_pond WHERE structure LIKE '{1}' ORDER BY structure", maxRows + startRow, SqlHelper.FixUp(searchStr));
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_pond WHERE latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_pond WHERE latitude is not null and longitude is not null and structure LIKE '{3}' ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr));
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<Pond> Ponds = new List<Pond>();
                        while (rc)
                        {
                            Ponds.Add(fillPond(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Ponds.ToArray();
                        Ponds.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetPondListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetPondListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        // first string is name in class, second is name in database
        string[,] beaverLayout = { { "structure", "structure" },
                                   { "road", "road" },
                                   { "location", "location" },
                                   { "area","area" },
                                   { "size", "size"},
                                   { "name", "name" },
                                   { "site", "site" },
                                   { "latitude", "latitude" },
                                   { "longitude", "longitude" },
                                   { "notes", "notes" } };

        [WebMethod(Description = "Add a Beaver Pond")]
        public int AddBeaver(Request request, Beaver bdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddBeaver"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (bdata != null && bdata.structure != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a beaver pond, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_beaver WHERE structure='{0}'", bdata.structure))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(bdata, beaverLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_beaver", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddBeaver() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddBeaver() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Beaver fillBeaver(SqlStmt stmt)
        {
            Beaver b = new Beaver();
            b.structure = stmt.fetch("structure");
            b.location = stmt.fetch("location");
            b.name = stmt.fetch("name");
            b.road = stmt.fetch("road");
            b.latitude = stmt.dfetch("latitude");
            b.longitude = stmt.dfetch("longitude");
            b.notes = stmt.fetch("notes");
            b.date = stmt.dtfetch("date");

            return b;
        }

        [WebMethod(Description = "Retrieve Beavers")]
        public Beaver[] GetBeaverList(Request request)
        {
            Beaver[] Result = null;

            if (LowSecurityCheck(request, "GetBeaverList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Beaver> Beavers = new List<Beaver>();
                        bool rc = stmt.DoSQL("SELECT * FROM asset_beaver ORDER BY structure");
                        while (rc)
                        {
                            Beavers.Add(fillBeaver(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Beavers.ToArray();
                        Beavers.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetBeaverList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBeaverList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Beavers closest to lat/lon pair for maxRows")]
        public Beaver[] GetBeaverListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            Beaver[] Result = null;

            if (LowSecurityCheck(request, "GetBeaverListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sqlStr = getSQLNear("beaver", "structure", searchStr, lat, lon, maxRows + startRow);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_beaver ORDER BY structure", maxRows + startRow);
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_beaver WHERE structure LIKE '{1}' ORDER BY structure", maxRows + startRow, SqlHelper.FixUp(searchStr));
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_beaver WHERE latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_beaver WHERE latitude is not null and longitude is not null and structure LIKE '{3}' ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr));
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<Beaver> Beavers = new List<Beaver>();
                        while (rc)
                        {
                            Beavers.Add(fillBeaver(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Beavers.ToArray();
                        Beavers.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetBeaverListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBeaverListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        // first string is name in class, second is name in database
        string[,] fenceLayout = { { "structure", "structure" },
                                  { "road", "road" },
                                  { "location", "location" },
                                  { "name", "name" },
                                  { "site", "site" },
                                  { "area","area" },
                                  { "length", "length" },
                                  { "latitude", "latitude" },
                                  { "longitude", "longitude" },
                                  { "notes", "notes" } };

        [WebMethod(Description = "Add a Bridge")]
        public int AddFence(Request request, Fence fdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddFence"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (fdata != null && fdata.structure != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a bridge, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_fence WHERE structure='{0}'", fdata.structure))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(fdata, fenceLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_fence", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddFence() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddFence() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Fence fillFence(SqlStmt stmt)
        {
            Fence f = new Fence();
            f.structure = stmt.fetch("structure");
            f.name = stmt.fetch("name");
            f.road = stmt.fetch("road");
            f.site = stmt.fetch("site");
            f.area = stmt.fetch("area");
            f.length = stmt.fetch("length");
            f.location = stmt.fetch("location");
            f.latitude = stmt.dfetch("latitude");
            f.longitude = stmt.dfetch("longitude");
            f.notes = stmt.fetch("notes");
            f.date = stmt.dtfetch("date");

            return f;
        }

        [WebMethod(Description = "Retrieve Fence and Fencing")]
        public Fence[] GetFenceList(Request request)
        {
            Fence[] Result = null;

            if (LowSecurityCheck(request, "GetFenceList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Fence> Fences = new List<Fence>();
                        bool rc = stmt.DoSQL("SELECT * FROM asset_fence ORDER BY structure");
                        while (rc)
                        {
                            Fences.Add(fillFence(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Fences.ToArray();
                        Fences.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetFenceList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetFenceList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Fence Fencing closest to lat/lon pair for maxRows")]
        public Fence[] GetFenceListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            Fence[] Result = null;

            if (LowSecurityCheck(request, "GetFenceListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sqlStr = getSQLNear("fence", "structure", searchStr, lat, lon, maxRows + startRow);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_fence ORDER BY structure", maxRows + startRow);
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_fence WHERE structure LIKE '{1}' ORDER BY structure", maxRows + startRow, SqlHelper.FixUp(searchStr));
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_fence WHERE latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_fence WHERE latitude is not null and longitude is not null and structure LIKE '{3}' ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr));
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<Fence> Fences = new List<Fence>();
                        while (rc)
                        {
                            Fences.Add(fillFence(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Fences.ToArray();
                        Fences.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetFenceListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetFenceListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Sidewalk closest to lat/lon pair for maxRows")]
        public SideWalkData[] GetSidewalkListNear(Request request, double lat, double lon, int startRow, int maxRows)
        {
            SideWalkData[] Result = null;

            if (LowSecurityCheck(request, "GetSidewalkListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            sqlStr = string.Format("SELECT TOP {0} * FROM sidewalk_report ORDER BY report_time DESC", maxRows + startRow);
                        }
                        else
                        {
                            sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM sidewalk_report WHERE latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                        }

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }

                        // now get the data we want
                        List<SideWalkData> Sidewalks = new List<SideWalkData>();
                        while (rc)
                        {
                            SideWalkData data = new SideWalkData();
                            data.reportId = stmt.fetch("report_id");
                            data.handCode = stmt.ifetch("hand_code");
                            data.address = stmt.fetch("address");
                            data.UnitNum = stmt.fetch("unit_num");
                            data.userName = stmt.fetch("user_name");
                            data.Colour = stmt.ifetch("colour");
                            data.reportTime = stmt.dtfetch("report_time");
                            data.bays = stmt.ifetch("bays");
                            data.blocks = stmt.ifetch("blocks");
                            data.def1 = stmt.ifetch("def1");
                            data.def2 = stmt.ifetch("def2");
                            data.notes = stmt.fetch("notes");
                            data.lon = stmt.dfetch("longitude");
                            data.lat = stmt.dfetch("latitude");
                            data.photos = new List<PhotoData>();
                            bool rc2;
                            if (data.reportId != string.Empty)
                            {
                                rc2 = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", data.reportId);
                                while (rc2)
                                {
                                    PhotoData pdata = new PhotoData();

                                    pdata.repkey = data.reportId;
                                    pdata.imgUrl = stmt2.fetch("img_url");

                                    data.photos.Add(pdata);

                                    rc2 = stmt2.dbSkip();
                                }
                            }

                            Sidewalks.Add(data);

                            rc = stmt.dbSkip();
                        }
                        Result = Sidewalks.ToArray();
                        Sidewalks.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetSidewalkListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetSidewalkListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                }
            }
            return Result;
        }

        // first string is name in class, second is name in database
        string[,] signLayout = { { "type", "type" },
                                 { "structure", "structure" },
                                 { "location", "location" },
                                 { "road", "road" },
                                 { "spot", "spot" },
                                 { "site", "site" },
                                 { "struct_type","struct_type" },
                                 { "panels", "panels"},
                                 { "foot_type_l", "foot_type_l"},
                                 { "foot_type_r", "foot_type_r"},
                                 { "size", "size" },
                                 { "sign_text", "sign_text" },
                                 { "latitude", "latitude" },
                                 { "longitude", "longitude" } };
        //{ "notes", "notes" } };

        [WebMethod(Description = "Add a Sign")]
        public int AddSign(Request request, Sign sdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddSign"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (sdata != null && sdata.structure != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a bridge, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_sign WHERE structure='{0}'", sdata.structure))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(sdata, signLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_sign", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddSign() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddSign() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        public Contract[] GetSignContracts(Request request)
        {
            Contract[] Result = null;

            if (LowSecurityCheck(request, "GetSignContracts"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Contract> contracts = new List<Contract>();

                        bool rc = stmt.DoSQL("select * from yard where yard_no in (select distinct isnull(contract,0) from asset_sign)");
                        while (rc)
                        {
                            Contract v = new Contract();
                            v.Id = stmt.ifetch("yard_no");
                            v.Name = stmt.fetch("name");

                            contracts.Add(v);

                            rc = stmt.dbSkip();
                        }

                        Result = contracts.ToArray();
                        contracts.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetSignContracts() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetSignContracts() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Sign FillSign(SqlStmt stmt)
        {
            Sign s = new Sign();
            s.type = stmt.ifetch("type");
            s.structure = stmt.fetch("structure");
            s.location = stmt.fetch("location");
            s.road = stmt.fetch("road");
            s.spot = stmt.fetch("spot");
            s.site = stmt.fetch("site");
            s.struct_type = stmt.fetch("struct_type");
            s.panels = stmt.fetch("panels");
            s.foot_type_l = stmt.fetch("foot_type_l");
            s.foot_type_r = stmt.fetch("foot_type_r");
            s.columns = stmt.ifetch("columns");
            s.arms = stmt.ifetch("arms");
            s.latitude = stmt.dfetch("latitude");
            s.longitude = stmt.dfetch("longitude");
            s.size = stmt.fetch("size");
            s.sign_text = stmt.fetch("sign_text");
            s.date = stmt.dtfetch("date");
            s.distance = stmt.dfetch("dist");
            return s;
        }

        [WebMethod(Description = "Retrieve Signs using a LIKE clause for maxRows")]
        public Sign[] GetSignList(Request request, string searchStr, int startRow, int maxRows)
        {
            Sign[] Result = null;

            if (LowSecurityCheck(request, "GetSignList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {


                        List<Sign> Signs = new List<Sign>();
                        bool rc;

                        rc = stmt.DoSQLFormat("SELECT TOP {0} * FROM asset_sign WHERE structure LIKE '{1}' ORDER BY structure", maxRows + startRow, searchStr);
                        // scroll past the rows we already have
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }
                        while (rc)
                        {
                            Signs.Add(FillSign(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Signs.ToArray();
                        Signs.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetSignList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetSignList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Signs closest to lat/lon pair for maxRows")]
        public Sign[] GetSignListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows, int contract)
        {
            Sign[] Result = null;

            if (LowSecurityCheck(request, "GetSignList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sqlStr = getSQLNear("sign", "structure", searchStr, lat, lon, maxRows + startRow, string.Empty, contract);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_sign ORDER BY structure", maxRows + startRow);
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_sign WHERE structure LIKE '{1}' ORDER BY structure", maxRows + startRow, SqlHelper.FixUp(searchStr));
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_sign WHERE latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_sign WHERE latitude is not null and longitude is not null and structure LIKE '{3}' ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr));
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }
                        // now get the data we want
                        List<Sign> Signs = new List<Sign>();
                        while (rc)
                        {
                            Signs.Add(FillSign(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Signs.ToArray();
                        Signs.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetSignListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetSignListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        // first string is name in class, second is name in database
        string[,] culvertLayout = { { "type", "type" },
                                   { "structure", "structure" },
                                   { "region", "region" },
                                   { "diameter", "diameter" },
                                   { "road", "road" },
                                   { "location", "location" },
                                   { "patrol","patrol" },
                                   { "latitude", "latitude" },
                                   { "longitude", "longitude" } };

        [WebMethod(Description = "Add a Culvert")]
        public int AddCulvert(Request request, Culvert cdata)
        {
            int Result = -5;

            if (LowSecurityCheck(request, "AddCulvert"))
            {
                Result = -4;
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    Result = -3;
                    if (GetStmt(connStr, out stmt))
                    {
                        if (cdata != null && cdata.type > 0 && cdata.structure != string.Empty)
                        {
                            Result = -2;
                            // currently, we are only adding a culvert, no UPDATES
                            if (!stmt.DoSQLFormat("SELECT id FROM asset_culvert WHERE structure='{0}'", cdata.structure))
                            {
                                Result = -1;

                                SqlHelper sql = procData.fillQuery(cdata, culvertLayout);
                                sql.AddNow("date");

                                if (stmt.DoSQL(sql.BuildCmd("asset_culvert", true)))
                                    Result = 1;
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("AddCulvert() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("AddCulvert() Failed to receive HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Culvert FillCulvert(SqlStmt stmt)
        {
            Culvert c = new Culvert();
            c.structure = stmt.fetch("structure");
            c.patrol = stmt.fetch("patrol");
            c.region = stmt.fetch("region");
            c.road = stmt.fetch("road");
            c.location = stmt.fetch("location");
            c.type = stmt.ifetch("type");
            c.diameter = stmt.dfetch("diameter");
            c.latitude = stmt.dfetch("latitude");
            c.longitude = stmt.dfetch("longitude");
            //c.notes = stmt.fetch("notes"); no such field?
            c.date = stmt.dtfetch("date");
            return c;
        }

        static int CULVERT_STEEL = 3;
        static int CULVERT_CONCRETE = 4;
        static int CULVERT_STEEL_CONCRETE = 8;
        static List<int> AMC_CULVERT_INSP = new List<int> { CULVERT_CONCRETE, CULVERT_STEEL, CULVERT_STEEL_CONCRETE };

        [WebMethod(Description = "Retrieve Culverts closest to lat/lon pair for maxRows")]
        public Culvert[] GetCulvertListNear(Request request, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            Culvert[] Result = null;

            if (LowSecurityCheck(request, "GetCulvertList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string tWhere = string.Format("type in ({0},{1},{2})", CULVERT_CONCRETE, CULVERT_STEEL, CULVERT_STEEL_CONCRETE);
                        string sqlStr = getSQLNear("culvert", "structure", searchStr, lat, lon, maxRows + startRow, tWhere);
                        /*
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_culvert WHERE {1} ORDER BY structure", maxRows + startRow, tWhere);
                            else
                                sqlStr = string.Format("SELECT TOP {0} * FROM asset_culvert WHERE ({1}) and structure LIKE '{2}' ORDER BY structure", maxRows + startRow, tWhere, SqlHelper.FixUp(searchStr));
                        }
                        else
                        {
                            if (searchStr == string.Empty)
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_culvert WHERE {3} and latitude is not null and longitude is not null ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, tWhere);
                            else
                                sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM asset_culvert WHERE ({4}) and latitude is not null and longitude is not null and structure LIKE '{3}' ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon, SqlHelper.FixUp(searchStr), tWhere);
                        }
                        */

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }
                        // now get the data we want
                        List<Culvert> Culverts = new List<Culvert>();
                        while (rc)
                        {
                            Culverts.Add(FillCulvert(stmt));

                            rc = stmt.dbSkip();
                        }
                        Result = Culverts.ToArray();
                        Culverts.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetCulvertListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetCulvertListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve Culverts assets")]
        public Culvert[] GetCulvertList(Request request)
        {
            Culvert[] Result = null;

            if (LowSecurityCheck(request, "GetCulvertList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        List<Culvert> Culverts = new List<Culvert>();
                        bool rc;

                        rc = stmt.DoSQL("SELECT * FROM asset_culvert ORDER BY structure");
                        while (rc)
                        {
                            Culvert b = new Culvert();
                            b.structure = stmt.fetch("structure");
                            b.patrol = stmt.fetch("patrol");
                            b.region = stmt.fetch("region");
                            b.road = stmt.fetch("road");
                            b.location = stmt.fetch("location");
                            b.type = stmt.ifetch("type");
                            b.diameter = stmt.dfetch("diameter");
                            b.latitude = stmt.dfetch("latitude");
                            b.longitude = stmt.dfetch("longitude");
                            //b.notes = stmt.fetch("notes");
                            b.date = stmt.dtfetch("date");

                            Culverts.Add(b);

                            rc = stmt.dbSkip();
                        }
                        Result = Culverts.ToArray();
                        Culverts.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetCulvertList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetCulvertList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        private Bitmap ChangeToMonochrome(Image image)
        {
            Bitmap newBitmap = null;
            using (Bitmap destImage = new Bitmap(image.Width, image.Height))
            {
                var destRect = new Rectangle(0, 0, image.Width, image.Height);
                using (var graphics = Graphics.FromImage(destImage))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
                }

                BitmapData bmpData = destImage.LockBits(new Rectangle(0, 0, destImage.Width, destImage.Height), ImageLockMode.ReadOnly, PixelFormat.Format1bppIndexed);
                newBitmap = new Bitmap(destImage.Width, destImage.Height, bmpData.Stride, PixelFormat.Format1bppIndexed, bmpData.Scan0);
            }
            return newBitmap;
        }

        private byte[] ImageToBytes(Image img)
        {
            ImageConverter converter = new ImageConverter();
            return (byte[])converter.ConvertTo(img, typeof(byte[]));
        }

        private Image LoadBase64(byte[] bytes)
        {
            Image image = null;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    image = Image.FromStream(ms);
                }
            }
            catch (Exception ex)
            {
                Global.messLog.Flush("LoadBase64() Excp '{0}'", ex.Message);
            }
            return image;
        }

        private Bitmap base64SigToBmp(string sigString)
        {
            Bitmap rc = null;

            int p = sigString.IndexOf(',');     // remove the 'data:image/png;base64,' at the start of base64 data
            if (p > 0)
                sigString = sigString.Substring(p + 1);
            byte[] sigData = Convert.FromBase64String(sigString);
            Image img = LoadBase64(sigData);
            if (img != null)
            {
                try
                {
                    rc = ChangeToMonochrome(img);
                }
                catch (Exception ex)
                {
                    Global.messLog.Flush("base64SigToBmp() Excp: {0}", ex.Message);
                }
                finally
                {
                    img.Dispose();
                    img = null;
                }
            }

            return rc;
        }

        private void logUser(SqlStmt stmt, Request req, string user)
        {
            logUser(stmt, req, user, false);

        }

        private void logUser(SqlStmt stmt, Request req, string user, bool defCol)
        {
            try
            {
                SqlHelper query = new SqlHelper();
                query.Add("hand_code", req.Handcode);
                query.Add("phone_version", req.Version);
                query.Add("user_name", user);
                query.Add("def_col", defCol);
                string wh = string.Format("where hand_code={0}", req.Handcode);
                bool ins = !stmt.DoSQL("select id from phone_users " + wh);
                stmt.DoSQL(query.BuildCmd("phone_users", ins, wh));
            }
            catch { }
        }

        [WebMethod(Description = "Push SendBridgeData Report")]
        public int SendBridgeData(Request req, BridgeInspectionData bdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendBridgeData"))
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

                    sql.Add("bridge", bdata.bridge);
                    sql.Add("structure", bdata.structure);
                    sql.Add("location", bdata.location);
                    sql.Add("name", bdata.name);
                    sql.Add("site", bdata.site);
                    sql.Add("type", bdata.type);
                    sql.Add("spans", bdata.spans);
                    sql.Add("width", bdata.width);
                    sql.Add("length", bdata.length);
                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("other", bdata.other);
                    sql.Add("reco", bdata.reco);
                    sql.Add("date", bdata.date);
                    sql.Add("type_code", bdata.type_code);
                    sql.Add("road", bdata.road);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);

                    if (!string.IsNullOrEmpty(bdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(bdata.sigBase64)));
                    else
                        sql.Add("sig_data", bdata.sigData);

                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_bridge"));

                    foreach (var item in bdata.bridgeItems)
                    {
                        sql.Clear();

                        // MUST have an issue # 
                        if (item.issue > 0)
                        {
                            sql.Add("bridge", bdata.bridge);
                            sql.Add("structure", bdata.structure);
                            sql.Add("date", bdata.date);

                            sql.Add("issue", item.issue);
                            sql.Add("defect", item.defect);
                            sql.Add("item", item.item);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.Add("severity", item.severity);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_bridge_item"));
                        }
                    }
                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendBridgeData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendBridgeData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendHighMastData Report")]
        public int SendHighMastData(Request req, HighMastInspectionData data)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendHighMastData"))
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


                    sql.Add("structure", data.structure);
                    sql.Add("date", data.date);
                    sql.Add("user_name", data.userName);
                    sql.Add("phone", data.phone);
                    sql.Add("relamped", data.relamped);
                    sql.Add("insp", data.insp);
                    sql.Add("repair", data.repair);
                    sql.Add("risk", data.risk);
                    sql.Add("scope", data.scope);
                    sql.Add("notes", data.notes);
                    sql.Add("reco", data.reco);
                    sql.Add("file_num", data.fileNum);
                    sql.Add("repKey", data.repKey);

                    if (!string.IsNullOrEmpty(data.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(data.sigBase64)));
                    else
                        sql.Add("sig_data", data.sigData);

                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection__high_mast_lite"));

                    foreach (var item in data.highmastItems)
                    {
                        sql.Clear();

                        // MUST have an issue # 
                        if (item.issue > 0)
                        {
                            sql.Add("structure", data.structure);
                            sql.Add("date", data.date);

                            sql.Add("issue", item.issue);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection__high_mast_lite__item"));
                        }
                    }
                    Result = 1;
                    logUser(stmt, req, data.userName);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendHighMastData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendHighMastData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }


        [WebMethod(Description = "Push Wall Inspection Report")]
        public int SendWallData(Request req, WallInspectionData wdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendWallData"))
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

                    sql.Add("structure", wdata.site);
                    sql.Add("latitude", wdata.latitude);
                    sql.Add("longitude", wdata.longitude);
                    sql.Add("notes", wdata.other);
                    sql.Add("reco", wdata.reco);
                    sql.Add("date", wdata.date);
                    sql.Add("inspector", wdata.inspector);
                    sql.Add("repKey", wdata.repKey);

                    if (!string.IsNullOrEmpty(wdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(wdata.sigBase64)));
                    else
                        sql.Add("sig_data", wdata.sigData);

                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_wall"));

                    foreach (var item in wdata.wallItems)
                    {
                        sql.Clear();

                        // MUST have an issue # 
                        if (item.issue > 0)
                        {
                            sql.Add("structure", wdata.site);
                            sql.Add("date", wdata.date);

                            sql.Add("issue", item.issue);
                            sql.Add("defect", item.defect);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_wall_item"));
                        }
                    }
                    Result = 1;
                    logUser(stmt, req, wdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendWallData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendWallData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendPondData Report")]
        public int SendPondData(Request req, PondInspectionData bdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendPondData"))
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

                    sql.Add("structure", bdata.structure);
                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("other", bdata.other);
                    sql.Add("reco", bdata.reco);
                    sql.Add("date", bdata.date);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);
                    if (!string.IsNullOrEmpty(bdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(bdata.sigBase64)));
                    else
                        sql.Add("sig_data", bdata.sigData);
                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_pond"));

                    foreach (var item in bdata.pondItems)
                    {
                        sql.Clear();

                        // MUST have an issue # 
                        if (item.issue > 0)
                        {
                            sql.Add("structure", bdata.structure);
                            sql.Add("date", bdata.date);

                            sql.Add("issue", item.issue);
                            sql.Add("defect", item.defect);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_pond_item"));
                        }
                    }
                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendPondData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendPondData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendRockData Report")]
        public int SendRockData(Request req, RockInspectionData bdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendRockData"))
                return -2;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("area", bdata.area);
                    sql.Add("hwy", bdata.hwy);
                    sql.Add("patrol", bdata.patrol);
                    sql.Add("site", bdata.site);
                    sql.Add("side", bdata.side);
                    sql.Add("lhrs", bdata.lhrs);
                    sql.Add("os", bdata.os);
                    sql.Add("location", bdata.location);
                    sql.Add("weather", bdata.weather);
                    sql.Add("conditions", bdata.conditions);
                    sql.Add("phone", bdata.phone);
                    sql.Add("quantity", bdata.quantity);
                    sql.Add("block_estimate", bdata.block);
                    sql.Add("distribution", bdata.distribution);
                    sql.Add("opp_officer", bdata.officer);
                    sql.Add("badge", bdata.badge);
                    sql.Add("vehicle_damage", bdata.vehicle);
                    sql.Add("closure", bdata.closure);
                    sql.Add("trimming", bdata.trimming);
                    sql.Add("injuries", bdata.injuries);
                    sql.Add("removal", bdata.removal);

                    sql.Add("remarks", bdata.remarks);

                    sql.Add("date", bdata.date);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);
                    sql.Add("sig_data", bdata.sigData);
                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_rockfall"));

                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendRockData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendRockData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendBeaverData Report")]
        public int SendBeaverData(Request req, BeaverInspectionData bdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendBeaverData"))
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

                    sql.Add("structure", bdata.structure);
                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("other", bdata.other);
                    sql.Add("reco", bdata.reco);
                    sql.Add("date", bdata.date);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);
                    if (!string.IsNullOrEmpty(bdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(bdata.sigBase64)));
                    else
                        sql.Add("sig_data", bdata.sigData);
                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_beaver"));

                    foreach (var item in bdata.beaverItems)
                    {
                        sql.Clear();

                        // MUST have an issue # 
                        if (item.issue > 0)
                        {
                            sql.Add("structure", bdata.structure);
                            sql.Add("date", bdata.date);

                            sql.Add("issue", item.issue);
                            sql.Add("defect", item.defect);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_beaver_item"));
                        }
                    }
                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendBeaverData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendBeaverData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendFenceData Report")]
        public int SendFenceData(Request req, FenceInspectionData bdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendFenceData"))
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

                    sql.Add("structure", bdata.structure);
                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("other", bdata.other);
                    sql.Add("reco", bdata.reco);
                    sql.Add("date", bdata.date);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);
                    if (!string.IsNullOrEmpty(bdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(bdata.sigBase64)));
                    else
                        sql.Add("sig_data", bdata.sigData);
                    sql.AddNow("idate");

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_fence"));

                    foreach (var item in bdata.fenceItems)
                    {
                        sql.Clear();

                        // MUST have an issue # 
                        if (item.issue > 0)
                        {
                            sql.Add("structure", bdata.structure);
                            sql.Add("date", bdata.date);

                            sql.Add("issue", item.issue);
                            sql.Add("defect", item.defect);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_fence_item"));
                        }
                    }
                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendFenceData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendFenceData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendSignData Report")]
        public int SendSignData(Request req, SignInspectionData bdata)
        {

            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendSignData"))
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

                    sql.Add("structure", bdata.structure);
                    sql.Add("notes", bdata.notes);
                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("date", bdata.date);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);
                    if (!string.IsNullOrEmpty(bdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(bdata.sigBase64)));
                    else
                        sql.Add("sig_data", bdata.sigData);
                    sql.Add("uni_code", 99);
                    sql.AddNow("idate");


                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_sign"));

                    foreach (var item in bdata.signItems)
                    {
                        sql.Clear();
                        if (item.issue > 0)
                        {
                            sql.Add("structure", bdata.structure);
                            sql.Add("date", bdata.date);

                            sql.Add("issue", item.issue);
                            sql.Add("defect", item.defect);
                            sql.Add("note", item.note);
                            sql.Add("tstate", item.tstate);
                            sql.Add("group_enabled", item.groupEnabled);
                            sql.Add("severity", item.severity);
                            sql.Add("uni_code", 99);
                            sql.AddNow("idate");

                            stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_sign_item"));
                        }

                    }

                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendSignData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendSignData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Push SendCulvertData Report")]
        public int SendCulvertData(Request req, CulvertInspectionData bdata)
        {
            int Result = 0;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req, "SendCulvertData"))
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

                    sql.Add("structure", bdata.structure);
                    sql.Add("location", bdata.location);
                    sql.Add("name", bdata.name);
                    sql.Add("region", bdata.region);
                    sql.Add("type", bdata.type);
                    sql.Add("diameter", bdata.diameter);
                    sql.Add("latitude", bdata.latitude);
                    sql.Add("longitude", bdata.longitude);
                    sql.Add("note", bdata.other);
                    sql.Add("reco", bdata.reco);
                    sql.Add("patrol", bdata.patrol);
                    sql.Add("date", bdata.date);
                    sql.Add("road", bdata.road);
                    sql.Add("inspector", bdata.inspector);
                    sql.Add("repKey", bdata.repKey);
                    if (!string.IsNullOrEmpty(bdata.sigBase64))
                        sql.Add("sig_data", myConvert.ToByteArray(base64SigToBmp(bdata.sigBase64)));
                    else
                        sql.Add("sig_data", bdata.sigData);
                    sql.Add("idate", DateTime.UtcNow);

                    stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_culvert"));

                    foreach (var item in bdata.culvertItems)
                    {
                        sql.Clear();

                        sql.Add("structure", bdata.structure);
                        sql.Add("date", bdata.date);
                        sql.Add("issue", item.issue);
                        sql.Add("defect", item.defect);
                        sql.Add("note", item.note);
                        sql.Add("location", item.location);
                        sql.Add("tstate", item.tstate);
                        sql.Add("group_enabled", item.groupEnabled);
                        sql.Add("severity", item.severity);
                        sql.Add("idate", DateTime.UtcNow);
                        sql.Add("item", item.item);

                        stmt.DoSQL(sql.BuildInsert("INSERT INTO inspection_culvert_item"));
                    }

                    Result = 1;
                    logUser(stmt, req, bdata.inspector);
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendCulvertData() HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, x.Message);
                InsertErrorRecord(req.CustCode, "SendCulvertData() " + x.Message, req);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve FormLayout for inspection app, new style")]
        public customForms.aForm GetCustomForm(Request request)
        {
            customForms.aForm Result = new customForms.aForm();
            Result.errorMsg = "invalid credentials";
            if (LowSecurityCheck(request, "GetCustomForm"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                        Result = customForms.GetSQLLayoutFile(stmt, stmt2);
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetCustomFormLayout() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    Result.errorMsg = x.Message;
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve custom form data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public customForms.aForm GetCustomFormJson(Request req)
        {
            return GetCustomForm(req);
        }


        [WebMethod(Description = "Retrieve FormLayout for inspection app")]
        public FormHelper GetFormLayout(Request request)
        {
            FormHelper Result = new FormHelper();

            if (LowSecurityCheck(request, "GetFormLayout"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string errMess;
                        Result.Export(stmt, out errMess);
                        if (errMess != string.Empty)
                            Global.messLog.Flush("GetFormLayout() Export error: {0}", errMess);
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetFormLayout() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
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

        [WebMethod(Description = "Retrieve Bridges inspections")]
        public HighMastInspectionData[] GetHighMastInsectionList(Request request, HighMast[] highmasts, int maxInsp)
        {
            HighMastInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetHighMastInsectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<HighMastInspectionData> Highmasts = new List<HighMastInspectionData>();

                        foreach (var item in highmasts)
                        {
                            if (item.structure != null && item.structure.Length > 0)
                            {

                                bool brc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection__high_mast_lite WHERE structure='{1}' ORDER BY date desc", maxInsp, item.structure);
                                while (brc)
                                {
                                    HighMastInspectionData hm = new HighMastInspectionData();

                                    hm.structure = stmt.fetch("structure");
                                    hm.date = stmt.dtfetch("date");
                                    hm.userName = stmt.fetch("user_name");
                                    hm.phone = stmt.fetch("phone");
                                    hm.relamped = stmt.ifetch("relamped");
                                    hm.insp = stmt.ifetch("insp");
                                    hm.repair = stmt.ifetch("repair");
                                    hm.risk = stmt.ifetch("risk");
                                    hm.scope = stmt.ifetch("scope");
                                    hm.notes = stmt.fetch("notes");
                                    hm.reco = stmt.fetch("reco");
                                    hm.fileNum = stmt.fetch("fileNum");
                                    hm.repKey = stmt.fetch("repKey");

                                    bool rc;
                                    if (hm.repKey != string.Empty)
                                    {
                                        rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", hm.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = hm.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            hm.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    rc = stmt2.DoSQLFormat("SELECT * FROM inspection__high_mast_lite__item WHERE structure='{0}' and DATEADD(ms, -DATEPART(ms, date), date)={1}", hm.structure, SqlHelper.timeStamp(hm.date));
                                    while (rc)
                                    {
                                        HighMastItem hmi = new HighMastItem();

                                        hmi.issue = stmt2.ifetch("issue");
                                        hmi.note = stmt2.fetch("note");
                                        hmi.tstate = stmt2.ifetch("tstate");
                                        hmi.groupEnabled = stmt2.bfetch("group_enabled");
                                        hmi.structure = stmt2.fetch("structure");
                                        hmi.date = stmt2.dtfetch("date");

                                        hm.highmastItems.Add(hmi);

                                        rc = stmt2.dbSkip();
                                    }

                                    Highmasts.Add(hm);

                                    brc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Highmasts.ToArray();
                        Highmasts.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetHighMastInsectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBridgeInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }


        [WebMethod(Description = "Retrieve Bridges inspections")]
        public BridgeInspectionData[] GetBridgeInsectionList(Request request, Bridge[] bridges, int maxInsp)
        {
            BridgeInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetBridgeInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<BridgeInspectionData> Bridges = new List<BridgeInspectionData>();

                        foreach (var item in bridges)
                        {
                            if (item.bridge != null && item.bridge.Length > 0)
                            {
                                string typeWhere = string.Empty;
                                if (item.type_code > 0)
                                    typeWhere = string.Format(" AND type_code={0}", item.type_code);
                                bool brc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection_bridge WHERE bridge='{1}'{2} ORDER BY date desc", maxInsp, item.bridge, typeWhere);
                                while (brc)
                                {
                                    BridgeInspectionData b = new BridgeInspectionData();
                                    b.bridge = stmt.fetch("bridge");
                                    b.structure = stmt.fetch("structure");
                                    b.location = stmt.fetch("location");
                                    b.name = stmt.fetch("name");
                                    b.site = stmt.fetch("site");
                                    b.type = stmt.fetch("type");
                                    b.spans = stmt.ifetch("spans");
                                    b.type_code = stmt.ifetch("type_code");
                                    b.length = stmt.dfetch("length");
                                    b.width = stmt.dfetch("width");
                                    b.latitude = stmt.dfetch("latitude");
                                    b.longitude = stmt.dfetch("longitude");
                                    b.other = stmt.fetch("other");
                                    b.reco = stmt.fetch("reco");
                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    b.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
                                    b.road = stmt.fetch("road");
                                    b.inspector = stmt.fetch("inspector");
                                    b.repKey = stmt.fetch("repKey");

                                    bool rc;
                                    if (b.repKey != string.Empty)
                                    {
                                        rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", b.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = b.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            b.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    rc = stmt2.DoSQLFormat("SELECT * FROM inspection_bridge_item WHERE bridge='{0}' and DATEADD(ms, -DATEPART(ms, date), date)={1}", b.bridge, SqlHelper.timeStamp(b.date));
                                    while (rc)
                                    {
                                        BridgeItem bi = new BridgeItem();
                                        bi.bridge = b.bridge;
                                        bi.date = b.date;
                                        bi.issue = stmt2.ifetch("issue");
                                        bi.defect = stmt2.bfetch("defect");
                                        bi.item = stmt2.fetch("item");
                                        bi.note = stmt2.fetch("note");
                                        bi.tstate = stmt2.ifetch("tstate");
                                        bi.groupEnabled = stmt2.bfetch("group_enabled");
                                        bi.severity = stmt2.ifetch("severity");

                                        b.bridgeItems.Add(bi);

                                        rc = stmt2.dbSkip();
                                    }

                                    Bridges.Add(b);

                                    brc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Bridges.ToArray();
                        Bridges.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetBridgeInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBridgeInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Wall inspections")]
        public WallInspectionData[] GetWallInspectionList(Request request, Wall[] walls, int maxInsp)
        {
            WallInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetWallInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<WallInspectionData> Walls = new List<WallInspectionData>();

                        foreach (var item in walls)
                        {
                            if (item.site != null && item.site.Length > 0)
                            {
                                bool brc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection_wall LEFT JOIN asset_wall ON asset_wall.structure = inspection_wall.structure WHERE asset_wall.structure='{1}' ORDER BY inspection_wall.date desc", maxInsp, SqlHelper.FixUp(item.site));
                                while (brc)
                                {
                                    WallInspectionData b = new WallInspectionData();
                                    b.location = stmt.fetch("location");
                                    b.name = stmt.fetch("name");
                                    b.site = stmt.fetch("structure");
                                    b.type = stmt.fetch("type");
                                    b.length = stmt.dfetch("length");
                                    b.latitude = stmt.dfetch("latitude");
                                    b.longitude = stmt.dfetch("longitude");
                                    b.other = stmt.fetch("notes");
                                    b.reco = stmt.fetch("reco");
                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    b.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
                                    b.road = stmt.fetch("road");
                                    b.inspector = stmt.fetch("inspector");
                                    b.repKey = stmt.fetch("repKey");

                                    bool rc;
                                    if (b.repKey != string.Empty)
                                    {
                                        rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", b.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = b.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            b.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    rc = stmt2.DoSQLFormat("SELECT * FROM inspection_wall_item WHERE structure='{0}' and DATEADD(ms, -DATEPART(ms, date), date)={1}", SqlHelper.FixUp(b.site), SqlHelper.timeStamp(b.date));
                                    while (rc)
                                    {
                                        WallItem bi = new WallItem();
                                        bi.site = b.site;
                                        bi.date = b.date;
                                        bi.issue = stmt2.ifetch("issue");
                                        bi.defect = stmt2.bfetch("defect");
                                        bi.note = stmt2.fetch("note");
                                        bi.tstate = stmt2.ifetch("tstate");
                                        bi.groupEnabled = stmt2.bfetch("group_enabled");
                                        bi.severity = stmt2.ifetch("severity");

                                        b.wallItems.Add(bi);

                                        rc = stmt2.dbSkip();
                                    }

                                    Walls.Add(b);

                                    brc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Walls.ToArray();
                        Walls.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetWallInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetWallInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Pond inspections")]
        public PondInspectionData[] GetPondInspectionList(Request request, Pond[] ponds, int maxInsp)
        {
            PondInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetPondInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<PondInspectionData> Ponds = new List<PondInspectionData>();

                        foreach (var item in ponds)
                        {
                            if (item.structure != null && item.structure.Length > 0)
                            {
                                bool brc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection_pond WHERE structure='{1}' ORDER BY date desc", maxInsp, SqlHelper.FixUp(item.structure));
                                while (brc)
                                {
                                    PondInspectionData b = new PondInspectionData();
                                    b.structure = stmt.fetch("structure");
                                    b.location = stmt.fetch("location");
                                    b.name = stmt.fetch("name");
                                    b.site = stmt.fetch("site");
                                    b.latitude = stmt.dfetch("latitude");
                                    b.longitude = stmt.dfetch("longitude");
                                    b.other = stmt.fetch("other");
                                    b.reco = stmt.fetch("reco");
                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    b.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
                                    b.road = stmt.fetch("road");
                                    b.inspector = stmt.fetch("inspector");
                                    b.repKey = stmt.fetch("repKey");

                                    bool rc;
                                    if (b.repKey != string.Empty)
                                    {
                                        rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", b.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = b.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            b.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    rc = stmt2.DoSQLFormat("SELECT * FROM inspection_pond_item WHERE structure='{0}' and DATEADD(ms, -DATEPART(ms, date), date)={1}", SqlHelper.FixUp(b.structure), SqlHelper.timeStamp(b.date));
                                    while (rc)
                                    {
                                        PondItem bi = new PondItem();
                                        bi.structure = b.structure;
                                        bi.date = b.date;
                                        bi.issue = stmt2.ifetch("issue");
                                        bi.defect = stmt2.bfetch("defect");
                                        bi.note = stmt2.fetch("note");
                                        bi.tstate = stmt2.ifetch("tstate");
                                        bi.groupEnabled = stmt2.bfetch("group_enabled");

                                        b.pondItems.Add(bi);

                                        rc = stmt2.dbSkip();
                                    }

                                    Ponds.Add(b);

                                    brc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Ponds.ToArray();
                        Ponds.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetPondInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetPondInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Rock inspections near")]
        public RockInspectionData[] GetRockInspectionListNear(Request request, double lat, double lon, int startRow, int maxRows)
        {
            RockInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetRockInspectionListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            sqlStr = string.Format("SELECT TOP {0} * FROM inspection_rockfall ORDER BY date DESC", maxRows + startRow);
                        }
                        else
                        {
                            sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326)) as dist FROM inspection_rockfall WHERE latitude is not null and longitude is not null and latitude > 0 ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(latitude, longitude, 4326))", maxRows + startRow, lat, lon);
                        }

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }
                        // now get the data we want
                        List<RockInspectionData> Rocks = new List<RockInspectionData>();
                        while (rc)
                        {
                            RockInspectionData b = new RockInspectionData();
                            b.location = stmt.fetch("location");
                            b.site = stmt.fetch("site");
                            b.latitude = stmt.dfetch("latitude");
                            b.longitude = stmt.dfetch("longitude");
                            b.area = stmt.fetch("area");
                            b.hwy = stmt.fetch("hwy");
                            b.patrol = stmt.fetch("patrol");
                            b.side = stmt.ifetch("side");
                            b.lhrs = stmt.fetch("lhrs");
                            b.os = stmt.fetch("os");
                            b.location = stmt.fetch("location");
                            b.weather = stmt.fetch("weather");
                            b.conditions = stmt.fetch("conditions");
                            b.phone = stmt.fetch("phone");
                            b.quantity = stmt.ifetch("quantity");
                            b.block = stmt.ifetch("block_estimate");
                            b.distribution = stmt.ifetch("distribution");
                            b.officer = stmt.fetch("officer");
                            b.badge = stmt.fetch("badge");
                            b.vehicle = stmt.fetch("vehicle_damage");
                            b.closure = stmt.fetch("closure");
                            b.trimming = stmt.fetch("trimming");
                            b.remarks = stmt.fetch("remarks");
                            // remove millisecs which some older reports have
                            DateTime dt = stmt.dtfetch("date");
                            b.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
                            b.inspector = stmt.fetch("inspector");
                            b.repKey = stmt.fetch("repKey");
                            b.photos = new List<PhotoData>();

                            bool rc2;
                            if (b.repKey != string.Empty)
                            {
                                rc2 = stmt2.DoSQLFormat("SELECT img_url FROM Img_events WHERE RepKey='{0}'", b.repKey);
                                while (rc2)
                                {
                                    PhotoData pdata = new PhotoData();

                                    pdata.repkey = b.repKey;
                                    pdata.imgUrl = stmt2.fetch("img_url");
                                    pdata.issue = stmt2.ifetch("issue");

                                    b.photos.Add(pdata);

                                    rc2 = stmt2.dbSkip();
                                }
                            }
                            Rocks.Add(b);
                            rc = stmt.dbSkip();
                        }
                        Result = Rocks.ToArray();
                        Rocks.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetRockInspectionListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetRockInspectionListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Wildlife incident near")]
        public WildlifeData[] GetWildlifeListNear(Request request, double lat, double lon, int startRow, int maxRows)
        {
            WildlifeData[] Result = null;

            if (LowSecurityCheck(request, "GetWilelifeListNear"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        string sqlStr;
                        if (lat == 0 && lon == 0)
                        {
                            sqlStr = string.Format("SELECT TOP {0} * FROM wildlife ORDER BY reportTime DESC", maxRows + startRow);
                        }
                        else
                        {
                            sqlStr = string.Format("SELECT TOP {0} *, geography::Point({1}, {2}, 4326).STDistance(geography::Point(lat, lon, 4326)) as dist FROM wildlife WHERE lat is not null and lon is not null and lat > 0 ORDER BY geography::Point({1}, {2}, 4326).STDistance(geography::Point(lat, lon, 4326))", maxRows + startRow, lat, lon);
                        }

                        bool rc = stmt.DoSQL(sqlStr);
                        // scroll to the beginning of the rows we call
                        while (rc && startRow > 0)
                        {
                            startRow--;
                            rc = stmt.dbSkip();
                        }
                        // now get the data we want
                        List<WildlifeData> Wild = new List<WildlifeData>();
                        while (rc)
                        {
                            WildlifeData data = new WildlifeData();
                            data.reportID = stmt.fetch("reportId");
                            data.handcode = stmt.fetch("hand_code");
                            data.address = stmt.fetch("address");
                            data.unitnum = stmt.fetch("unit_num");
                            data.username = stmt.fetch("username");
                            data.direction = stmt.ifetch("direction");
                            data.reportTime = stmt.dtfetch("reportTime");
                            data.town = stmt.fetch("town");
                            data.age = stmt.fetch("age");
                            data.gender = stmt.ifetch("gender");
                            data.species = stmt.fetch("species");
                            data.weather = stmt.ifetch("weather");
                            data.visibility = stmt.ifetch("visibility");
                            data.highway = stmt.ifetch("highway");
                            data.rowt = stmt.ifetch("rowt");
                            data.comments = stmt.fetch("comments");
                            data.log = stmt.fetch("wlog");
                            data.lat = stmt.cfetch("lat");
                            data.lon = stmt.cfetch("lon");
                            data.photos = new List<PhotoData>();

                            bool rc2;
                            if (data.reportID != string.Empty)
                            {
                                rc2 = stmt2.DoSQLFormat("SELECT img_url FROM Img_events WHERE RepKey='{0}'", data.reportID);
                                while (rc2)
                                {
                                    PhotoData pdata = new PhotoData();

                                    pdata.repkey = data.reportID;
                                    pdata.imgUrl = stmt2.fetch("img_url");
                                    pdata.issue = stmt2.ifetch("issue");

                                    data.photos.Add(pdata);

                                    rc2 = stmt2.dbSkip();
                                }
                            }
                            Wild.Add(data);
                            rc = stmt.dbSkip();
                        }
                        Result = Wild.ToArray();
                        Wild.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetWildlifeListNear() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetWildlifeListNear() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Beaver inspections")]
        public BeaverInspectionData[] GetBeaverInspectionList(Request request, Beaver[] beavers, int maxInsp)
        {
            BeaverInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetBeaverInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<BeaverInspectionData> Beavers = new List<BeaverInspectionData>();

                        foreach (var item in beavers)
                        {
                            if (item.structure != null && item.structure.Length > 0)
                            {
                                bool brc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection_beaver WHERE structure='{1}' ORDER BY date desc", maxInsp, SqlHelper.FixUp(item.structure));
                                while (brc)
                                {
                                    BeaverInspectionData b = new BeaverInspectionData();
                                    b.structure = stmt.fetch("structure");
                                    b.location = stmt.fetch("location");
                                    b.name = stmt.fetch("name");
                                    b.site = stmt.fetch("site");
                                    b.latitude = stmt.dfetch("latitude");
                                    b.longitude = stmt.dfetch("longitude");
                                    b.other = stmt.fetch("other");
                                    b.reco = stmt.fetch("reco");
                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    b.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
                                    b.road = stmt.fetch("road");
                                    b.inspector = stmt.fetch("inspector");
                                    b.repKey = stmt.fetch("repKey");

                                    bool rc;
                                    if (b.repKey != string.Empty)
                                    {
                                        rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", b.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = b.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            b.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    rc = stmt2.DoSQLFormat("SELECT * FROM inspection_beaver_item WHERE structure='{0}' and DATEADD(ms, -DATEPART(ms, date), date)={1}", SqlHelper.FixUp(b.structure), SqlHelper.timeStamp(b.date));
                                    while (rc)
                                    {
                                        BeaverItem bi = new BeaverItem();
                                        bi.structure = b.structure;
                                        bi.date = b.date;

                                        bi.issue = stmt2.ifetch("issue");
                                        bi.defect = stmt2.bfetch("defect");
                                        bi.note = stmt2.fetch("note");
                                        bi.tstate = stmt2.ifetch("tstate");
                                        bi.groupEnabled = stmt2.bfetch("group_enabled");

                                        b.beaverItems.Add(bi);

                                        rc = stmt2.dbSkip();
                                    }

                                    Beavers.Add(b);

                                    brc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Beavers.ToArray();
                        Beavers.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetBeaverInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetBeaverInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Fence Fence inspections")]
        public FenceInspectionData[] GetFenceInspectionList(Request request, Fence[] fences, int maxInsp)
        {
            FenceInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetFenceInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<FenceInspectionData> Fences = new List<FenceInspectionData>();

                        foreach (var item in fences)
                        {
                            if (item.structure != null && item.structure.Length > 0)
                            {
                                bool brc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection_fence WHERE structure='{1}' ORDER BY date desc", maxInsp, SqlHelper.FixUp(item.structure));
                                while (brc)
                                {
                                    FenceInspectionData b = new FenceInspectionData();
                                    b.structure = stmt.fetch("structure");
                                    b.location = stmt.fetch("location");
                                    b.name = stmt.fetch("name");
                                    b.site = stmt.fetch("site");
                                    b.latitude = stmt.dfetch("latitude");
                                    b.longitude = stmt.dfetch("longitude");
                                    b.other = stmt.fetch("other");
                                    b.reco = stmt.fetch("reco");
                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    b.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
                                    b.road = stmt.fetch("road");
                                    b.inspector = stmt.fetch("inspector");
                                    b.repKey = stmt.fetch("repKey");

                                    bool rc;
                                    if (b.repKey != string.Empty)
                                    {
                                        rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey='{0}'", b.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = b.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            b.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    rc = stmt2.DoSQLFormat("SELECT * FROM inspection_fence_item WHERE structure='{0}' and DATEADD(ms, -DATEPART(ms, date), date)={1}", SqlHelper.FixUp(b.structure), SqlHelper.timeStamp(b.date));
                                    while (rc)
                                    {
                                        FenceItem bi = new FenceItem();
                                        bi.structure = b.structure;
                                        bi.date = b.date;
                                        bi.issue = stmt2.ifetch("issue");
                                        bi.defect = stmt2.bfetch("defect");
                                        bi.note = stmt2.fetch("note");
                                        bi.tstate = stmt2.ifetch("tstate");
                                        bi.groupEnabled = stmt2.bfetch("group_enabled");

                                        b.fenceItems.Add(bi);

                                        rc = stmt2.dbSkip();
                                    }

                                    Fences.Add(b);

                                    brc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Fences.ToArray();
                        Fences.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetFenceInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetFenceInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }
            }
            return Result;
        }


        [WebMethod(Description = "Retrieve Sign inspections")]
        public SignInspectionData[] GetSignInspectionList(Request request, Sign[] signs, int maxInsp)
        {
            SignInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetSignInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                SqlStmt stmt3 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2) && GetStmt(connStr, out stmt3))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<SignInspectionData> Signs = new List<SignInspectionData>();

                        foreach (var item in signs)
                        {
                            if (item.structure != null && item.structure.Length > 0)
                            {
                                bool rc3 = stmt.DoSQLFormat("SELECT TOP {1} * FROM inspection_sign WHERE structure = '{0}' ORDER BY date desc", item.structure, maxInsp);
                                while (rc3)
                                {
                                    SignInspectionData s = new SignInspectionData();

                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    s.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));

                                    s.structure = stmt.fetch("structure");
                                    s.inspector = stmt.fetch("inspector");
                                    s.notes = stmt.fetch("notes");
                                    s.latitude = stmt.dfetch("latitude");
                                    s.longitude = stmt.dfetch("longitude");
                                    s.repKey = stmt.fetch("repKey");


                                    if (s.repKey.Length > 0)
                                    {
                                        bool rc = stmt3.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey = '{0}'", s.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = s.repKey;
                                            pdata.imgUrl = stmt3.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            s.photos.Add(pdata);

                                            rc = stmt3.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    bool rc2 = stmt2.DoSQLFormat("SELECT * FROM inspection_sign_item WHERE DATEADD(ms, -DATEPART(ms, date), date)={0} AND structure = '{1}'", SqlHelper.timeStamp(s.date), s.structure);
                                    while (rc2)
                                    {
                                        SignItem si = new SignItem();
                                        si.structure = s.structure;
                                        si.date = s.date;
                                        si.issue = stmt2.ifetch("issue");
                                        si.defect = stmt2.bfetch("defect");
                                        si.note = stmt2.fetch("note");
                                        si.tstate = stmt2.ifetch("tstate");
                                        si.groupEnabled = stmt2.bfetch("group_enabled");
                                        si.severity = stmt2.ifetch("severity");

                                        s.signItems.Add(si);

                                        rc2 = stmt2.dbSkip();

                                    }

                                    Signs.Add(s);

                                    rc3 = stmt.dbSkip();
                                }

                            }


                        }
                        Result = Signs.ToArray();
                        Signs.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetSignInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetSignInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                    if (stmt3 != null)
                    {
                        stmt3.Close();
                        stmt3 = null;
                    }

                }
            }
            return Result;
        }

        [WebMethod(Description = "Retrieve Culverts inspections")]
        public CulvertInspectionData[] GetCulvertInsectionList(Request request, Culvert[] culvertItems, int maxInsp)
        {
            CulvertInspectionData[] Result = null;

            if (LowSecurityCheck(request, "GetCulvertInspectionList"))
            {
                string connStr = CustConnectionString(request);
                SqlStmt stmt = null;
                SqlStmt stmt2 = null;
                try
                {
                    if (GetStmt(connStr, out stmt) && GetStmt(connStr, out stmt2))
                    {
                        maxInsp = Math.Max(1, maxInsp);
                        List<CulvertInspectionData> Culverts = new List<CulvertInspectionData>();

                        foreach (var item in culvertItems)
                        {
                            if (item.structure != null && item.structure.Length > 0)
                            {
                                string typeWhere = string.Empty;
                                if (item.type > 0)
                                    if (AMC_CULVERT_INSP.Contains(item.type))
                                        typeWhere = string.Format(" AND type in ({0},{1},{2})", CULVERT_CONCRETE, CULVERT_STEEL, CULVERT_STEEL_CONCRETE);
                                    else
                                        typeWhere = string.Format(" AND type={0}", item.type);

                                bool crc = stmt.DoSQLFormat("SELECT TOP {0} * FROM inspection_culvert WHERE structure='{1}'{2} ORDER BY date desc", maxInsp, item.structure, typeWhere);
                                while (crc)
                                {
                                    CulvertInspectionData c = new CulvertInspectionData();

                                    c.structure = stmt.fetch("structure");
                                    c.location = stmt.fetch("location");
                                    c.name = stmt.fetch("name");
                                    c.road = stmt.fetch("road");
                                    c.type = stmt.ifetch("type");
                                    c.diameter = stmt.dfetch("diameter");
                                    c.latitude = stmt.dfetch("latitude");
                                    c.longitude = stmt.dfetch("longitude");
                                    c.other = stmt.fetch("note");
                                    c.reco = stmt.fetch("reco");
                                    c.region = stmt.fetch("region");
                                    c.patrol = stmt.fetch("patrol");
                                    // remove millisecs which some older reports have
                                    DateTime dt = stmt.dtfetch("date");
                                    c.date = dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));

                                    c.road = stmt.fetch("road");
                                    c.inspector = stmt.fetch("inspector");
                                    c.repKey = stmt.fetch("repKey");

                                    if (c.repKey.Length > 0)
                                    {
                                        bool rc = stmt2.DoSQLFormat("SELECT * FROM Img_events WHERE RepKey = '{0}'", c.repKey);
                                        while (rc)
                                        {
                                            PhotoData pdata = new PhotoData();

                                            pdata.repkey = c.repKey;
                                            pdata.imgUrl = stmt2.fetch("img_url");
                                            pdata.issue = stmt2.ifetch("issue");

                                            c.photos.Add(pdata);

                                            rc = stmt2.dbSkip();
                                        }
                                    }

                                    // the DATEADD(ms, -DATEPART(ms, date), date) is because some older inspections had millisecs in the date, this makes sure to ignore that
                                    bool rc2 = stmt2.DoSQLFormat("SELECT * FROM inspection_culvert_item WHERE DATEADD(ms, -DATEPART(ms, date), date)={0} AND structure='{1}'", SqlHelper.timeStamp(c.date), c.structure);
                                    while (rc2)
                                    {
                                        CulvertItem ci = new CulvertItem();
                                        ci.structure = c.structure;
                                        ci.date = c.date;
                                        ci.issue = stmt2.ifetch("issue");
                                        ci.defect = stmt2.bfetch("defect");
                                        ci.location = stmt2.fetch("location");
                                        ci.note = stmt2.fetch("note");
                                        ci.item = stmt2.fetch("item");
                                        ci.tstate = stmt2.ifetch("tstate");
                                        ci.groupEnabled = stmt2.bfetch("group_enabled");
                                        ci.severity = stmt2.ifetch("severity");

                                        c.culvertItems.Add(ci);

                                        rc2 = stmt2.dbSkip();
                                    }

                                    Culverts.Add(c);

                                    crc = stmt.dbSkip();
                                }
                            }
                        }
                        Result = Culverts.ToArray();
                        Culverts.Clear();
                    }
                }
                catch (Exception x)
                {
                    Global.messLog.Flush("GetCulvertInspectionList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetCulvertInspectionList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
                }
                finally
                {
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
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
        public GoUser[] GetGoUsers(Request request, out int userVersion)
        {
            GoUser[] Result = null;
            userVersion = 0;

            if (LowSecurityCheck(request, "GetGoUsers"))
            {
                string connStr = CustConnectionString(request);
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
                    Global.messLog.Flush("GetGouserList() HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message);
                    InsertErrorRecord(request.CustCode, string.Format("GetGouserList() Failed to send HandCode:{0} Version:{1} Error:{2}", request.Handcode, request.Version, x.Message), request);
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

        [WebMethod(Description = "Retrieve vehicle types list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public VehicleType[] GetVehicleTypesJson(Request req)
        {
            return GetVehicleTypes(req);
        }

        [WebMethod(Description = "Send Bridgedata as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendBridgeDataJson(Request req, BridgeInspectionData bdata)
        {
            return SendBridgeData(req, bdata);
        }

        [WebMethod(Description = "Send Highmast as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendHighMastDataJson(Request req, HighMastInspectionData data)
        {
            return SendHighMastData(req, data);
        }

        [WebMethod(Description = "Send Ponddata as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendPondDataJson(Request req, PondInspectionData bdata)
        {
            return SendPondData(req, bdata);
        }

        [WebMethod(Description = "Send WallInspectionData as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendWallDataJson(Request req, WallInspectionData wdata)
        {
            return SendWallData(req, wdata);
        }

        [WebMethod(Description = "Send Rockdata as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendRockDataJson(Request req, RockInspectionData bdata)
        {
            return SendRockData(req, bdata);
        }

        [WebMethod(Description = "Send Beaverdata as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendBeaverDataJson(Request req, BeaverInspectionData bdata)
        {
            return SendBeaverData(req, bdata);
        }

        [WebMethod(Description = "Send Fence as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendFenceDataJson(Request req, FenceInspectionData bdata)
        {
            return SendFenceData(req, bdata);
        }

        [WebMethod(Description = "Send Signdata as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendSignDataJson(Request req, SignInspectionData sdata)
        {
            return SendSignData(req, sdata);
        }

        [WebMethod(Description = "Send Culvertdata as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendCulvertDataJson(Request req, CulvertInspectionData bdata)
        {
            return SendCulvertData(req, bdata);
        }

        [WebMethod(Description = "Add a Bridge")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddBridgeJson(Request req, Bridge bdata)
        {
            return AddBridge(req, bdata);
        }

        [WebMethod(Description = "Add a Bridge")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddHighMastJson(Request req, HighMast data)
        {
            return AddHighMast(req, data);
        }

        [WebMethod(Description = "Add a Sign")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddSignJson(Request req, Sign sdata)
        {
            return AddSign(req, sdata);
        }

        [WebMethod(Description = "Add a Beaver Pond")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddBeaverJson(Request req, Beaver bdata)
        {
            return AddBeaver(req, bdata);
        }

        [WebMethod(Description = "Add a Culvert")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddCulvertJson(Request req, Culvert cdata)
        {
            return AddCulvert(req, cdata);
        }

        [WebMethod(Description = "Add a Fence")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddFenceJson(Request req, Fence fdata)
        {
            return AddFence(req, fdata);
        }

        [WebMethod(Description = "Add a Pond")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddPondJson(Request req, Pond pdata)
        {
            return AddPond(req, pdata);
        }

        [WebMethod(Description = "Add a Wall")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int AddWallJson(Request req, Wall wdata)
        {
            return AddWall(req, wdata);
        }

        [WebMethod(Description = "Retrieve Bridge list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Bridge[] GetBridgesJson(Request req, int type)
        {
            return GetBridgeList(req, type);
        }

        [WebMethod(Description = "Retrieve Bridge list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Bridge[] GetBridgesNearExJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows, int type)
        {
            return GetBridgeListNear(req, searchStr, lat, lon, startRow, maxRows, type);
        }

        [WebMethod(Description = "Retrieve Bridge list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public HighMast[] GetHighMastNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetHighMastListNear(req, searchStr, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Bridge list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Bridge[] GetBridgesNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetBridgeListNear(req, searchStr, lat, lon, startRow, maxRows, -1);
        }

        [WebMethod(Description = "Retrieve Wall list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Wall[] GetWallsJson(Request req)
        {
            return GetWallList(req);
        }

        [WebMethod(Description = "Retrieve Wall list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Wall[] GetWallsNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetWallListNear(req, searchStr, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Pond list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Pond[] GetPondsJson(Request req)
        {
            return GetPondList(req);
        }

        [WebMethod(Description = "Retrieve Pond list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Pond[] GetPondsNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetPondListNear(req, searchStr, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Sidewalk list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public SideWalkData[] GetSidewalksNearJson(Request req, double lat, double lon, int startRow, int maxRows)
        {
            return GetSidewalkListNear(req, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Rock list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public RockInspectionData[] GetRocksNearJson(Request req, double lat, double lon, int startRow, int maxRows)
        {
            return GetRockInspectionListNear(req, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Wild list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public WildlifeData[] GetWildNearJson(Request req, double lat, double lon, int startRow, int maxRows)
        {
            return GetWildlifeListNear(req, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Pond list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Beaver[] GetBeaversNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetBeaverListNear(req, searchStr, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Fence list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Fence[] GetFencesJson(Request req)
        {
            return GetFenceList(req);
        }

        [WebMethod(Description = "Retrieve Fence list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Fence[] GetFencesNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetFenceListNear(req, searchStr, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Sign list as JSON using a LIKE clause for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Sign[] GetSignsJson(Request req, string searchStr, int startRow, int maxRows)
        {
            return GetSignList(req, searchStr, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Sign list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Sign[] GetSignsNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetSignListNear(req, searchStr, lat, lon, startRow, maxRows, 0);
        }

        [WebMethod(Description = "Retrieve Sign list closest to lat/lon pair for maxRows filtered by contract field")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Sign[] GetSignsNearJsonEx(Request req, string searchStr, double lat, double lon, int startRow, int maxRows, int contract)
        {
            return GetSignListNear(req, searchStr, lat, lon, startRow, maxRows, contract);
        }

        [WebMethod(Description = "Retrieve all values in contract field of asset_sign")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Contract[] GetSignContractsJson(Request req)
        {
            return GetSignContracts(req);
        }

        [WebMethod(Description = "Retrieve Culvert list closest to lat/lon pair for maxRows")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Culvert[] GetCulvertNearJson(Request req, string searchStr, double lat, double lon, int startRow, int maxRows)
        {
            return GetCulvertListNear(req, searchStr, lat, lon, startRow, maxRows);
        }

        [WebMethod(Description = "Retrieve Culvert list as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public Culvert[] GetCulvertJson(Request req)
        {
            return GetCulvertList(req);
        }

        [WebMethod(Description = "Retrieve Defect forms as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public FormHelper GetFormLayoutJson(Request req)
        {
            return GetFormLayout(req);
        }

        [WebMethod(Description = "Retrieve Bridge Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public BridgeInspectionData[] GetBridgeDataJson(Request req, Bridge[] bridges, int maxInsp)
        {
            return GetBridgeInsectionList(req, bridges, maxInsp);
        }

        [WebMethod(Description = "Retrieve HighMast Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public HighMastInspectionData[] GetHighMastDataJson(Request req, HighMast[] highmasts, int maxInsp)
        {
            return GetHighMastInsectionList(req, highmasts, maxInsp);
        }

        [WebMethod(Description = "Retrieve Wall Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public WallInspectionData[] GetWallDataJson(Request req, Wall[] walls, int maxInsp)
        {
            return GetWallInspectionList(req, walls, maxInsp);
        }

        [WebMethod(Description = "Retrieve Pond Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public PondInspectionData[] GetPondDataJson(Request req, Pond[] ponds, int maxInsp)
        {
            return GetPondInspectionList(req, ponds, maxInsp);
        }

        [WebMethod(Description = "Retrieve Beaver Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public BeaverInspectionData[] GetBeaverDataJson(Request req, Beaver[] beavers, int maxInsp)
        {
            return GetBeaverInspectionList(req, beavers, maxInsp);
        }

        [WebMethod(Description = "Retrieve Fence Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public FenceInspectionData[] GetFenceDataJson(Request req, Fence[] fences, int maxInsp)
        {
            return GetFenceInspectionList(req, fences, maxInsp);
        }

        [WebMethod(Description = "Retrieve Sign Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public SignInspectionData[] GetSignDataJson(Request req, Sign[] signs, int maxInsp)
        {
            return GetSignInspectionList(req, signs, maxInsp);
        }

        [WebMethod(Description = "Retrieve Culvert Inspection Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public CulvertInspectionData[] GetCulvertDataJson(Request req, Culvert[] culvert, int maxInsp)
        {
            return GetCulvertInsectionList(req, culvert, maxInsp);
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

        /*
        create table weather_forecast
        (
        id int identity(1,1) primary key,
        source varchar(64),
        forecast_time datetime,
        rdate datetime,
        temp_air decimal(5,1),
        wind_speed decimal(5,1),
        wind_direction varchar(2),
        precip_code varchar(4),
        precip_time datetime,
        snow_accum varchar(7),
        swsnow_accum varchar(7),
        action varchar(128),
        unit_num varchar(32),
        hand_code int,
        driver varchar(64),
        idate datetime
        );
        */
        // defines the relationship between WeatherForecast class and weather_forecast fields
        // first string is name in class, second is name in database
        string[,] forecastLayout = { { "Source", "source" },
                                     { "TimeOfForecast", "forecast_time" },
                                     { "RecordedAt", "rdate" },
                                     { "AirTemperature", "temp_air" },
                                     { "WindSpeed", "wind_speed" },
                                     { "WindDirection", "wind_direction" },
                                     { "SnowAccum","snow_accum" },
                                     { "SwSnowAccum", "swsnow_accum"},
                                     { "PrecipCode", "precip_code" },
                                     { "PrecipTime", "precip_time" },
                                     { "Action", "action" },
                                     { "HandCode", "hand_code" },
                                     { "UnitNum", "unit_num" },
                                     { "Driver", "driver" }};

        [WebMethod(Description = "Receive Weather Forecast Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendForecastJson(Request req, ForecastData data)
        {
            return SendForecast(req, data);
        }

        [WebMethod(Description = "Receive Weather Forecast Data from Tablet")]
        public int SendForecast(Request req, ForecastData data)
        {
            int rc = 0;

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "SendForecast"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    rc = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = procData.fillQuery(data, forecastLayout);
                        sql.AddNow("idate");

                        string where = string.Format("WHERE forecast_time={0} and driver='{1}'", SqlHelper.timeStamp(data.TimeOfForecast), data.Driver);

                        if (stmt.DoSQL("SELECT id FROM weather_forecast " + where))
                        {
                            rc = stmt.ifetch("id");     // return the ID of the existing record...
                            stmt.DoSQL(sql.BuildCmd("weather_forecast", false, where, true));
                        }
                        else
                        {
                            sql.Add("vehicle_id", getVehicleId(stmt, data.HandCode, data.UnitNum));
                            // MUST set second param of DoSQL() to true as this makes it run the insert 
                            // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted.id
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO weather_forecast", " output inserted.id VALUES "), true);
                            rc = stmt.ifetch("id");     // return the ID of the new record...
                        }
                    }
                }
                catch (Exception ex)
                {
                    rc = -3;
                    Global.messLog.Flush("SendForecast() data failed to receive: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("SendForecast() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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
            return rc;
        }


        // defines the relationship between LoginData class and patrol_log fields
        // first string is name in class, second is name in database
        string[,] loginLayout = { { "HandCode", "hand_code" },
                                  { "UnitNum", "unit_num" },
                                  { "UserName", "user_name" },
                                  { "District", "district" },
                                  { "StartTime", "start_patrol" },
                                  { "StartLatitude", "start_latitude" },
                                  { "StartLongitude", "start_longitude" },
                                  { "StartOdometer", "start_odo" },
                                  { "EndTime", "end_patrol" },
                                  { "EndLatitude", "end_latitude" },
                                  { "EndLongitude", "end_longitude" },
                                  { "EndOdometer", "end_odo" } };
        //{ "Signature", "signature" } };

        [WebMethod(Description = "Receive Login Data as JSON")]
        [ScriptMethod(UseHttpGet = false, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendPhoneLoginJson(Request req, LoginData data)
        {
            return SendTabletLogin(req, data);
        }

        [WebMethod(Description = "Receive Login Data as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int SendTabletLoginJson(Request req, LoginData data)
        {
            // couldn't seem to pass byte[] array with JSON?
            // so converted it to Base64 string and put into LoginData.strSignature
            if (data.strSignature != null)
                data.Signature = Convert.FromBase64String(data.strSignature);
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
                        SqlHelper sql = procData.fillQuery(data, loginLayout);

                        // because of some app screwup, we need to handle type casting of WinterPatrol manually as it's now defined as object
                        sql.Add("winter_patrol", myConvert.ToBoolean(data.WinterPatrol));

                        if (!string.IsNullOrEmpty(data.strImgSig))
                            sql.Add("signature", myConvert.ToByteArray(base64SigToBmp(data.strImgSig)));
                        else
                            sql.Add("signature", data.Signature);
                        sql.AddNow("udate");
                        string where = string.Format("WHERE hand_code={0} and start_patrol={1}", data.HandCode, SqlHelper.timeStamp(data.StartTime));

                        if (stmt.DoSQL("SELECT id FROM patrol_log " + where))
                        {
                            rc = stmt.ifetch("id");     // return the ID of the existing record...
                            stmt.DoSQL(sql.BuildCmd("patrol_log", false, where, true));
                        }
                        else
                        {
                            // close off 'open' login for same handcode/user
                            stmt.DoSQLFormat("update patrol_log set end_patrol={0} where hand_code={1} and user_name='{2}' and end_patrol is null", SqlHelper.timeStamp(data.StartTime), data.HandCode, data.UserName);

                            sql.Add("vehicle_id", getVehicleId(stmt, data.HandCode, data.UnitNum));
                            sql.Add("dev_id", req.Handcode);
                            // MUST set second param of DoSQL() to true as this makes it run the insert 
                            // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted.id
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
                    if (stmt != null)
                    {
                        stmt.Close();
                        stmt = null;
                    }
                }

            }
            return rc;
        }

        // defines the relationship between ReportSchedule class and report_schedule fields
        // first string is name in class, second is name in database
        string[,] scheduleLayout = { { "ReportId", "report_id" },
                                  { "UserName", "username" },
                                  { "ReportName", "report_name" },
                                  { "Email", "emails" },
                                  { "Coverage", "coverage" },
                                  { "CoverageValues", "coverage_values" },
                                  { "PeriodType", "period_type" },
                                  { "PeriodStart", "period_start" },
                                  { "PeriodEnd", "period_end" },
                                  { "RecurType", "recur_type" },
                                  { "RangeEndType", "range_end_type" } };

        [WebMethod(Description = "Add a row to the report_schedule table JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int ScheduleReportJson(Request req, ReportSchedule data)
        {
            return ScheduleReport(req, data);
        }

        [WebMethod(Description = "Add a row to the report_schedule table")]
        public int ScheduleReport(Request req, ReportSchedule data)
        {
            int rc = 0;

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "ScheduleReport"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    rc = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = procData.fillQuery(data, scheduleLayout);
                        sql.Add("currently_generating", false);
                        sql.Add("generate_right_away", true);
                        // MUST set second param of DoSQL() to true as this makes it run the insert 
                        // as an ExecuteReader instead of ExecuteNonQuery so we can get back the inserted.id
                        stmt.DoSQL(sql.BuildInsert("INSERT INTO report_schedule", " output inserted.id VALUES "), true);
                        rc = stmt.ifetch("id");     // return the ID of the new record...
                    }
                }
                catch (Exception ex)
                {
                    rc = -3;
                    Global.messLog.Flush("ScheduleReport() data failed to receive: " + ex.Message);
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
            return rc;
        }

        // defines the relationship between VehicleData class and vehicle table fields
        // first string is name in class, second is name in database
        string[,] vehicleLayout = { { "DevId", "hand_code" },
                                  { "UnitNum", "unit_num" },
                                  { "License", "license" },
                                  { "Type", "type" },
                                  { "Odom", "gps_odom" } };

        string[,] dvirHeaderLayout = { { "CommId", "comm_id" },
                                  { "handcode", "phone_code" },
                                  { "Company", "company" },
                                  { "TractorId", "vehicle_id" },
                                  { "TractorPlate", "tractor_plate" },
                                  { "Odom", "odom" },
                                  { "lat", "lat" },
                                  { "lon", "lon" },
                                  { "Inspector", "inspector" },
                                  { "Comments", "comments" },
                                  { "RepKey", "RepKey" },
                                  { "rdate", "rdate" } };

        [WebMethod(Description = "Update vehicle data from Phone App as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public int UpdateVehicleDataJson(Request req, Vehicle data)
        {
            return UpdateVehicleData(req, data);
        }

        [WebMethod(Description = "Update vehicle data from phone App")]
        public int UpdateVehicleData(Request req, Vehicle data)
        {
            int rc = -4;

            // low security check, don't want hackers!
            if (LowSecurityCheck(req, "UpdateVehicle"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    rc = -2;
                    if (GetStmt(connStr, out stmt))
                    {
                        SqlHelper sql = procData.fillQuery(data, vehicleLayout);

                        string where = string.Format("WHERE vehicle={0}", data.Id);

                        stmt.DoSQL(sql.BuildUpdate("UPDATE vehicles with (rowlock)", where));
                        rc = 1;
                    }
                }
                catch (Exception ex)
                {
                    rc = -3;
                    Global.messLog.Flush("UpdateVehicleData() data failed to receive: " + ex.Message);
                    InsertErrorRecord(req.CustCode, string.Format("UpdateVehicleData() data failed to receive HandCode:{0} Version:{1} Error:{2}", req.Handcode, req.Version, ex.Message), req);
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

        create table patrol_log
        (
        id int identity(1,1) primary key,
        hand_code int,
        unit_num varchar(16),
        user_name VARCHAR(32),
        start_patrol DATETIME,
        start_latitude DECIMAL(10,6),
        start_longitude DECIMAL(10,6),
        start_odo decimal(10,1),
        end_patrol DATETIME,
        end_latitude DECIMAL(10,6),
        end_longitude DECIMAL(10,6),
        end_odo decimal(10,1)
        );
        create index unit_num on patrol_log(unit_num);
        create index unit_start on patrol_log(unit_num,start_patrol);

        create table weather_forecast
        (
        id int identity(1,1) primary key,
        source varchar(64),
        forecast_time datetime,
        rdate datetime,
        temp_air decimal(5,1),
        wind_speed decimal(5,1),
        wind_direction varchar(2),
        snow_accum varchar(7),
        swsnow_accum varchar(7),
        precip_code varchar(4),
        precip_time datetime,
        action varchar(128)
        );
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

        [WebMethod(Description = "Prepare Frequency Page for Auto-Login")]
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

        [WebMethod(Description = "Retrieve attachment info for CityWorks service requests, response Json")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public AttachInfo[] getAttachInfoJson(Request req, int requestId)
        {
            return getAttachInfo(req, requestId);

        }

        [WebMethod(Description = "Retrieve attachment info for CityWorks service requests")]
        public AttachInfo[] getAttachInfo(Request req, int requestId)
        {
            List<AttachInfo> Result = new List<AttachInfo>();

            if (LowSecurityCheck(req, "getAttachInfo"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sql = "select srattach.*, img_events.img_url from srattach left join img_events on img_events.obj_key = srattach.objkey where srattach.requestid = {0} order by srattach.datetimeattached desc";
                        //Global.messLog.Flush("Cmd: " + sql + join + where + " order by w.start_time desc");
                        bool rc = stmt.DoSQLFormat(sql, requestId);
                        //Global.messLog.Flush("Error: " + stmt.LastError);
                        while (rc)
                        {
                            AttachInfo a = new AttachInfo();
                            a.Comments = stmt.fetch("comments");
                            a.FileName = stmt.fetch("attachment");
                            a.Id = stmt.ifetch("id");
                            a.RDate = stmt.dtfetch("datetimeattached");
                            a.RequestId = stmt.ifetch("requestid");
                            a.URL = stmt.fetch("img_url");
                            a.AttachedBy = stmt.fetch("attachedby");
                            Result.Add(a);

                            rc = stmt.dbSkip();
                        }
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
            return Result.ToArray();
        }

        [WebMethod(Description = "Retrieve comment list, optionally filtered by DC Name, as JSON")]
        [ScriptMethod(UseHttpGet = true, XmlSerializeString = false, ResponseFormat = ResponseFormat.Json)]
        public CommentData[] getCommentsJson(Request req, string Area)
        {
            return getComments(req, Area);
        }

        [WebMethod(Description = "send back all comments for past 24hrs for the given DC Name")]
        public CommentData[] getComments(Request req, string Area)
        {
            List<CommentData> Result = new List<CommentData>();

            if (LowSecurityCheck(req, "getComments"))
            {
                string connStr = CustConnectionString(req);

                SqlStmt stmt = null;
                try
                {
                    if (GetStmt(connStr, out stmt))
                    {
                        string sql = "select w.*,r.seg_id,u.first_name,u.last_name from weather_condition w";
                        string where = " where isnull(w.comment, '')<>'' and w.start_time>=getdate()-3";
                        if (!string.IsNullOrEmpty(Area))
                            where += string.Format(" and w.vehicle_id in (select vehicle from vehicles where fleet in (select id from fleet where distribution_id in (select id from distribution_center where name='{0}')))", Area);

                        string join = " left join roadSegments r on (w.start_time between r.start_time and r.end_time) and w.hand_code=r.hand_code";
                        join += " left join users u on u.username=w.driver";

                        //Global.messLog.Flush("Cmd: " + sql + join + where + " order by w.start_time desc");
                        bool rc = stmt.DoSQL(sql + join + where + " order by w.start_time desc");
                        //Global.messLog.Flush("Error: " + stmt.LastError);
                        while (rc)
                        {
                            CommentData com = new CommentData();
                            com.comment = stmt.fetch("comment");
                            com.roadSegment = stmt.fetch("seg_id");
                            com.sent = fromUTC(stmt.dtfetch("start_time"));
                            com.user = (stmt.fetch("first_name") + " " + stmt.fetch("last_name")).Trim();
                            if (com.user == string.Empty)
                                com.user = stmt.fetch("driver");

                            Result.Add(com);

                            rc = stmt.dbSkip();
                        }
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
            return Result.ToArray();
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

        private int getVehicleId(SqlStmt stmt, int hand_code, string unit_num)
        {
            int rc = 0;
            string wh;
            if (unit_num != string.Empty)
                wh = string.Format("unit_num='{0}'", unit_num);
            else
                wh = string.Format("hand_code={0}", hand_code);
            if (stmt.DoSQL("select vehicle from vehicles with (nolock) where " + wh))
                rc = stmt.ifetch("vehicle");
            return rc;
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

    public class DistInfo
    {
        public double Distance;
        public int Id;
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
