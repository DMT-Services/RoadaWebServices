using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Messaging;
using System.Collections;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace RoadaWebService
{
    /// <summary>
    /// Response 'Data class'
    /// </summary>
    public class Response
    {
        public bool Success;
        public int ErrCode;
        public string ErrMessage;
    }

    public class Request
    {
        public string CustCode;
        public string UserName;
        public string Password;
        public string BinPassword;
        public int CustId;
    }

    public class CommResponse
    {
        public bool CommFlag;
        public string Mesg;
        public int Mode;
        public int Id;
        public int UniqueId;
    }

    public class XBeeCommResponse
    {
        public string CustCode;
        public int CustId;
        public int PanId;
        public int TzOff;
        public int GwyId;
        public int PollRt;
        public bool DSTApp;
        public int UnixTim;
    }

    public class XBAck
    {
        public string Addr;     // address of device
        public bool rOn;        // remote on request
        public bool rOff;       // remote off request
        public bool sReq;       // settings request
    }

    public class XBiB
    {
        public string Addr;     // address of device
        public string Td;       // time and date of iButton read
        public string Code;     // iButton code
    }

    public class XBResp
    {
        public string Addr;     // address of device
        public bool rOn;        // remote on request
        public bool rOff;       // remote off request
        public bool sReq;       // settings request
        public int rTim;       // burner run time
        public int[] wTim;      // week day times
        public double mTemp;    // minimum temperature
        public DateTime oDay;   // override day
    }

    public class XBData
    {
        public string Addr; // address of device
        public int In;      // input status
        public bool tV;     // temperature is valid
        public double tVal; // temperature value
        public int Tim;     // current running time left, minutes
        public string Ver;  // software version
    }

    public class MQueue
    {
        public string CustCode;
        public string ConnectionString;
        public string UserName;
        public string Password;
        public string QName;
        public string Name;
        public System.Messaging.Message MyMessage;
        public MessageQueueTransaction MessageTx;
        public MessageQueue MessageQ;
    }

    public class GPSData
    {
        public int DevId;
        public double Latitude;
        public double Longitude;
        public DateTime UTCDate;
        public int COG;
        public int SOG;
        public bool TempValid;
        public double TempSurface;
        public double TempAir;
        public int MetersTravelled;
        public bool PlowInp;
        public bool WingInp;
        public bool TowInp;
        public bool Moving;
        public bool Stopped;
        public int CellStrength;
        public DateTime IDate;
    }

    public class ACEData
    {
        public int DevId;
        public double Latitude;
        public double Longitude;
        public DateTime UTCDate;
        public int COG;
        public int SOG;
        public int TempSurface;
        public int TempAir;
        public bool PlowInp;
        public bool WingInp;
        public bool TowInp;
        public byte[] ContID;
        public byte[] OpID;
        public int SpreadRate;
        public int BlastRate;
        public int PrewetRate;
        public int SpinnerWidth;
        public byte MatType;
        public byte EventType;
        public byte ErrorBits;
        public int VehSpeed;
        public int Inputs;
        public int SysState;
        public int TripDist;
        public int TripSolid;
        public int TripLiquid;
        public int ServicedDist;
    }

    public class ReplicationData
    {
        public int RecordId;
        public string SqlStr;
        public string CustCode;
        public SQLParameter[] Params;
    }

    /// <summary>
    /// must be able to serialize, so can't use a hashtable, make my own
    /// </summary>
    public class SQLParameter
    {
        public string Key;
        public object Value;
    }

    public class VersionInfo
    {
        public string Name;
        public string Vers;
    }

    public class Client
    {
        public string CustCode;
        public string Name;
        public bool SaveData;
    }

    public class Vehicle
    {
        public int DevId;
        public string UnitNum;
        public string License;
        public int Type;
        public string TypeDesc;
        public bool HasPlow;
        public bool HasWing;
        public bool HasTow;
        public bool HasSpreader;
    }
}
