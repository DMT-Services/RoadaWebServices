using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RoadaWebService
{
    /// <summary>
    /// Response 'Data class'
    /// </summary>
    ///  
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
    [DataContract]
    public class AmazonURL
    {
        [DataMember]
        public string urlString { get; set; }
        [DataMember]
        public int Id { get; set; }
        [DataMember]
        public string ReportID { get; set; }
    }

    [DataContract]
    public class Yard
    {
        [DataMember]
        public string Name { get; set; }
        [DataMember]
        public string Address { get; set; }
        [DataMember]
        public string Province { get; set; }
        [DataMember]
        public string Postal { get; set; }
        [DataMember]
        public string Phone { get; set; }
        [DataMember]
        public string GEOFence { get; set; }
        [DataMember]
        public double BrineDensity { get; set; }
        [DataMember]
        public double PerSodium { get; set; }
        [DataMember]
        public int FleetId { get; set; }
        [DataMember]
        public bool Visible { get; set; }
    }

    public class PatrolTripData
    {
        public int TripId;              // (Server) Database determined only!  Reverse match-up can be achived using h/c, user & time.
        public int HandCode;
        public string UnitNum;
        public string UserName;
        public DateTime ReportTime;
        public double Lon;
        public double Lat;
        public object xref;
    }

    public class PatrolTripDetailData
    {
        public int TripId;
        public int Type;
        public DateTime Start;
        public double StLon;
        public double StLat;
        public DateTime Finish;
        public double FiLon;
        public double FiLat;
        public string Notes;
    }

    public class MobileRoles
    {
        public int RoleId;
        public string RoleName;
        public bool statusPerm;
        public bool hosPerm;
        public bool Collision;
        public bool weather;
        public bool routing;
        public bool diag;
        public bool camera;
        public bool super;
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

    [DataContract]
    public class Comissioninfo
    {
        [DataMember]
        public string IMEI { get; set; }
        [DataMember]
        public bool ForceUpdate { get; set; }
        [DataMember]
        public string Owner { get; set; }
        [DataMember]
        public string desc { get; set; }
        [DataMember]
        public string hardware { get; set; }
    }

    public class DCenterData
    {
        public int ID;
        public string Name;
        public double lat;
        public double lon;
        public int YardNo;

    }

    public class SideWalkData
    {
        public string address;
        public string reportId;
        public int handCode;
        public string UnitNum;
        public string userName;
        public int Colour;
        public DateTime reportTime;
        public int blocks;
        public int bays;
        public int def1;
        public int def2;
        public string notes;
        public double lon;
        public double lat;
    }

    [DataContract]
    public class DeficiencyData
    {
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public int ServerId { get; set; }

        [DataMember]
        public string ReportId { get; set; }

        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public int RequestId { get; set; }
        [DataMember]
        public int HandCode { get; set; }
        [DataMember]
        public string UnitNum { get; set; }
        [DataMember]
        public string UserName { get; set; }
        [DataMember]
        public DateTime ReportTime { get; set; }
        [DataMember]
        public int Type { get; set; }
        [DataMember]
        public int Item { get; set; }
        [DataMember]
        public int Issue { get; set; }
        [DataMember]
        public int Status { get; set; }
        [DataMember]
        public int Length { get; set; }
        [DataMember]
        public int Width { get; set; }
        [DataMember]
        public int Depth { get; set; }
        [DataMember]
        public int Option { get; set; }
        [DataMember]
        public string Note { get; set; }
        [DataMember]
        public double Lon { get; set; }
        [DataMember]
        public double Lat { get; set; }
        [DataMember]
        public bool Ready { get; set; }
        [DataMember]
        public bool Sent { get; set; }

        [DataMember]
        public string District { get; set; }

        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public string Location { get; set; }
        [DataMember]
        public bool Closed { get; set; }
        [DataMember]
        public bool Finished { get; set; }
        [DataMember]
        public string WO_Id { get; set; }
        [DataMember]
        public string WO_No { get; set; }
    }

    [DataContract]
    public class LoginData
    {
        [DataMember]
        public int HandCode { get; set; }
        [DataMember]
        public string UnitNum { get; set; }
        [DataMember]
        public string UserName { get; set; }
        [DataMember]
        public DateTime StartTime { get; set; }
        [DataMember]
        public double StartLatitude { get; set; }
        [DataMember]
        public double StartLongitude { get; set; }
        [DataMember]
        public DateTime EndTime { get; set; }
        [DataMember]
        public double EndLatitude { get; set; }
        [DataMember]
        public double EndLongitude { get; set; }
    }

    public class ColumnCheckData
    {
        public int idField;
        public string TableName;
        public string ColumnName;
        public string SqlStr;
    }

    public class ManagerData
    {
        public ManagerData()
        {
            this.managerConfig = new RoadaManagerConfig();
            this.goConfig = new RoadaGoConfig();
            this.diagConfig = new RoadaDiagConfig();
            this.usbConfig = new RoadaUSBConfig();
        }
        public bool updatenow;
        public string imei;
        public string hardware;
        public double ManagerVersion;
        public int PhoneID;
        public int HandCode;
        public RoadaManagerConfig managerConfig;
        public bool roadaGo;
        public int userVersion;
        public int VehicleVersion;
        public double goVer;
        public RoadaGoConfig goConfig;
        public bool roadaDiag;
        public double roadaDiagVer;
        public RoadaDiagConfig diagConfig;
        public bool RoadaUSB;
        public double RoadaUSBVersion;
        public RoadaUSBConfig usbConfig;
    }

    public class RoadaManagerConfig
    {
        public double version;
        public int interval;
    }

    public class RoadaUSBConfig
    {
        public double version;
    }

    public class RoadaGoConfig
    {
        public RoadaGoConfig()
        {
            this.ServerInfo = new AppServerInfo();
        }
        public double version;
        public AppServerInfo ServerInfo;
        public string description;
        public int PhotoInterval;
        public bool cameraSound;
        public bool adbOn;
        public bool AutoResume;
        public bool offlineLogin;
        public bool photoService;
        public bool eventService;
        public bool tracking;
        public bool ttsEnable;
        public int MainActivity;
        public int updatedCount;
        public int notUpdatedCount;
        public int pdTimeout;
        public int esTimeout;
        public int dmTimeout;
        public bool opps;
        public bool partolFrq;
    }

    public class RoadaDiagConfig
    {
        public RoadaDiagConfig()
        {
            this.CustomerInfo = new List<AppServerInfo>();
        }
        public double version;
        public string description;
        public List<AppServerInfo> CustomerInfo;
    }

    [DataContract]
    public class AppServerInfo
    {
        [DataMember]
        public string customerCode { get; set; }
        [DataMember]
        public string WebServiceUrl { get; set; }
        [DataMember]
        public string PhotoBucket { get; set; }
        [DataMember]
        public string PickupBucket { get; set; }
        [DataMember]
        public string ReportBucket { get; set; }
        [DataMember]
        public string ObjStoreUrl { get; set; }
        [DataMember]
        public string AmazonSecretKey { get; set; }
        [DataMember]
        public string AmazonAccessKey { get; set; }
        [DataMember]
        public string patrolFrqURL { get; set; }
    }

    [DataContract]
    public class GoUser
    {
        [DataMember]
        public int userRole { get; set; }
        [DataMember]
        public string username { get; set; }
        [DataMember]
        public string password { get; set; }
        [DataMember]
        public string email { get; set; }
        [DataMember]
        public string firstname { get; set; }
        [DataMember]
        public string lastname { get; set; }
        [DataMember]
        public string phonenumber { get; set; }
        [DataMember]
        public string title { get; set; }
    }

    [DataContract]
    public class Request
    {
        [DataMember]
        public string CustCode { get; set; }
        [DataMember]
        public string UserName { get; set; }
        [DataMember]
        public string Password { get; set; }
        [DataMember]
        public string Version { get; set; }
        [DataMember]
        public int Handcode { get; set; }
        [DataMember]
        public string BinPassword { get; set; }
        [DataMember]
        public int CustId { get; set; }
        [DataMember]
        public DateTime ReqDate { get; set; }
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

    public class evntOut
    {
        public int plan;
        public string routeName;
        public int routeID;
        public string routeNameYork;
        public int speedEvents;
        public int outOfBounds;
        public int outOfOpp;
    }

    [DataContract]
    public class evntRecord
    {
        [DataMember]
        public double last_lat;
        [DataMember]
        public double last_lon;
        [DataMember]
        public UInt32 memory_remaining;
        [DataMember]
        public int BatteryLevel;
        [DataMember]
        public bool Chargeing;
        [DataMember]
        public bool LoggedIn;
        [DataMember]
        public bool driving;
        [DataMember]
        public bool sent;
        [DataMember]
        public bool hosEvent;
        [DataMember]
        public static string licence;
        [DataMember]
        public int dcdHC;
        [DataMember]
        public int EventType;
        [DataMember]
        public DateTime eventTime;
        [DataMember]
        public string Ops;
        [DataMember]
        public int ech;
        [DataMember]
        public double version;
        [DataMember]
        public double lon;
        [DataMember]
        public double lat;
        [DataMember]
        public int heading;
        [DataMember]
        public int speed;
        [DataMember]
        public double mdistance;
        [DataMember]
        public DateTime routeTime;
        [DataMember]
        public string eventMessage;
        [DataMember]
        public string driver;
        [DataMember]
        public string truck;
        [DataMember]
        public int handcode;
        [DataMember]
        public string route;
        [DataMember]
        public int routeId;
        [DataMember]
        public double mRouteDistance;
        [DataMember]
        public int wType;
        [DataMember]
        public int waypoint;
        [DataMember]
        public int processed;
        [DataMember]
        public int comm;
        [DataMember]
        public int CStr;
        [DataMember]
        public string net_type;
        [DataMember]
        public int plan;
        [DataMember]
        public string routeName;
        [DataMember]
        public byte[] signature;
        [DataMember]
        public int SignalStr;
        [DataMember]
        public int EventQCount;
        [DataMember]
        public int qCount;
        [DataMember]
        public int PicQCount;
        [DataMember]
        public DateTime rdate;
        [DataMember]
        public DateTime idate;
        // USED only for updateing most current Go_Status
        [DataMember]
        public bool LoggedIn_GS;
        [DataMember]
        public string driver_GS;
        [DataMember]
        public int handcode_GS;
        [DataMember]
        public string truck_GS;
        [DataMember]
        public double version_GS;
        [DataMember]
        public int CStr_GS;
        [DataMember]
        public string net_type_GS;
        [DataMember]
        public int PicQCount_GS;
        [DataMember]
        public int EventQCount_GS;
        [DataMember]
        public int qCount_GS;
        [DataMember]
        public bool inYard;
        [DataMember]
        public string imei;
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
        public int userId;
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
        public string city;
        public string driver;
        public string street;
        public string url;
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

    [DataContract]
    public class CommResponse
    {
        [DataMember]
        public bool CommFlag { get; set; }
        [DataMember]
        public string Mesg { get; set; }
        [DataMember]
        public int Mode { get; set; }
        [DataMember]
        public int Id { get; set; }
        [DataMember]
        public int handcode { get; set; }
        [DataMember]
        public string dbCodes { get; set; }
        [DataMember]
        public string desc { get; set; }
        [DataMember]
        public string pcCode { get; set; }
        [DataMember]
        public int UniqueId { get; set; }
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
        public string comment;
        public string action;
        public string workOrder;
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
        public string reportTime;       // used for the unique Time code for linking photos etc..

        // user info
        public string username;         // users login name
        public string repPrep;          // Users name
        public string repTitle;         // Users title
        public string repPhone;         // Users phone
        public string equipment;        // truck number
        public string phoneID;          // Id of phone
        public byte[] Signiture;        // stores the Signiture Array
        public bool Sent;

        // location info
        public decimal CurrentLat;
        public decimal CurrentLong;
        public string hwy;              // Stores hwy number 404 401 etc...
        public string locDetails;       // stores the 
        public string locDirection;     // east west etc..
        public string locIntersection;

        public DateTime DateCol;        // date and time of Collision
        public DateTime repDate;        // date and time of arrival
        public DateTime clearDate;      // Date and time of scene cleared

        // Descriptions and details
        public string Detour;
        public string ColDesc;
        public int Fatal;
        public int injuries;
        public string Physical;
        public string Other;

        // Chks for other documents
        public int chkOpp;
        public int chkFatal;
        public int chkOther;
        public int chkSign;
        public int chkDmg;

        // Weater Data
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

        // Update
        public List<DateTime> updateTime;
        public List<string> upDesc;

        // People On scene
        public List<string> title;
        public List<string> name;
        public List<string> equip;

        // internal use for the App only
        public int HwyNdx;
        public int HwyDetNdx;
        public int HwyDirNdx;
        public int chkWeather;
        public int chkSig;
    }

    public class PhoneData
    {
        public int id { get; set; }                 // Primary key
        public int handCode { get; set; }
        public string dbcodes { get; set; }         // Db_code for Roada Go
        public string DeviceHardware { get; set; }  // type of device          
        public string Imei { get; set; }            // Unique ID from Device
        public string diagCustomers { get; set; }   // List of customers comma seperated
        public bool roadaGo { get; set; }           // Commision flag for Roada go
        public bool roadaDiag { get; set; }         // commission flag for Roada Diag
        public double goVersion { get; set; }
        public double diagVersion { get; set; }
        public double managerVersion { get; set; }
        public bool installer { get; set; }
    }

    public class VersionData
    {
        public int id { get; set; }
        public double version { get; set; }
        public string description { get; set; }
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
        public int rTim;        // burner run time
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

    public class InstalledPictures
    {
        public double lat;
        public double lon;
        public string username;
        public string objKey;
        public string unitNum;
        public int handcode;
        public string imgUrl;
        public string InstallKey;
        public DateTime pictureSent;
    }

    public class goTables
    {
        public int id;
        public string sqlStatement;
    }

    public class tabletTables
    {
        public string sqlCmd;
        public string tablename;
        public string indexname;
        public string columnname;
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

    public class CommData

    {
        public int id { get; set; }                     // Primary key

        public int handCode { get; set; }

        public string dbcodes { get; set; }             // Db_code for Roada Go

        public string DeviceHardware { get; set; }      // type of device          

        public string Imei { get; set; }                // Unique ID from Device

        public string diagCustomers { get; set; }       // List of customers comma seperated

        public bool roadaGo { get; set; }               // Commision flag for Roada go

        public bool roadaDiag { get; set; }             // comission flag for Roada Diag

        public bool installer { get; set; }

        public double goVersion { get; set; }           // version for roadaGo

        public double diagVersion { get; set; }         // version for diagVersion

        public DateTime lastCheckin { get; set; }

        public bool responseFlg { get; set; }

        public int responseType { get; set; }

        public string phoneResponse { get; set; }

        public string command { get; set; }
    }

    public class ChangeLog
    {
        public string Imei { get; set; }        // Unique ID from Device
        public int eventType { get; set; }
        public string eventLog { get; set; }
        public DateTime changeTime { get; set; }
    }

    public class PhotoData
    {
        public bool dash;
        public bool collision;
        public bool done;
        public bool ready;
        public int handcode;
        public int phandcode;
        public string unitnum;
        public int evtType;
        public decimal latitude;
        public decimal longitude;
        public int degrees;
        public int speed;
        public string repkey;
        public string username;
        public string imgUrl;
        public string custCode;
        public string objKey;
        public string bucket;
        public DateTime rdate;
        public decimal gpsAccuracy;
        public bool priv;
        public bool intall;
        public string installKey;
        public DateTime imgcreated;
        public bool sent;
        public bool sidewalk;
        public bool deficiency;
        public string imgPath;
        public bool photoSent;
        public DateTime sentTime;
        public string NetworkType;
        public int SignalStr;
        public int qCount;
        public int picQCount;
        public int eventQCount;
        public string imei;
        public bool inyard;
    }

    public class ConfigData
    {
        public double version { get; set; }             // version of the config

        public string description { get; set; }         // description of the config

        public int photoInterval { get; set; }          // photo interval in seconds

        public bool cameraSound { get; set; }           // camera sound flag

        public bool adbOn { get; set; }                 // enable adb poweroff sequence

        public bool offlineLogin { get; set; }          // enable offline Login

        public bool photoService { get; set; }          // enable photoservice

        public bool eventService { get; set; }          // enable event service

        public bool ttsEnable { get; set; }             // enable text to speech

        public int MainActivity { get; set; }           // value for MainActivity's state machine

        public int updatedCount { get; set; }           // count for devices that have successfully received config update

        public int notUpdatedCount { get; set; }        // count for devices that have not updated yet after config was changed.
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

    [DataContract]
    public class Vehicle
    {
        [DataMember]
        public int Id { get; set; }
        [DataMember]
        public int fleetID { get; set; }
        [DataMember]
        public int DevId { get; set; }
        [DataMember]
        public string UnitNum { get; set; }
        [DataMember]
        public string License { get; set; }
        [DataMember]
        public int Type { get; set; }
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public double Odom { get; set; }
    }

    [DataContract]
    public class VehicleType
    {
        [DataMember]
        public int Type { get; set; }
        [DataMember]
        public string TypeDesc { get; set; }
        [DataMember]
        public bool HasPlow { get; set; }
        [DataMember]
        public bool HasWing { get; set; }
        [DataMember]
        public bool HasTow { get; set; }
        [DataMember]
        public bool HasSpreader { get; set; }
    }

    [DataContract]
    public class DeviceInfo
    {
        [DataMember]
        public int IOstatus { get; set; }
        [DataMember]
        public string Comments { get; set; }
        [DataMember]
        public string installkey { get; set; }
        [DataMember]
        public int DevId { get; set; }
        [DataMember]
        public DateTime ServerDate { get; set; }
        [DataMember]
        public DateTime WriteDate { get; set; }
        [DataMember]
        public DateTime LoginDate { get; set; }
        [DataMember]
        public DateTime GPSDate { get; set; }
        [DataMember]
        public DateTime SpreadDate { get; set; }
        [DataMember]
        public DateTime ECMDate { get; set; }
        [DataMember]
        public double Latitude { get; set; }
        [DataMember]
        public double Longitude { get; set; }
        [DataMember]
        public int Degrees { get; set; }
        [DataMember]
        public int Speed { get; set; }
        [DataMember]
        public string FirmVers { get; set; }
        [DataMember]
        public string ModemVers { get; set; }
        [DataMember]
        public int CellStrength { get; set; }
        [DataMember]
        public bool Plow { get; set; }
        [DataMember]
        public bool Wing { get; set; }
        [DataMember]
        public bool Tow { get; set; }
        [DataMember]
        public double RoadTemp { get; set; }
        [DataMember]
        public double AirTemp { get; set; }
        [DataMember]
        public bool SpreadDry { get; set; }
        [DataMember]
        public bool SpreadWet { get; set; }
        [DataMember]
        public double DryRate { get; set; }
        [DataMember]
        public double WetRate { get; set; }
        [DataMember]
        public double RPM { get; set; }
        [DataMember]
        public double CoolTemp { get; set; }
        [DataMember]
        public double OilPress { get; set; }
        [DataMember]
        public string driver { get; set; }
        [DataMember]
        public string truck { get; set; }
        [DataMember]
        public bool online { get; set; }
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
