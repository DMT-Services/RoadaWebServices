using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services;
using System.Messaging;
using System.Runtime.InteropServices;
using DMTServices;

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
        public const int MAX_RECORDS = 500;
        private List<MQueue> queueList = new List<MQueue>();
        private MessLog messLog = new MessLog(@"log\webService.log");

        private string[] ReadQueueList(bool forceRefresh)
        {
            List<string> custCodes = new List<string>();
            if (queueList.Count == 0 || forceRefresh)
            {
                SqlConn mConn = new SqlConn("MainConn");
                try
                {
                    queueList.Clear();
                    mConn.Mess = messLog;
                    if (mConn.dbConnect())
                    {
                        SqlStmt stmt = new SqlStmt(mConn);
                        bool rc = stmt.DoSQL("SELECT * FROM customer");   // main database customer that points to all customers
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
                                        messLog.Flush(String.Format("MessageQueue Create Exception: {0}", ex.Message));
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
                            messLog.Flush("No customer records found with web service privileges");
                        else
                            messLog.Flush("Customer Queue List Refreshed");
                        stmt.Close();
                    }
                    else
                        messLog.Flush("ReadQueueList: Error connecting to main database!");
                }
                catch (Exception ex)
                {
                    messLog.Flush(string.Format("Excp ReadQueueList: {0}", ex.Message));
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

        private MQueue GetQueue(Request request, bool checkBin)
        {
            MQueue Result = null;
            ReadQueueList(false);      // always must check if we have read the connection list
            if(request.CustCode.Length == 4)
            {
                foreach (MQueue q in queueList)
                {
                    if (request.CustCode == q.CustCode && request.UserName == q.UserName && request.Password == q.Password)
                    {
                        if (!checkBin || request.BinPassword == "1dmtserv2")
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
                messLog.Flush(string.Format("Exception in AddToMSMQueue Cust {0} {1}", q.CustCode, ex.Message));
            }
            /*
            AddCount++;
            if (Result)
            {
                if (AddCount > 2000)
                {
                    TotCount += AddCount;
                    AddCount = 0;
                    messLog.Flush(string.Format("Queued {0} for customer {1}, waiting...", TotCount, custCodes[mNdx]));
                    while (!IsQueueNearEmpty(MessageQ[mNdx], 5))
                        System.Threading.Thread.Sleep(50);
                    messLog.Flush(string.Format("Queueing Resumed", custCodes[mNdx]));
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
            MQueue q = GetQueue(request, true);
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
            MQueue q = GetQueue(request, false);
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
                            messLog.Flush(string.Format("MakeGPSReading Excp: {0}", ex.Message));
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
            MQueue q = GetQueue(request, false);
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
                            messLog.Flush(string.Format("MakeACE Excp: {0}", ex.Message));
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

        private void InitializeComponent()
        {

        }

    }
}
