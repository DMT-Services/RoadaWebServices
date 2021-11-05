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
        [DataMember]
        public int Dc_Id { get; set; }
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
    public class ReportSchedule
    {
        [DataMember]
        public string ReportId { get; set; }
        [DataMember]
        public string UserName { get; set; }
        [DataMember]
        public string ReportName { get; set; }
        [DataMember]
        public string Email { get; set; }
        [DataMember]
        public int Coverage { get; set; }
        [DataMember]
        public string CoverageValues { get; set; }
        [DataMember]
        public int PeriodType { get; set; }
        [DataMember]
        public DateTime PeriodStart { get; set; }
        [DataMember]
        public DateTime PeriodEnd { get; set; }
        [DataMember]
        public int RecurType { get; set; }
        [DataMember]
        public int RangeEndType { get; set; }
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

    [DataContract]
    public class SideWalkData
    {
        [DataMember]
        public string address { get; set; }
        [DataMember]
        public string reportId { get; set; }
        [DataMember]
        public int handCode { get; set; }
        [DataMember]
        public string UnitNum { get; set; }
        [DataMember]
        public string userName { get; set; }
        [DataMember]
        public int Colour { get; set; }
        [DataMember]
        public DateTime reportTime { get; set; }
        [DataMember]
        public int blocks { get; set; }
        [DataMember]
        public int bays { get; set; }
        [DataMember]
        public int def1 { get; set; }
        [DataMember]
        public int def2 { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public double lon { get; set; }
        [DataMember]
        public double lat { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }
    }

    [DataContract]
    public class DefiPicData
    {
        [DataMember]
        public string reportId { get; set; }
        [DataMember]
        public string[] photos { get; set; }
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
        public string addNotes { get; set; }
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
    public class ForecastData
    {
        [DataMember]
        public string Source { get; set; }
        [DataMember]
        public DateTime TimeOfForecast { get; set; }
        [DataMember]
        public DateTime RecordedAt { get; set; }
        [DataMember]
        public double AirTemperature { get; set; }
        [DataMember]
        public double WindSpeed { get; set; }
        [DataMember]
        public string WindDirection { get; set; }
        [DataMember]
        public string SnowAccum { get; set; }
        [DataMember]
        public string SwSnowAccum { get; set; }
        [DataMember]
        public string PrecipCode { get; set; }
        [DataMember]
        public DateTime PrecipTime { get; set; }
        [DataMember]
        public string Action { get; set; }
        [DataMember]
        public int HandCode { get; set; }
        [DataMember]
        public string UnitNum { get; set; }
        [DataMember]
        public string Driver { get; set; }
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
        public double StartOdometer { get; set; }
        [DataMember]
        public DateTime EndTime { get; set; }
        [DataMember]
        public double EndLatitude { get; set; }
        [DataMember]
        public double EndLongitude { get; set; }
        [DataMember]
        public double EndOdometer { get; set; }
        [DataMember]
        public string District { get; set; }
        [DataMember]
        public byte[] Signature { get; set; }
        [DataMember]
        public string strSignature { get; set; }
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
        [DataMember]
        public string Name { get; set; }
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
    public class ServerIDs
    {
        [DataMember]
        public int[] serverIds { get; set; }
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

    [DataContract]
    public class HosEvent
    {
        [DataMember]
        public int code { get; set; }
        [DataMember]
        public DateTime eventTime { get; set; }
        [DataMember]
        public string user_name { get; set; }
        [DataMember]
        public int handcode { get; set; }
        [DataMember]
        public bool duty { get; set; }
        [DataMember]
        public bool driving { get; set; }
        [DataMember]
        public int phandcode { get; set; }
        [DataMember]
        public int serverID { get; set; }
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
    public class Sigdata
    {
        [DataMember]
        public DateTime datetime { get; set; }
        [DataMember]
        public List<short> signature { get; set; }
    }


    [DataContract]
    public class Employee
    {
        [DataMember]
        public int Id { get; set; }
        [DataMember]
        public string employee_num { get; set; }
        [DataMember]
        public string first_name { get; set; }
        [DataMember]
        public string last_name { get; set; }
        [DataMember]
        public int dc_id { get; set; }
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

    [DataContract]
    public class WeatherData
    {
        [DataMember]
        public string reportId { get; set; }
        [DataMember]
        public DateTime fromTime { get; set; }
        [DataMember]
        public int commId { get; set; }
        [DataMember]
        public int handCode { get; set; }
        [DataMember]
        public string unitNum { get; set; }
        [DataMember]
        public string driver { get; set; }
        [DataMember]
        public int percipitationType { get; set; }
        [DataMember]
        public int percipitationLvl { get; set; }
        [DataMember]
        public int windStrength { get; set; }
        [DataMember]
        public int windDirection { get; set; }
        [DataMember]
        public int snowAccum { get; set; }
        [DataMember]
        public int swsnowAccum { get; set; }
        [DataMember]
        public int visibility { get; set; }
        [DataMember]
        public int cloudCondition { get; set; }
        [DataMember]
        public int fogLvl { get; set; }
        [DataMember]
        public int drifting { get; set; }
        [DataMember]
        public int roadCondition { get; set; }
        [DataMember]
        public int road2ndCondition { get; set; }
        [DataMember]
        public int sidewalkCondition { get; set; }
        [DataMember]
        public decimal CurrentLat { get; set; }
        [DataMember]
        public decimal CurrentLong { get; set; }
        [DataMember]
        public double calcLat { get; set; }
        [DataMember]
        public double calcLong { get; set; }
        [DataMember]
        public string address { get; set; }
        [DataMember]
        public double tempAir { get; set; }
        [DataMember]
        public double tempRoad { get; set; }
        [DataMember]
        public bool validLoc { get; set; }
        [DataMember]
        public string comment { get; set; }
        [DataMember]
        public string action { get; set; }
        [DataMember]
        public string workOrder { get; set; }
        [DataMember]
        public int roadService { get; set; }
        [DataMember]
        public int sidewalkService { get; set; }
    }

    [DataContract]
    public class CommentData
    {
        [DataMember]
        public string comment { get; set; }
        [DataMember]
        public DateTime sent { get; set; }
        [DataMember]
        public string user { get; set; }
        [DataMember]
        public string roadSegment { get; set; }
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

    [DataContract]
    public class TestData
    {
        [DataMember]
        public List<byte> Signature { get; set; }
    }

    [DataContract]
    public class BridgeInspectionData
    {
        public BridgeInspectionData()
        {
            this.bridgeItems = new List<BridgeItem>();
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public string bridge { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public string type { get; set; }
        [DataMember]
        public int spans { get; set; }
        [DataMember]
        public double length { get; set; }
        [DataMember]
        public double width { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string other { get; set; }
        [DataMember]
        public string reco { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string sigBase64 { get; set; }
        [DataMember]
        public List<BridgeItem> bridgeItems { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }
    }

    [DataContract]
    public class BridgeItem
    {
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public int issue { get; set; }
        [DataMember]
        public bool defect { get; set; }
        [DataMember]
        public string note { get; set; }
        [DataMember]
        public int tstate { get; set; }
        [DataMember]
        public bool groupEnabled { get; set; }
        [DataMember]
        public int severity { get; set; }
        [DataMember]
        public string bridge { get; set; }
    }

    [DataContract]
    public class PondInspectionData
    {
        public PondInspectionData()
        {
            this.pondItems = new List<PondItem>();
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string other { get; set; }
        [DataMember]
        public string reco { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string sigBase64 { get; set; }
        [DataMember]
        public List<PondItem> pondItems { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }
    }

    [DataContract]
    public class PondItem
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public int issue { get; set; }
        [DataMember]
        public bool defect { get; set; }
        [DataMember]
        public string note { get; set; }
        [DataMember]
        public int tstate { get; set; }
        [DataMember]
        public bool groupEnabled { get; set; }
    }

    [DataContract]
    public class RockInspectionData
    {

        public RockInspectionData()
        {
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string hwy { get; set; }
        [DataMember]
        public string area { get; set; }
        [DataMember]
        public int side { get; set; }
        [DataMember]
        public string patrol { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public string lhrs { get; set; }
        [DataMember]
        public string os { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string remarks { get; set; }
        [DataMember]
        public string conditions { get; set; }
        [DataMember]
        public string weather { get; set; }
        [DataMember]
        public string phone { get; set; }
        [DataMember]
        public int quantity { get; set; }
        [DataMember]
        public int block { get; set; }
        [DataMember]
        public int distribution { get; set; }
        [DataMember]
        public string vehicle { get; set; }
        [DataMember]
        public string injuries { get; set; }
        [DataMember]
        public string officer { get; set; }
        [DataMember]
        public string badge { get; set; }
        [DataMember]
        public string closure { get; set; }
        [DataMember]
        public string trimming { get; set; }
        [DataMember]
        public string removal { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }

    }

    [DataContract]
    public class BeaverInspectionData
    {

        public BeaverInspectionData()
        {
            this.beaverItems = new List<BeaverItem>();
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string other { get; set; }
        [DataMember]
        public string reco { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string sigBase64 { get; set; }
        [DataMember]
        public List<BeaverItem> beaverItems { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }

    }

    [DataContract]
    public class BeaverItem
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public int issue { get; set; }
        [DataMember]
        public bool defect { get; set; }
        [DataMember]
        public string note { get; set; }
        [DataMember]
        public int tstate { get; set; }
        [DataMember]
        public bool groupEnabled { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class FenceInspectionData
    {

        public FenceInspectionData()
        {
            this.fenceItems = new List<FenceItem>();
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string other { get; set; }
        [DataMember]
        public string reco { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string sigBase64 { get; set; }
        [DataMember]
        public List<FenceItem> fenceItems { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }

    }

    [DataContract]
    public class FenceItem
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public int issue { get; set; }
        [DataMember]
        public bool defect { get; set; }
        [DataMember]
        public string note { get; set; }
        [DataMember]
        public int tstate { get; set; }
        [DataMember]
        public bool groupEnabled { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class SignInspectionData
    {

        public SignInspectionData()
        {
            this.signItems = new List<SignItem>();
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string sigBase64 { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<SignItem> signItems { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }

    }

    [DataContract]
    public class SignItem
    {
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public int issue { get; set; }
        [DataMember]
        public bool defect { get; set; }
        [DataMember]
        public string note { get; set; }
        [DataMember]
        public int tstate { get; set; }
        [DataMember]
        public bool groupEnabled { get; set; }
        [DataMember]
        public int severity { get; set; }
        [DataMember]
        public string structure { get; set; }
    }

    [DataContract]
    public class Bridge
    {
        [DataMember]
        public string bridge { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public string type { get; set; }
        [DataMember]
        public int spans { get; set; }
        [DataMember]
        public double length { get; set; }
        [DataMember]
        public double width { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class Pond
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public string area { get; set; }
        [DataMember]
        public string size { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class Beaver
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class Fence
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string area { get; set; }
        [DataMember]
        public string length { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class Sign
    {
        [DataMember]
        public int type { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string spot { get; set; }
        [DataMember]
        public string site { get; set; }
        [DataMember]
        public string struct_type { get; set; }
        [DataMember]
        public string panels { get; set; }
        [DataMember]
        public string foot_type_l { get; set; }
        [DataMember]
        public string foot_type_r { get; set; }
        [DataMember]
        public int columns { get; set; }
        [DataMember]
        public int arms { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string size { get; set; }
        [DataMember]
        public string sign_text { get; set; }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public double distance { get; set; }
    }

    [DataContract]
    public class Culvert
    {
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string region { get; set; }
        [DataMember]
        public string patrol { get; set; }
        [DataMember]
        public int type { get; set; }
        [DataMember]
        public double diameter { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string notes { get; set; }
        [DataMember]
        public DateTime date { get; set; }
    }

    [DataContract]
    public class CulvertInspectionData
    {
        public CulvertInspectionData()
        {
            this.culvertItems = new List<CulvertItem>();
            this.photos = new List<PhotoData>();
        }
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public string road { get; set; }
        [DataMember]
        public string location { get; set; }
        [DataMember]
        public string inspector { get; set; }
        [DataMember]
        public string patrol { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string region { get; set; }
        [DataMember]
        public int type { get; set; }
        [DataMember]
        public double diameter { get; set; }
        [DataMember]
        public double latitude { get; set; }
        [DataMember]
        public double longitude { get; set; }
        [DataMember]
        public string other { get; set; }
        [DataMember]
        public List<short> sigData { get; set; }
        [DataMember]
        public string sigBase64 { get; set; }
        [DataMember]
        public List<CulvertItem> culvertItems { get; set; }
        [DataMember]
        public string repKey { get; set; }
        [DataMember]
        public List<PhotoData> photos { get; set; }
    }

    [DataContract]
    public class CulvertItem
    {
        [DataMember]
        public DateTime date { get; set; }
        [DataMember]
        public int issue { get; set; }
        [DataMember]
        public bool defect { get; set; }
        [DataMember]
        public string note { get; set; }
        [DataMember]
        public int tstate { get; set; }
        [DataMember]
        public bool groupEnabled { get; set; }
        [DataMember]
        public int severity { get; set; }
        [DataMember]
        public string structure { get; set; }
        [DataMember]
        public string location { get; set; }

    }

    [DataContract]
    public class WildlifeData
    {
        [DataMember]
        public string reportID { get; set; }
        [DataMember]
        public string address { get; set; }
        [DataMember]
        public string handcode { get; set; }
        [DataMember]
        public string unitnum { get; set; }
        [DataMember]
        public string username { get; set; }
        [DataMember]
        public DateTime reportTime { get; set; }
        [DataMember]
        public int direction { get; set; }
        [DataMember]
        public string town { get; set; }
        [DataMember]
        public string age { get; set; }
        [DataMember]
        public int gender { get; set; }
        [DataMember]
        public string species { get; set; }
        [DataMember]
        public int weather { get; set; }
        [DataMember]
        public int highway { get; set; }
        [DataMember]
        public int rowt { get; set; }
        [DataMember]
        public string comments { get; set; }
        [DataMember]
        public string log { get; set; }
        [DataMember]
        public decimal lon { get; set; }
        [DataMember]
        public decimal lat { get; set; }
        [DataMember]
        public List<PhotoData> photos;
    }

    [DataContract]
    public class ReportData
    {
        [DataMember]
        public string reportTime { get; set; }       // used for the unique Time code for linking photos etc..

        // user info
        [DataMember]
        public string username { get; set; }          // users login name
        [DataMember]
        public string repPrep { get; set; }           // Users name
        [DataMember]
        public string repTitle { get; set; }          // Users title
        [DataMember]
        public string repPhone { get; set; }          // Users phone
        [DataMember]
        public string equipment { get; set; }         // truck number
        [DataMember]
        public string phoneID { get; set; }           // Id of phone
        [DataMember]
        public byte[] Signiture { get; set; }         // stores the Signiture Array
        [DataMember]
        public string sigbase64 { get; set; }
        [DataMember]
        public List<short> Signature { get; set; }
        [DataMember]
        public bool Sent { get; set; }

        // location info
        [DataMember]
        public decimal CurrentLat { get; set; }
        [DataMember]
        public decimal CurrentLong { get; set; }
        [DataMember]
        public string hwy { get; set; }               // Stores hwy number 404 401 etc...
        [DataMember]
        public string locDetails { get; set; }        // stores the 
        [DataMember]
        public string locDirection { get; set; }      // east west etc..
        [DataMember]
        public string locIntersection { get; set; }

        [DataMember]
        public DateTime DateCol { get; set; }         // date and time of Collision
        [DataMember]
        public DateTime repDate { get; set; }         // date and time of arrival
        [DataMember]
        public DateTime clearDate { get; set; }       // Date and time of scene cleared

        // Descriptions and details
        [DataMember]
        public string Detour { get; set; }
        [DataMember]
        public string ColDesc { get; set; }
        [DataMember]
        public int Fatal { get; set; }
        [DataMember]
        public int injuries { get; set; }
        [DataMember]
        public string Physical { get; set; }
        [DataMember]
        public string Other { get; set; }
        [DataMember]
        public string Address { get; set; }

        // Chks for other documents
        [DataMember]
        public int chkOpp { get; set; }
        [DataMember]
        public int chkFatal { get; set; }
        [DataMember]
        public int chkOther { get; set; }
        [DataMember]
        public int chkSign { get; set; }
        [DataMember]
        public int chkDmg { get; set; }

        // Weather Data
        [DataMember]
        public List<DateTime> fromTime { get; set; }
        [DataMember]
        public List<int> percipitationType { get; set; }
        [DataMember]
        public List<int> percipitationLvl { get; set; }
        [DataMember]
        public List<int> windStrength { get; set; }
        [DataMember]
        public List<int> windDirection { get; set; }
        [DataMember]
        public List<int> snowAccum { get; set; }
        [DataMember]
        public List<int> swsnowAccum { get; set; }
        [DataMember]
        public List<int> visibility { get; set; }
        [DataMember]
        public List<int> cloudCondition { get; set; }
        [DataMember]
        public List<int> fogLvl { get; set; }
        [DataMember]
        public List<int> drifting { get; set; }
        [DataMember]
        public List<int> roadCondition { get; set; }
        [DataMember]
        public double Temp { get; set; }

        // Update
        [DataMember]
        public List<DateTime> updateTime { get; set; }
        [DataMember]
        public List<string> upDesc { get; set; }

        // People On scene
        [DataMember]
        public List<string> title { get; set; }
        [DataMember]
        public List<string> name { get; set; }
        [DataMember]
        public List<string> equip { get; set; }

        // internal use for the App only
        [DataMember]
        public int HwyNdx { get; set; }
        [DataMember]
        public int HwyDetNdx { get; set; }
        [DataMember]
        public int HwyDirNdx { get; set; }
        [DataMember]
        public int chkWeather { get; set; }
        [DataMember]
        public int chkSig { get; set; }
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

    [DataContract]
    public class InstalledPictures
    {
        [DataMember]
        public double lat { get; set; }
        [DataMember]
        public double lon { get; set; }
        [DataMember]
        public string username { get; set; }
        [DataMember]
        public string objKey { get; set; }
        [DataMember]
        public string unitNum { get; set; }
        [DataMember]
        public int handcode { get; set; }
        [DataMember]
        public string imgUrl { get; set; }
        [DataMember]
        public string InstallKey { get; set; }
        [DataMember]
        public DateTime pictureSent { get; set; }
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

    [DataContract]
    public class PhotoData
    {
        [DataMember]
        public bool dash;
        [DataMember]
        public bool collision;
        [DataMember]
        public bool done;
        [DataMember]
        public bool ready;
        [DataMember]
        public int handcode;
        [DataMember]
        public int phandcode;
        [DataMember]
        public string unitnum;
        [DataMember]
        public int evtType;
        [DataMember]
        public decimal latitude;
        [DataMember]
        public decimal longitude;
        [DataMember]
        public int degrees;
        [DataMember]
        public int speed;
        [DataMember]
        public string repkey;
        [DataMember]
        public int issue;
        [DataMember]
        public string username;
        [DataMember]
        public string imgUrl;
        [DataMember]
        public string custCode;
        [DataMember]
        public string objKey;
        [DataMember]
        public string bucket;
        [DataMember]
        public DateTime rdate;
        [DataMember]
        public decimal gpsAccuracy;
        [DataMember]
        public bool priv;
        [DataMember]
        public bool intall;
        [DataMember]
        public string installKey;
        [DataMember]
        public DateTime imgcreated;
        [DataMember]
        public bool sent;
        [DataMember]
        public bool sidewalk;
        [DataMember]
        public bool deficiency;
        [DataMember]
        public string imgPath;
        [DataMember]
        public bool photoSent;
        [DataMember]
        public DateTime sentTime;
        [DataMember]
        public string NetworkType;
        [DataMember]
        public int SignalStr;
        [DataMember]
        public int qCount;
        [DataMember]
        public int picQCount;
        [DataMember]
        public int eventQCount;
        [DataMember]
        public string imei;
        [DataMember]
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
    public class DVIRReport
    {
        [DataMember]
        public int Id { get; set; }     // id from table
        [DataMember]
        public int CommId { get; set; }         // Id from CommResponse of PhoneLoginJson
        [DataMember]
        public string Company { get; set; }
        [DataMember]
        public int TractorId { get; set; }      // from vehicles table
        [DataMember]
        public string TractorPlate { get; set; }
        [DataMember]
        public double Odom { get; set; }
        [DataMember]
        public DateTime rdate { get; set; }
        [DataMember]
        public double lat { get; set; }
        [DataMember]
        public double lon { get; set; }
        [DataMember]
        public string Inspector { get; set; }
        [DataMember]
        public string Comments { get; set; }
        [DataMember]
        public List<short> Signature { get; set; }
        [DataMember]
        public string RepKey { get; set; }
        [DataMember]
        public List<int> DefectIds { get; set; }
        [DataMember]
        public bool done { get; set; }
    }

    [DataContract]
    public class DVIRDefectType
    {
        [DataMember]
        public int id;

        [DataMember]
        public string referenceId;

        [DataMember]
        public string description;

        [DataMember]
        public bool isMajor;

        [DataMember]
        public bool isMinor;

        [DataMember]
        public int sectionId;
    }

    [DataContract]
    public class DVIRDefectSection
    {
        [DataMember]
        public int id;

        [DataMember]
        public string referenceId;

        [DataMember]
        public string description;

        [DataMember]
        public DVIRDefectType[] defectTypes;
    }

    // DEPRECATED
    // DVIRHeader and DVIRDetail are deprecated and will soon be removed. Use DVIRReport instead
    [DataContract]
    public class DVIRHeader
    {
        [DataMember]
        public int CommId { get; set; }         // Id from CommResponse of PhoneLoginJson
        [DataMember]
        public string Company { get; set; }
        [DataMember]
        public int TractorId { get; set; }      // from vehicles table
        [DataMember]
        public string TractorPlate { get; set; }
        [DataMember]
        public double Odom { get; set; }
        [DataMember]
        public DateTime rdate { get; set; }
        [DataMember]
        public double lat { get; set; }
        [DataMember]
        public double lon { get; set; }
        [DataMember]
        public string Inspector { get; set; }
        [DataMember]
        public string Comments { get; set; }
        [DataMember]
        public List<short> Signature { get; set; }
        [DataMember]
        public string RepKey { get; set; }
        [DataMember]
        public DVIRDetail[] Details { get; set; }
        [DataMember]
        public bool done { get; set; }
    }

    [DataContract]
    public class InspDetails
    {
        [DataMember]
        public int Id { get; set; }
        [DataMember]
        public int Type { get; set; }
        [DataMember]
        public int Sequence { get; set; }
        [DataMember]
        public string Section { get; set; }
        [DataMember]
        public string Category { get; set; }
        [DataMember]
        public string Issue { get; set; }
        [DataMember]
        public int Display { get; set; }
        [DataMember]
        public string PdfField { get; set; }
    }

    [DataContract]
    public class DVIRDetail
    {
        [DataMember]
        public int CategoryId { get; set; }
        [DataMember]
        public int[] MinorDefects { get; set; }
        [DataMember]
        public int[] MajorDefects { get; set; }
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
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public bool HHFlag { get; set; }
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
    public class ImgInfo
    {
        [DataMember]
        public DateTime rdate { get; set; }
        [DataMember]
        public int EvtType { get; set; }
        [DataMember]
        public string ImgUrl { get; set; }
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
        [DataMember]
        public double Pdop { get; set; }
        [DataMember]
        public double Hdop { get; set; }
        [DataMember]
        public ImgInfo[] Images { get; set; }
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
        public string ACSnowAccum;
        public string ACSwSnowAccum;


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
