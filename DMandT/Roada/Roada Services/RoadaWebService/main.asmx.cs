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
using System.Xml;

namespace RoadaWebService
{
    /// <summary>
    /// Roada Web Service, used for Wabasto Heaters among other things
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
        private List<MQueue> custList = new List<MQueue>();
        const string DEFAULT_USER = "DMTServices";
        const string DEFAULT_PASSWORD = "1Secr3t2";
        const string RECVD_PATH = @"Recvd\";
        const string OBJECTS_PATH = @"objects\";
        const string LONG_DATE = "yyyy-MM-dd HH:mm:ss";
        const int DEFAULT_RUN_TIME = 40;
        private const int MaxDownloadBatchSize = 1000;
        // spatial data lookup

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
 *                                  if (! MessageQueue.Exists(q.QName))
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
                    if (! AddToMSMQueue(q, b))
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
            bool Result = (request.UserName == DEFAULT_USER && request.Password == DEFAULT_PASSWORD);
            if (!Result)
                Global.messLog.Flush("LowSecurityCheck() failed with {0} and {1}", request.UserName, request.Password);
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
            if(LowSecurityCheck(request))
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

            if (! LowSecurityCheck(request))
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
            if (LowSecurityCheck(request))
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

                        string whereClause =  string.Format("WHERE code = '{0}'", sql.FixUpEx(route.Code));
                        bool insFlag = ! stmt.DoSQLFormat("SELECT id FROM routes {0}", whereClause);
                        int routeId = -1;
                        if (! insFlag)
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
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }

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
            finally
            {
                stmt.Close();
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
            if (LowSecurityCheck(request))
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
            if (LowSecurityCheck(request))
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
                    Global.messLog.Flush("RequestYards() Ex: {0}", ex.Message);
                }
                finally
                {
                    if (stmt != null)
                        stmt.Close();
                }
            }
            return Result;
        }


        [WebMethod(Description = "Commission Software")]
        public CommResponse CommSoftware(Request request, string guid, string descr)
        {
            CommResponse response = new CommResponse();
            response.CommFlag = false;
            response.Mesg = "Unknown error";
            // added security!!
            if (LowSecurityCheck(request))
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

        [WebMethod(Description = "Soft activate for phone app")]
        public CommResponse PhoneSoft(Request request, string guid)
        {
            CommResponse response = new CommResponse();
            response.CommFlag = false;
            response.Mesg = "Unknown error";
            // added security!!
            if (LowSecurityCheck(request))
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

        private void ActivateHandCode (int handcode, string IMEI)
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
            

            if (!LowSecurityCheck(request))
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
                    Global.messLog.Flush("Excp in GetClients: " + ex.Message);
              
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
        public ReplicationData[] GetReplicationData( Request request, string dbCode, int startRecord )
        {
            // I needed to preserve this older version for backwards compatibility.
            return GetReplicationDataX(request, dbCode, "", -1, startRecord);
        }

        [WebMethod(Description = "Retrieve customer list from the server")]
        public Client[] GetClientList(Request request)
        {
            Client[] Result = null;
            if (!LowSecurityCheck(request))
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
                        foreach(XBData x in dat)
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
                                if (! heaterFound)
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
                catch(Exception x)
                {
                    Global.messLog.Flush("CommissionGateway() Excp: {0}", x.Message);
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
                    Global.messLog.Flush("ValidateUser() Excp: {0}", x.Message);
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

            if (! LowSecurityCheck(request))
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
                        if (! stmt.IsNull("cell_strength"))
                            dInfo.CellStrength = stmt.ifetch("cell_strength");
                        if (stmt.DoSQLFormat("SELECT TOP 1 * FROM messagein WHERE hand_code = {0} and rdate >= {{ts '{1}'}}", devId, dInfo.GPSDate.ToString("yyyy-MM-dd HH:mm:ss")))
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

                        if (stmt.DoSQLFormat("SELECT Max(id) as maxid FROM EngineIn WHERE hand_code = {0} and rdate >= {{ts '{1}'}}", devId, DateTime.UtcNow.AddMinutes(-30).ToString(LONG_DATE)))
                        {
                            if (!stmt.IsNull("maxid"))
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
                        }
                    
                    }
                }
            }
            catch (Exception x)
            {
                Global.messLog.Flush("GetDeviceInfo() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
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
                    if (wdata.fromTime.Year > 1000)
                    {
                        sql.Clear();
                        sql.Add("ci_id",wdata.reportId);    
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
                        sql.AddNow("idate");

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO weather_condition"));
                                 
                        Result = 1;
                    }
                }
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendRpt() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
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
                    sql.Add("submit_time",now);
                    sql.Add("title",rdata.repTitle);
                    sql.Add("equipment",rdata.equipment);

                     //adds and updates main report table 
                     if (stmt.DoSQL(String.Format("SELECT ci_id FROM collision_incident WHERE ci_id = '{0}-{1}'", rdata.phoneID, rdata.reportTime)))                        
                     {                                
                                            
                        stmt.DoSQL(sql.BuildUpdate("UPDATE collision_incident", String.Format("WHERE ci_id = '{0}-{1}'", rdata.phoneID, rdata.reportTime)));

                        Result = 1;

                    
                    }
                    else
                    {                                              
                        sql.Add("ci_id", String.Format("{0}-{1}",rdata.phoneID, rdata.reportTime));                       

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO collision_incident"));
                        
                        Result = 2;
                    }

                    

                     //updates add adds update files to update table 
                     int ndx = 0;
                     foreach (DateTime record in rdata.updateTime)
                     {
                         if (stmt.DoSQL(String.Format("SELECT ci_id FROM collision_updates WHERE ci_id = '{0}-{1}' AND update_time = '{2}.000'",rdata.phoneID , rdata.reportTime, rdata.updateTime[ndx].ToString("yyyy-MM-dd HH:mm:ss"))))
                         {
                             sql.Clear();
                             sql.Add("description", rdata.upDesc[ndx]);
                             stmt.DoSQL(sql.BuildUpdate("UPDATE collision_updates", string.Format("WHERE ci_id = '{0}-{1}' AND start_time = '{2}.000'",rdata.phoneID, rdata.reportTime, rdata.updateTime[ndx].ToString("yyyy-MM-dd HH:mm:ss"))));
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

                     int pplNdx = 0;
                     foreach (string name in rdata.name)
                     {
                         if (stmt.DoSQL(String.Format("SELECT * FROM report_ppl WHERE ci_id = '{0}-{1}' AND name = '{2}' AND title = '{3}' AND equip = '{4}' ", rdata.phoneID, rdata.reportTime, rdata.name[pplNdx], rdata.title[pplNdx], rdata.equip[pplNdx])))                         
                         {
                             pplNdx++;
                         }
                         else
                         {
                             sql.Clear();
                             sql.Add("ci_id", String.Format("{0}-{1}", rdata.phoneID, rdata.reportTime));
                             sql.Add("name", rdata.name[pplNdx]);
                             sql.Add("title", rdata.title[pplNdx]);
                             sql.Add("equip", rdata.equip[pplNdx]);
                             stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO report_ppl"));
                             pplNdx++;
                             Result = 6;
                         }
                     
                     }
                     pplNdx = 0; 
                 } 
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendRpt() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
                return -2;

            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                Result = -3;
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.AddNow("idate");
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
                Global.messLog.Flush("SendImage() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
                return -2;

            SqlConn mConn = new SqlConn("MainConn");
            SqlStmt stmt = null;
            try
            {
                stmt = new SqlStmt(mConn);
                Result = -3;

                Global.messLog.Flush("SendCommission() Guid {0}", comInfo.IMEI);
                if (!stmt.DoSQL(String.Format("SELECT * FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI)))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    sql.Add("pc_code", comInfo.Owner);
                    sql.Add("client_guid", comInfo.IMEI);
                    sql.Add("description", comInfo.desc);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO soft_activate"));
                }

                stmt.DoSQLFormat("SELECT id FROM soft_activate WHERE client_guid = '{0}'", comInfo.IMEI);

                Result = stmt.ifetch("id"); //returns the newly created ID for the phones Confiuration  
            }
            catch (Exception x)
            {
                Result = -4;
                Global.messLog.Flush("SendCommission() Excp: {0}", x.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        [WebMethod(Description = "Send Phone New Route and Operations")]
        public DispatchStatus UpdateDispatch(Request req, DispatchStatus info)
        {
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req))
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
                Global.messLog.Flush("UpdateDispatch() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
                return -1;
            int status = 0;
            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                    stmt.DoSQLFormat("SELECT * FROM go_events WHERE truck = '{0}' and comm_type = 1 and isnull(processed,0) = 0 order by event_time desc", truck);

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

            if (!LowSecurityCheck(request))
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
                Global.messLog.Flush("Get Hos Events() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
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
                    sql.Add("duty",evnt.duty);
                    sql.Add("driving", evnt.driving);
                    stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_hos"));

                    Result = 1;
                }
            }
            catch (Exception x)
            {
                Result = -3;
                Global.messLog.Flush("HOS Event Message Failed HOS HandCode: {0}", evnt.handcode.ToString());
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
            if (!LowSecurityCheck(req))
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
                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                        DateTime now = DateTime.Now.ToLocalTime();

                        sql.Add("install", 1);
                        sql.Add("hand_code",data.handcode);
                        sql.Add("unit_num",data.unitNum);
                        sql.Add("obj_key",data.objKey);
                        sql.Add("rdate", now);
                        sql.Add("idate", data.pictureSent);
                        sql.Add("installkey", data.InstallKey);
                        sql.Add("img_url", data.imgUrl);
                       

                        if (!stmt.DoSQLFormat("SELECT id FROM Img_Events WHERE obj_key = '{0}'", data.objKey))
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO Img_Events"));

                        sql.Clear();
                    }
                    

                    Result = 1;
                }
            }
            catch (Exception x)
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

        [WebMethod(Description = "Puts installed Data Away")]
        public int SendInstallData(Request req, DeviceInfo data)
        {
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req))
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

                    if (stmt.DoSQL(String.Format("SELECT * FROM InstallData WHERE devID = '{0}'", data.DevId)))
                    {

                        stmt.DoSQL(sql.BuildUpdate("UPDATE InstallData", String.Format("WHERE devID = '{0}'", data.DevId)));

                        Result = 1;

                    }
                    else
                    {

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO InstallData"));

                        Result = 2;
                    }
              
                    Result = 1;
                }
            }
            catch (Exception x)
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

        [WebMethod(Description = "Comissions Intaller app")]
        public int ComissionInstaller(Request req, string imei, out bool commissioned, out string Customers)
        {
            Customers = "";
            commissioned = false;
            int Result = -1;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req))
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
                    Result = stmt.ifetch("id"); //returns the newly created ID for the phones Confiuration    

                    commissioned = stmt.bfetch("installer");                    
                    if (commissioned)
                    {
                        Customers = stmt.fetch("db_codes");
                    }

                }
            }
            catch (Exception x)
            {
                Result = -3;       
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }

            return Result;
        }

        [WebMethod(Description = "Consumes Event records send from Roada Go")]
        public int SendEventRecord(Request req, evntRecord evnt)
        {
            int Result = 8;
            // low security check, don't want hackers!
            if (!LowSecurityCheck(req))
                return 8;
          
            string connStr = CustConnectionString(req);

            SqlStmt stmt = null;
            try
            {
                
                Result = 8;
                if (GetStmt(connStr, out stmt))
                {
                    if (evnt.EventType == 1) //Driver route Confirmation
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
                        sql.Add("route",evnt.route);
                        sql.Add("route_id",evnt.routeId);
                        sql.Add("waypoint", evnt.waypoint);
                        sql.Add("processed", 0);
                        sql.Add("comm_type", 0);

                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));

                    
                            Result = 1;
                                               
                    }
                    if (evnt.EventType == 2) //Driver Login
                    {

                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                        sql.Add("event_type", evnt.EventType);
                        sql.Add("event_time", evnt.eventTime);                 
                        sql.Add("event_mess", evnt.eventMessage);
                        sql.Add("driver", evnt.driver);
                        sql.Add("truck", evnt.truck);
                        sql.Add("hand_code", evnt.handcode);
                        sql.Add("route", evnt.route);                        
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));
                        
                        sql.Clear();

                        if (!stmt.DoSQLFormat("SELECT * FROM go_status WHERE hand_code = {0}", evnt.handcode))
                        {
                            sql.Add("logged_in", 1);
                            sql.Add("hand_code", evnt.handcode);
                            sql.Add("truck_id", evnt.truck);
                            sql.Add("version", evnt.version);
                            sql.Add("user_id", evnt.driver);
                            sql.Add("last_log", evnt.eventTime);
                            sql.Add("route", evnt.route);      
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO go_status"));                    

                        }
                        else
                        {                         
                            sql.Add("logged_in", 1);
                            sql.Add("hand_code", evnt.handcode);
                            sql.Add("truck_id", evnt.truck);
                            sql.Add("version", evnt.version);
                            sql.Add("user_id", evnt.driver);
                            sql.Add("last_log", evnt.eventTime);
                            sql.Add("route", evnt.route);  
                            stmt.DoSQLFormat(sql.BuildUpdate("UPDATE go_status with (rowlock)", "WHERE hand_code = {0}"), evnt.handcode);
                        }


                       
                       Result = 2;

                    }
                    if (evnt.EventType == 3) //Device Checkin
                    {

                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                        sql.Add("event_type", evnt.EventType);
                        sql.Add("event_time", evnt.eventTime);
                        sql.Add("event_mess", evnt.eventMessage);
                        sql.Add("driver", evnt.driver);
                        sql.Add("truck", evnt.truck);
                        sql.Add("latitude", evnt.lat);
                        sql.Add("longitude", evnt.lon);
                        sql.Add("hand_code", evnt.handcode);
                        sql.Add("m_distance", evnt.mRouteDistance);
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));

                        Result = 3;

                    }
                    if (evnt.EventType == 4) //Driver Logout
                    {

                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                        sql.Add("event_type", evnt.EventType);
                        sql.Add("event_time", evnt.eventTime);
                        sql.Add("event_mess", evnt.eventMessage);
                        sql.Add("driver", evnt.driver);
                        sql.Add("truck", evnt.truck);
                        sql.Add("hand_code", evnt.handcode);
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));

                        sql.Clear();

                        if (!stmt.DoSQLFormat("SELECT * FROM go_status WHERE hand_code = {0}", evnt.handcode))
                        {

                            sql.Add("logged_in", 0);
                            sql.Add("hand_code", evnt.handcode);
                            sql.Add("truck_id", evnt.truck);
                            sql.Add("user_id", evnt.driver);
                            sql.Add("last_log", evnt.eventTime);
                            stmt.DoSQL(sql.BuildInsert("INSERT INTO go_status"));

                        }
                        else
                        {
                            sql.Add("logged_in", 0);
                            sql.Add("hand_code", evnt.handcode);
                            sql.Add("truck_id", evnt.truck);
                            sql.Add("user_id", evnt.driver);
                            sql.Add("last_log", evnt.eventTime);
                            stmt.DoSQLFormat(sql.BuildUpdate("UPDATE go_status with (rowlock)", "WHERE hand_code = {0}"), evnt.handcode);
                        }


                        Result = 4;

                    }
                    if (evnt.EventType == 5) //Waypoint event
                    {

                        SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);

                        sql.Add("event_type", evnt.EventType);
                        sql.Add("event_time", evnt.eventTime);
                        sql.Add("event_mess", evnt.eventMessage);
                        sql.Add("driver", evnt.driver);
                        sql.Add("truck", evnt.truck);
                        sql.Add("latitude", evnt.lat);
                        sql.Add("longitude", evnt.lon);
                        sql.Add("hand_code", evnt.handcode);
                        sql.Add("route_id",evnt.routeId);
                        sql.Add("m_distance", evnt.mdistance);                       
                        sql.Add("waypoint", evnt.waypoint);
                        sql.Add("comm_type", 0);
                        sql.Add("processed", 0);
                        sql.Add("wtype", evnt.wType);
                        stmt.DoSQLFormat(sql.BuildInsert("INSERT INTO go_events"));

                        Result = 5;

                    }
                    //checks to see if dispatch has changed
                    if (GetDispatch(req, evnt.truck, evnt.routeId) == 1)
                    {
                        Result = 99;
                    }
                }
               
            }
            catch (Exception x)
            {
                Result = 4;
                Global.messLog.Flush("SendEventRecord() Excp: {0}", x.Message);
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
            if (!LowSecurityCheck(req))
                return data;

            string connStr = CustConnectionString(req);
            SqlStmt stmt = null;
            try
            {                
                if (GetStmt(connStr, out stmt))
                {
                                       
                    SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                      
                   stmt.DoSQLFormat("SELECT * FROM go_status WHERE hand_code = '{0}'",handcode);

                   data.online = stmt.ifetch("logged_in");
                   data.truck = stmt.fetch("truck_id");
                   data.user = stmt.fetch("user_id");
                   
                   stmt.DoSQLFormat("SELECT password FROM users WHERE username = '{0}'", data.user);
                   data.password = stmt.fetch("password");
               
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
            return data;
        }


        [WebMethod(Description = "Retrieve vehicle listing")]
        public Vehicle[] GetVehicleList(Request request, int? fleetId)
        {
            Vehicle[] Result = null;

            if (! LowSecurityCheck(request))
                return Result;

            string connStr = CustConnectionString(request);
            SqlStmt stmt = null;
            try
            {
                if (GetStmt(connStr, out stmt))
                {
                    List<Vehicle> vehicles = new List<Vehicle>();
                    bool rc;
                    if (fleetId != null)
                        rc = stmt.DoSQLFormat("SELECT * FROM vehicles WHERE fleet = {0} ORDER BY unit_num", fleetId);
                    else
                        rc = stmt.DoSQL("SELECT * FROM vehicles ORDER BY unit_num");
                    while (rc)
                    {
                        Vehicle v = new Vehicle();
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
            catch(Exception x)
            {
                Global.messLog.Flush("GetVehicleList() Excp: {0}", x.Message);
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

            if (! LowSecurityCheck(request))
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
                Global.messLog.Flush("GetVehicleTypes() Excp: {0}", x.Message);
            }
            finally
            {
                if (stmt != null)
                    stmt.Close();
            }
            return Result;
        }

        private bool GetStmt(string connStr, out SqlStmt stmt)
        {
            stmt = new SqlStmt();
            stmt.Mess = Global.messLog;
            stmt.ConnectionData = connStr;
            bool Result = stmt.dbConnect();
            if (! Result)
                Global.messLog.Flush("Failed to connect to {0}", connStr);
            return Result;
        }

        [WebMethod(Description = "Retrieve current GPS readings")]
        public GPSData[] GPSDataRequest(Request request, bool firstFlag, int devId, string unitNum)
        {
            GPSData[] Result = null;

            if (! LowSecurityCheck(request))
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
                Global.messLog.Flush("GPSDataRequest() Excp: {0}", x.Message);
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
        public ReplicationData[] GetReplicationDataY( Request request,
                                                      string dbCode, string region, int histMax, int startRecord, int limit )
        {
            /* Note:    For DB that have multiple regions (and a lot of data across them), we can narrow what we
             *          replicate by "region" (see 'yard' table, 'name' field).  As well, should there be a lot
             *          of data in a database, and we only want more recent material, we can limit by how many
             *          months maximum (in "histMax") we want (value of -1 will have this parameter ignored).
             */
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
                                    sql +=  string.Format(" AND idate >= '{0}'", max.ToString("yyyy-MM-dd"));
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
}
