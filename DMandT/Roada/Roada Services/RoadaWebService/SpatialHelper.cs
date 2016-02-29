using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using DMTServices;

namespace DMTServices
{
    public class SpatialHelper
    {
        private SqlConn conS = null;
        private SqlStmt stmt = null;
        private MessLog messLog;

        public string StreetName { get; set; }
        public string City { get; set; }
        public int FirstHouseNumber { get; set; }
        public int LastHouseNumber { get; set; }
        public int Direction { get; set; }
        public bool Toll { get; set; }
        public string ExitNum { get; set; }
        public int RoadClass { get; set; }
        public string FullAddress { get; set; }

        public SpatialHelper(string spatialConn, MessLog log)
        {
            conS = new SqlConn(spatialConn);
            messLog = log;
            conS.Mess = log;

            if (! conS.dbConnect())
                log.Flush("Unable to connect to the Spatial Database.");
        }

        private bool FindSpatialMatch(SpatData spat)
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
                messLog.Flush("FindSpatialMatch() Excp: ", ex.Message);
            }
            finally
            {
                stmt.Close();
            }

            return i > 0;
        }

        public SqlHelper GetRoadNetSql(SpatData spat)
        {
            bool found;

            return GetRoadNetDataEx(spat, true, out found);
        }

        public bool GetRoadNet(SpatData spat)
        {
            bool found;

            GetRoadNetDataEx(spat, true, out found);

            return found;
        }

        public SqlHelper GetRoadNetDataEx(SpatData spat, bool retHelper, out bool found)
        {
            SqlHelper sql = new SqlHelper();

            found = false;

            try
            {
                stmt = new SqlStmt(conS);
                found = FindSpatialMatch(spat);

                StreetName = string.Empty;
                City = string.Empty;
                FirstHouseNumber = 0;
                LastHouseNumber = 0;
                Direction = 0;
                Toll = false;
                ExitNum = string.Empty;
                RoadClass = 0;

                if (found)
                {
                    found = false;
                    // sometimes there is no address info for a give object, so try all 3
                    int ndx;
                    for (ndx = 0; ndx < spat.ObjIds.Length; ndx++)
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
                                    StreetName = streetName;
                                    City = stmt.fetch("standard_municipality");
                                    if (retHelper)
                                    {
                                        sql.Add("street", streetName);
                                        sql.Add("city", City);
                                    }
                                    if (stmt.fetch("HOUSE_NUMBER_STRUCTURE").ToUpper() != "NONE")
                                    {
                                        FirstHouseNumber = stmt.ifetch("FIRST_HOUSE_NUMBER");
                                        LastHouseNumber = stmt.ifetch("LAST_HOUSE_NUMBER");
                                        if (retHelper)
                                        {
                                            sql.Add("first_house_number", FirstHouseNumber);
                                            sql.Add("last_house_number", LastHouseNumber);
                                        }
                                    }

                                    FullAddress = string.Empty;
                                    int house = 0;
                                    if (FirstHouseNumber != 0 && LastHouseNumber != 0)
                                        house = (LastHouseNumber - FirstHouseNumber) + FirstHouseNumber;
                                    else
                                        if (FirstHouseNumber != 0)
                                            house = FirstHouseNumber;
                                        else
                                            if (LastHouseNumber != 0)
                                                house = LastHouseNumber;
                                    if (house != 0)
                                        FullAddress += string.Format("{0} ");
                                    FullAddress += string.Format("{0}, {1}", StreetName, City);
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
                    if (ndx < spat.ObjIds.Length)
                    {
                        //if (!sql.AnyQuery())
                        //    Global.messLog.Flush("Nothing found - " + spat.Latitude.ToString() + " , " + spat.Longitude.ToString());

                        // look for direction, junctions and length
                        if (stmt.DoSQLFormat("SELECT * FROM orn_road_net_element e WHERE e.FMF_OBJECT_ID = {0}", spat.ObjIds[ndx]))
                        {
                            bool inCase = true;
                            switch (stmt.fetch("direction_of_traffic_flow").ToUpper())
                            {
                                case "BOTH":
                                    Direction = 0;
                                    break;
                                case "NEGATIVE":
                                    Direction = -1;
                                    break;
                                case "POSITIVE":
                                    Direction = 1;
                                    break;
                                default:
                                    inCase = false;
                                    break;
                            }
                            if (inCase && retHelper)
                                sql.Add("direction", Direction);

                            inCase = true;
                            switch (stmt.fetch("toll_road_ind").ToUpper())
                            {
                                case "Yes":
                                    Toll = true;
                                    break;
                                case "NEGATIVE":
                                    Toll = false;
                                    break;
                                default:
                                    inCase = false;
                                    break;
                            }
                            if (inCase && retHelper)
                                sql.Add("toll", Toll);
                            ExitNum = stmt.fetch("exit_number");
                            if (retHelper)
                                sql.Add("exit_num", ExitNum);
                            // sql.Add("junction_fr", stmt.fetch("from_junction_id"));
                            // sql.Add("junction_to", stmt.fetch("to_junction_id"));
                            // sql.Add("length", stmt.dfetch("length"));
                        }

                        // look for road class info
                        if (stmt.DoSQLFormat("SELECT MIN(v.nrn_roadclass) AS roadclass FROM orn_road_class c LEFT JOIN orn_road_class_list v ON c.ROAD_CLASS=v.ROAD_CLASS WHERE c.ROAD_NET_ELEMENT_ID={0}", spat.ObjIds[ndx]))
                        {
                            RoadClass = stmt.ifetch("roadclass");
                            if (RoadClass > 0 && retHelper)
                                sql.Add("road_class", RoadClass);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                messLog.Flush("GetRoadNetDataEx() Excp: ", ex.Message);
            }
            finally
            {
                if (stmt != null)
                {
                    stmt.Close();
                    stmt = null;
                }
            }
            return sql;
        }
    }

    public class SpatData
    {
        public DateTime Date;
        public int Id;
        public long SpatialId;
        public double Latitude;
        public double Longitude;
        public bool Done;
        public long[] ObjIds = new long[3];
        public double[] Dist = new double[3];
        public int Value;
        public string Name;

        public SpatData(DateTime d, int id, double lat, double lon)
        {
            Date = d;
            Id = id;
            Latitude = lat;
            Longitude = lon;
            Done = false;
        }
    }

}