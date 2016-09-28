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

    public class Trip
    {
        public DateTime StartDate;
        public DateTime EndDate;
        public int TripNumber;
    }

    public class Yard
    {
        public string Name;
        public string Address;
        public string Province;
        public string Postal;
        public string Phone;
        public string GEOFence;
        public double BrineDensity;
        public double PerSodium;
        public int FleetId;
        public bool Visible;
    }

    public class NeoTerraSystemsGatewayData
    {
        public DateTime Dt;
        public double Lat;
        public double Lon;
        public double Speed;
        public int Direction;
        public string UnitID;
        public double Odometer;
    }

    public class Comissioninfo 
    {
        public string IMEI;
        public string Owner;
        public string desc;
    }

    public class Request
    {
        public string CustCode;
        public string UserName;
        public string Password;
        public string BinPassword;
        public int CustId;
        public DateTime ReqDate;
    }
    public class HosEvent 
    {
        public int code;
        public DateTime eventTime;
        public string user_name;
        public int handcode;
        public bool duty;
        public bool driving;

    }
    public class evntRecord
    {
        public int EventType;
        public DateTime eventTime;
        public string Ops;
        public int ech;
        public double version;
        public double lon;
        public double lat;
        public double mdistance;
        public DateTime routeTime;
        public string eventMessage;
        public string driver;
        public string truck;
        public int handcode;
        public string route;
        public int routeId;
        public double mRouteDistance;
        public int wType;
        public int waypoint;
        public int processed;
        public int comm;
    }
    public class DispatchStatus
    {
        public string driver;
        public string unitNum;
        public int routeId;
        public string operations;
        public int ech;        
        public DateTime timeDispached;
    }
    public class LogginData   
    {
        public int online;
        public string user;
        public string password;
        public string truck;       
    }
    public class UserInfo
    {
        public string FirstName;
        public string LastName;
        public string Phone;
        public string Email;
        public string title;
        public string EmployeeNum;
        public int role;
        public int truckStatus;
        public int routing;
        public int weatherRep;
        public int icReports;
        public int fullDiag;
        public int camPerm;
    }

    public class ImgRec
    {
        public int HC;
        public DateTime rDate;
        public int EType;
        public double Lat;
        public double Lon;
        public int Sog;
        public int Cog;
        public byte[] Img;
    }
    public class CustomerSettings
    {
        public string PortNumber;
        public string CameraIP;
        public string TrackingIP;
        public int Handcode;
        public string ErrorResp;
    }
    public class CommResponse
    {
        public bool CommFlag;
        public string Mesg;
        public int Mode;
        public int Id;
        public int handcode;
        public string dbCodes;
        public string desc;
        public string pcCode;
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

    public class WeatherData
    {
        public string reportId;
        public DateTime fromTime;
        public int commId;
        public int handCode;
        public string unitNum;
        public string driver;
        public int percipitationType;
        public int percipitationLvl;
        public int windStrength;
        public int windDirection;
        public int visibility;
        public int cloudCondition;
        public int fogLvl;
        public int drifting;
        public int roadCondition;
        public int road2ndCondition;
        public decimal CurrentLat;
        public decimal CurrentLong;
        public double calcLat;
        public double calcLong;
        public string address;
        public double tempAir;
        public double tempRoad;
        public bool validLoc;
    }

    public class WeatherLocation
    {
        public double calcLat;
        public double calcLong;
        public string address;
        public double tempAir;
        public double tempRoad;
    }

    public class installer
    {
        public int deviceId;
        public int vechileId;
        public string comments;
        public byte[] image;
    }

    public class ReportData
    {
        public string reportTime; //used for the unique Time code for linking photos etc..

        //user info
        public string repPrep; //USers name
        public string repTitle; //USers title
        public string repPhone;  // Users phone
        public string equipment; // truck number
        public string phoneID; //Id of phone
        public byte[] Signiture; //stores the Signiture Array

        //location info
        public decimal CurrentLat;
        public decimal CurrentLong;
        public string hwy; //Stores hwy number 404 401 etc...
        public string locDetails; //stores the 
        public string locDirection; // east west etc..
        public string locIntersection;

        public DateTime DateCol; //date and time of Colision
        public DateTime repDate; //date and time of arrival
        public DateTime clearDate; // Date and time of scene cleared

        //Descriptions and details
        public string Detour;
        public string ColDesc; 
        public int Fatal;
        public int injuries; 
        public string Physical; 
        public string Other; 

        //Chks for other documents
        public int chkOpp; 
        public int chkFatal; 
        public int chkOther;
        public int chkSign;
        public int chkDmg;

        

        //Weater Data
        public List<DateTime> fromTime;
        public List<int> percipitationType;
        public List<int> percipitationLvl;
        public List<int> windStrength;
        public List<int> windDirection;
        public List<int> visibility;
        public List<int> cloudCondition;
        public List<int> fogLvl;
        public List<int> drifting;
        public List<int> roadCondition;
        

        //Update
        public List<DateTime> updateTime;
        public List<string> upDesc;

        //People On scene
        public List<string> title;
        public List<string> name;       
        public List<string> equip;

        //internal use for the App only
        public int HwyNdx;
        public int HwyDetNdx;
        public int HwyDirNdx;
        public int chkWeather;
        public int chkSig;

        

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
    public class InstalledPictures 
    {
        public string imgUrl;
        public string InstallKey;
        public DateTime pictureSent;
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
    }

    public class VehicleType
    {
        public int Type;
        public string TypeDesc;
        public bool HasPlow;
        public bool HasWing;
        public bool HasTow;
        public bool HasSpreader;
    }

    public class DeviceInfo
    {
        public string Comments;
        public string installkey;
        public int DevId;
        public DateTime ServerDate;
        public DateTime WriteDate;
        public DateTime LoginDate;
        public DateTime GPSDate;
        public DateTime SpreadDate;
        public DateTime ECMDate;
        public double Latitude;
        public double Longitude;
        public int Degrees;
        public int Speed;
        public string FirmVers;
        public string ModemVers;
        public int CellStrength;
        public bool Plow;
        public bool Wing;
        public bool Tow;
        public double RoadTemp;
        public double AirTemp;
        public bool SpreadDry;
        public bool SpreadWet;
        public double DryRate;
        public double WetRate;
        public double RPM;
        public double CoolTemp;
        public double OilPress;
    }


    public class TPatrol511
    {
        public int HandCode;
        public DateTime FromDate;
        public DateTime ToDate;
        public DateTime FromTime;
        public DateTime ToTime;

        public int FromSegment;
        public int ToSegment;

        public int PCPercipitationConditions;
        public string PCPercipitationConditionsAN;
        public int PCPercipitation;
        public int PCPercipitationAN;
        public int ACWind;
        public int ACWindAN;
        public string ACWindDirection;

        public int ACVisibility;
        public int ACVisibilityAN;
        public int ACSkyCloudConditions;
        public int ACSkyCloudConditionsAN;
        public int ACFog;
        public int ACFogAN;
        public int ACDrifting;
        public int ACDriftingAN;

        public int RoadConditions;
        public int RoadConditionsAN;
    }

    public class Route
    {
        public int Id;
        public string Code;
        public int Area;
        public int Yard;
        public string Name;
        public string Info;
        public DateTime Date;
        public DateTime Last;
        public int RType;
        public RoutePoint[] RoutePoints;
        public WayPoint[] WayPoints;
    }

    public class RoutePoint
    {
        public DateTime Dt;
        public double Lat;
        public double Lon;
        public int Spd;
        public int Dir;
        public double Dst;
        public int Order;
    }

    public class WayPoint : RoutePoint
    {
        public int WTp;
        public bool done;
        public string Txt;
        
    }

    public class SpatData
    {
        public long SpatialId;
        public double Latitude;
        public double Longitude;
        public long[] ObjIds = new long[3];
        public double[] Dist = new double[3];

        public SpatData(double lat, double lon)
        {
            Latitude = lat;
            Longitude = lon;
        }
    }
}
