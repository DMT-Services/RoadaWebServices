using System;
using MapPoint;

/// <summary>
/// Summary description for MapP
/// </summary>
public class MapP
{
    private enum mConvert { getTimeZone, getAddress };

    private MapPoint.Application mApp = null;

    public MapP()
    {
        InitializeMap();
    }

    private void InitializeMap()
    {
        // make sure any previous instance is cleared first...
        Close();

        try
        {
            mApp = new MapPoint.ApplicationClass();
            mApp.Units = MapPoint.GeoUnits.geoKm;
        }
        catch { }
    }

    public void Close()
    {
        if (mApp != null)
        {
            try
            {
                if (mApp.ActiveMap != null)
                    mApp.ActiveMap.Saved = true;    // keeps it from popping up some message...
                mApp.Quit();                        // this actually releases the MapPoint.exe
            }
            finally
            {
                mApp = null;
            }
        }
    }

    public string LLToTimeZone(double Latitude, double Longitude)
    {
        string s = "";

        MapPoint.Location rc = LLToLoc(Latitude, Longitude, mConvert.getTimeZone);
        if (rc != null)
        {
            s = rc.Name;
            rc = null;
        }
        return s;
    }

    // loops around the Lat/Long passed in a spiral pattern, trying to get valid data
    private MapPoint.Location LLToLoc(double Latitude, double Longitude, mConvert cMode)
    {
        MapPoint.Location Result = null;

        double Lat = Latitude;
        double Long = Longitude;
        int mx = 0;
        if (Lat != 0.0 || Long != 0.0)
        {
            double theta = 0;
            double radius = 0.00003;
            // 'Approximately' corrects for latitude changing the value of longitude
            double longMultiplier = 1 / Math.Cos(Lat * Math.PI / 180);

            bool first = true;
            bool found = false;

            MapPoint.Location Loc = null;
            MapPoint.FindResults FindR = null;
            while (!found)
            {
                if (mx++ > 50)  // can't go on trying to find it forever!?
                    break;

                try
                {
                    Loc = mApp.ActiveMap.GetLocation(Lat, Long, 12.5);  // changing the 12.5 (altitude) seems to really make a difference...
                }
                catch
                {
                    Loc = null;
                }
                if (Loc != null)
                {
                    if (first)
                    {
                        Loc.GoTo();
                        first = false;
                    }

                    try
                    {
                        FindR = mApp.ActiveMap.ObjectsFromPoint(mApp.ActiveMap.LocationToX(Loc), mApp.ActiveMap.LocationToY(Loc));
                    }
                    catch
                    {
                        FindR = null;
                    }
                    if (FindR != null)
                    {
                        for (int i = 0; i < FindR.Count; i++)
                        {
                            object ndx = i + 1;
                            try
                            {
                                Result = FindR.get_Item(ref ndx) as Location;
                            }
                            catch
                            {
                                Result = null;
                            }
                            if (Result != null)
                            {
                                switch (cMode)
                                {
                                    case mConvert.getTimeZone:
                                        found = Result.Name.Contains("GMT");
                                        break;

                                    case mConvert.getAddress:
                                        found = (Result.StreetAddress != null);
                                        break;
                                }
                                if (found)
                                    break;
                                Result = null;
                            }
                        }
                        FindR = null;
                    }
                    Loc = null;
                }
                if (!found)
                {
                    // Increment the radius;
                    radius = radius + 0.00001;
                    // Increment the angle
                    theta = theta + 0.19634954084936207740625;   // (PI / 16)

                    // Increase the Long/Lats...
                    Long = Long + (longMultiplier * (Math.Cos(theta) * radius));
                    Lat = Lat + Math.Sin(theta) * radius;
                }
            }
        }
        return Result;
    }
}
