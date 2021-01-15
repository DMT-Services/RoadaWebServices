using System;
using System.Collections;
using System.Collections.Generic;
#if USE_MAP
using Simplovation.Web.Maps.VE;
#endif

namespace DMTServices
{
#if !USE_MAP && !PocketPC
    public class LatLong
    {
        /// <summary>
        /// Initializes a new instance of the LatLong object.
        /// </summary>
        /// <overloads>Initializes a new instance of the LatLong object.</overloads>
        public LatLong() { }

        /// <summary>
        /// Initializes a new instance of the LatLong object.
        /// </summary>
        /// <param name="latitude">Specifies the latitude of a single point on the globe.</param>
        /// <param name="longitude">Specifies the longitude of a single point on the globe.</param>
        public LatLong(double latitude, double longitude)
        {
            this.Latitude = latitude;
            this.Longitude = longitude;
        }

        /// <summary>
        /// Returns a String representation of the LatLong object.
        /// </summary>
        /// <returns>A string containing only the Latitude and Longitude of the LatLong object seperated by a comma and space (Example: "75.55, -75.55")</returns>
        public new string ToString()
        {
            return this.Latitude.ToString() + ", " + this.Longitude.ToString();
        }

        /// <summary>
        /// Specifies the latitude of a single point on the globe.
        /// </summary>
        public double? Latitude { get; set; }

        /// <summary>
        /// Specifies the longitude of a single point on the globe.
        /// </summary>
        public double? Longitude { get; set; }
    }
#endif

    /// <summary>
    /// Class for manipulating Polygon shapes
    /// </summary>
    public class InPoly
    {
        private List<LatLong> points = new List<LatLong>();

        private bool _knowClockWise = false;
        private bool _isClockWise;

        public InPoly()
        {
        }

        public InPoly(List<LatLong> pPoints)
        {
            points.AddRange(pPoints);
            _knowClockWise = false;
        }

        public InPoly(double lat, double lon)
        {
            AddPoint(lat, lon);
        }

        /// <summary>
        /// create the class with the points passed in as string
        /// each long/lat is separated by ; and each of those is separated by ,
        /// </summary>
        /// <param name="locations"></param>
        public InPoly(string locations)
        {
            Add(locations);
            if (points.Count > 2)
                // add the starting point in as the final point to close the 'circle'...
                if (points[0].Latitude != points[points.Count - 1].Latitude|| points[0].Longitude != points[points.Count - 1].Longitude)
                    AddPoint(points[0]);
            _knowClockWise = false;
        }

        public void Clear()
        {
            points.Clear();
            _knowClockWise = false;
        }

        public int Count
        {
            get { return points.Count; }
        }

        public int Points()
        {
            return points.Count;
        }

        public List<LatLong> thePoints
        {
            get { return points; }
        }

        public void AddPoint(LatLong l)
        {
            points.Add(l);
            _knowClockWise = false;
        }

        public void AddPoint(double lat, double lon)
        {
            if (lat != 0.0 && lon != 0.0)
                AddPoint(new LatLong(lat, lon));
        }

        public void AddPoint(string csLatLong)
        {
            AddPoint(csLatLong, ',');
        }

        // add a point passed as a string; "lat,long"...
        public void AddPoint(string csLatLong, char XYSep)
        {
#if PocketPC
            string[] slatlong = csLatLong.Split(new char[] { XYSep });
#else
            string[] slatlong = csLatLong.Split(new char[] { XYSep }, StringSplitOptions.RemoveEmptyEntries);
#endif
            if (slatlong.Length > 1)
                AddPoint(myToDouble(slatlong[0]), myToDouble(slatlong[1]));
        }

        public void Add(string locations)
        {
            Add(locations, ';', ',');
        }

        public void Add(string locations, char pointSep)
        {
            Add(locations, pointSep, ',');
        }

        public void Add(string locations, char pointSep, char XYSep)
        {
#if PocketPC
            string[] slatlongs = locations.Split(new char[] { pointSep });
#else
            string[] slatlongs = locations.Split(new char[] { pointSep }, StringSplitOptions.RemoveEmptyEntries);
#endif
            if (slatlongs.Length > 0)
            {
                for (int i = 0; i < slatlongs.Length; i++)
                    AddPoint(slatlongs[i], XYSep);
            }
        }

        public void AddPoints(List<LatLong> pPoints)
        {
            points.AddRange(pPoints);
            _knowClockWise = false;
        }

        public bool isClockWise
        {
            get { return getClockwise(); }
        }

        private bool getClockwise()
        {
            if (!_knowClockWise)
            {
                _isClockWise = CalculateArea() >= 0;
                _knowClockWise = true;
            }
            return _isClockWise;
        }

        private double CalculateArea()
        {
            double area = 0.0;
            if (points.Count > 2)
            {
                for (int i = 0; i < points.Count - 1; i++)
                    area += ((points[i + 1].Latitude ?? 0) - (points[i].Latitude ?? 0)) * ((points[i + 1].Longitude ?? 0) + (points[i].Longitude ?? 0));
                area += ((points[0].Latitude ?? 0) - (points[points.Count - 1].Latitude ?? 0)) * ((points[0].Longitude ?? 0) + (points[points.Count - 1].Longitude ?? 0));
            }
            // positive is clockwise
            return area;
        }

        public void reversePoints()
        {
            List<LatLong> nPoints = new List<LatLong>(points.Count);
            for (int i = points.Count - 1; i >= 0; i--)
                nPoints.Add(points[i]);
            points.Clear();
            points.AddRange(nPoints);

            // set the clockwise flag to new value
            if (_knowClockWise)
                _isClockWise = !_isClockWise;
        }

        /// <summary>
        /// returns the approximate center of the polygon
        /// </summary>
        /// <returns></returns>
        public LatLong GetCentroid()
        {
            LatLong rc = new LatLong(0.0, 0.0);

            if (points.Count > 2)
            {
                double sumY = 0.0;
                double sumX = 0.0;
                double sum = 0.0;

                // 'close' the polygon, if it's not already
                if (points[0].Latitude != points[points.Count - 1].Latitude || points[0].Longitude != points[points.Count - 1].Longitude)
                    AddPoint(points[0]);

                var n = points.Count;
                for (int i = 0; i < n - 1; i++)
                {
                    double partialSum = (points[i].Longitude ?? 0.0) * (points[i + 1].Latitude ?? 0.0) - (points[i + 1].Longitude ?? 0.0) * (points[i].Latitude ?? 0.0);
                    sum += partialSum;
                    sumX += ((points[i].Longitude ?? 0.0) + (points[i + 1].Longitude ?? 0.0)) * partialSum;
                    sumY += ((points[i].Latitude ?? 0.0) + (points[i + 1].Latitude ?? 0.0)) * partialSum;
                }

                double area = 0.5 * sum;
                rc = new LatLong(sumY / 6 / area, sumX / 6 / area);
            }
            else
                if (points.Count > 0)
                    rc = points[0];
            return rc;
        }

        public bool pointInPolygon(double lat, double lon)
        {
            return pointInPolygon(new LatLong(lat, lon));
#if OLD
            bool oddNodes = false;
            if (y != 0 && x != 0)
            {
                int i, j = polyY.Count - 1;
                for (i = 0; i < polyY.Count; i++)
                {
                    if (polyX[i] < x && polyX[j] >= x || polyX[j] < x && polyX[i] >= x)
                        if (polyY[i] + (x - polyX[i]) / (polyX[j] - polyX[i]) * (polyY[j] - polyY[i]) < y)
                            oddNodes = !oddNodes;
                    j = i;
                }
            }
            return oddNodes;
#endif
        }

        public bool pointInPolygon(LatLong l)
        {
            bool oddNodes = false;

            int i, j = points.Count - 1;
            for (i = 0; i < points.Count; i++)
            {
                if (points[i].Latitude < l.Latitude && points[j].Latitude >= l.Latitude || points[j].Latitude < l.Latitude && points[i].Latitude >= l.Latitude)
                    if (points[i].Longitude + (l.Latitude - points[i].Latitude) / (points[j].Latitude - points[i].Latitude) * (points[j].Longitude - points[i].Longitude) < l.Longitude)
                        oddNodes = !oddNodes;
                j = i;
            }
            return oddNodes;
        }

        /// <summary>
        /// speeded up version of above, only check the 1st point in polygon, if > dist (in decimal degrees) then abort
        /// NOTE! DO NOT USE for large polygons, won't work!
        /// </summary>
        /// <param name="lat"></param>
        /// <param name="lon"></param>
        /// <param name="dist"></param>
        /// <returns></returns>
        public bool pointInPolygonDist(double lat, double lon, double dist)
        {
            bool Result = false;
            if (lat != 0.0 && lon != 0.0 && points.Count > 0)
            {
                if (Math.Abs((points[0].Longitude ?? 0.0) - lon) < dist && Math.Abs((points[0].Latitude ?? 0.0) - lat) < dist)
                    Result = pointInPolygon(lat, lon);
            }
            return Result;
        }

        /// <summary>
        /// check each point in this poly is inside the passed in InPoly
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public bool isInside(InPoly p)
        {
            bool rc = false;
            if (points.Count > 0)
            {
                rc = true;
                foreach (LatLong l in points)
                    if (!p.pointInPolygon(l))
                    {
                        rc = false;
                        break;
                    }
            }
#if OLD
            for (int i = 0; i < polyY.Count; i++)
            {
                if (!p.pointInPolygon(polyY[i], polyX[i]))
                {
                    rc = false;
                    break;
                }
            }
#endif
            return rc;
        }

        public double closestPoint(double lat, double lon)
        {
            double rc = double.MaxValue;
            if (lat != 0 && lon != 0)
                for (int i = 0; i < points.Count; i++)
                {
                    double d = globe.distance(lat, lon, points[i].Latitude ?? 0.0, points[i].Longitude ?? 0.0);
                    if (d < rc)
                        rc = d;
                }
            return rc;
        }

        /// <summary>
        /// return the point at the given index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public LatLong Point(int index)
        {
            LatLong pt = new LatLong();
            if (index < points.Count)
                pt = points[index];
            return pt;
        }

        /// <summary>
        /// Needed this to get rid of the myConvert dependency for ShawMobile
        /// </summary>
        /// <param name="s">string to convert to a double</param>
        /// <returns>s converted to double else zero, catches exception</returns>
        private double myToDouble(string s)
        {
            return myToDouble(s, 0.0);
        }

        private double myToDouble(object o, double def)
        {
            try
            {
                if (o == null || o is System.DBNull)
                    return def;
                else
                {
                    if (o is string || o is char)
                    {
                        double d;
                        if (!double.TryParse((o is char) ? ((char)o).ToString() : (string)o, out d))
                            d = def;
                        return d;
                    }
                    else
                        if (o is DateTime)
                            return ((DateTime)o).Ticks / 10000;     // milliseconds since DateTime.MinValue
                        else
                            return Convert.ToDouble(o);
                }
            }
            catch
            {
                return def;
            }
        }
    }

    public static class polyFix
    {
        public static List<List<LatLong>> fixRotation(List<List<LatLong>> polygon)
        {
            List<List<LatLong>> rc = new List<List<LatLong>>();
            if (polygon.Count > 1)
            {
                // first get each ring as InPoly()
                List<InPoly> polys = new List<InPoly>();
                foreach (List<LatLong> ll in polygon)
                    polys.Add(new InPoly(ll));

                Dictionary<int, List<int>> family = new Dictionary<int, List<int>>();

                List<int> added = new List<int>();
                for (int i = 0; i < polys.Count; i++)
                    for (int j = 0; j < polys.Count; j++)
                        if (j != i) // don't check yourself against yourself!
                        {
                            // if this polygon is inside the other, make it's clockwise direction the opposite
                            if (polys[j].isInside(polys[i]))
                            {
                                if (!added.Contains(j))
                                {
                                    added.Add(j);
                                    if (!added.Contains(i))
                                    {
                                        added.Add(i);
                                        family.Add(i, new List<int>());
                                        if (polys[i].isClockWise)           // parent should be counter clockwise
                                            polys[i].reversePoints();
                                    }

                                    family[i].Add(j);
                                    if (!polys[j].isClockWise)          // children should all be clockwise
                                        polys[j].reversePoints();
                                }

                            }
                        }

                added.Clear();
                foreach (KeyValuePair<int, List<int>> p in family)
                {
                    added.Add(p.Key);
                    rc.Add(polys[p.Key].thePoints);   // add the 'parent'
                    foreach (int i in p.Value)
                    {
                        added.Add(i);
                        rc.Add(polys[i].thePoints);   // and all the children
                    }
                }

                for (int i = 0; i < polys.Count; i++)
                    if (!added.Contains(i))
                        rc.Add(polys[i].thePoints);
            }
            else
                if (polygon.Count == 1)
                    rc.Add(polygon[0]);
            return rc;
        }
    }

    // this is a slightly more accurate way to check if one polygon is inside the other
    // main difference is that points that lie right on the border of the 'parent' will 
    // still be considered inside...
    class PolyCheck
    {
        private class Point
        {
            public double lat;
            public double lon;

            public Point()
            {
            }

            public Point(double plat, double plon)
            {
                lat = plat;
                lon = plon;
            }
        }

        private List<Point> polyPoints = new List<Point>();

        public PolyCheck(List<LatLong> points)
        {
            foreach (LatLong ll in points)
                AddPoint(ll.Latitude ?? 0.0, ll.Longitude ?? 0.0);
        }

        public void AddPoint(double lat, double lon)
        {
            if (lat != 0.0 && lon != 0.0)
                polyPoints.Add(new Point(lat, lon));
        }

        public bool isInside(PolyCheck p)
        {
            bool rc = true;

            int no = 0;
            // seems like sometimes we can get a point outside of the parent but it actually is wholly inside it?
            int mx = (int)Math.Round(polyPoints.Count / 100.0);
            if (mx == 0)
                mx = 1;
            mx = Math.Min(mx, 5);

            for (int i = 0; i < polyPoints.Count; i++)
            {
                if (!p.pointInPolygon(polyPoints[i]))
                {
                    if (++no > mx)
                    {
                        rc = false;
                        break;
                    }
                }
            }
            return rc;
        }

        private bool pointInPolygon(Point p)
        {
            // check if the point is a vertex
            if (polyPoints.Contains(p))
                return true;

            // check if point is on a boundary
            int n = polyPoints.Count;
            for (int i = 0; i < n; i++)
            {
                Point p1;
                Point p2;
                if (i == 0)
                {
                    p1 = polyPoints[0];
                    p2 = polyPoints[1];
                }
                else
                {
                    p1 = polyPoints[i - 1];
                    p2 = polyPoints[i];
                }
                //if (p1.y == p2.y && p1.y == p.y && p.x > Math.Min(p1.x, p2.x) && p.x < Math.Max(p1.x, p2.x))
                //    return true;
                //if (p1.x == p2.x && p1.x == p.x && p.y > Math.Min(p1.y, p2.y) && p.y < Math.Max(p1.y, p2.y))
                //    return true;
                if (p.lat <= Math.Max(p1.lat, p2.lat) && p.lat >= Math.Min(p1.lat, p2.lat) && p.lon <= Math.Max(p1.lon, p2.lon) && p.lon >= Math.Min(p1.lon, p2.lon))
                    return true;
            }

            bool inside = false;

            double p1lat = polyPoints[0].lat;
            double p1lon = polyPoints[0].lon;
            for (int i = 0; i <= n; i++)
            {
                double p2lat = polyPoints[i % n].lat;
                double p2lon = polyPoints[i % n].lon;
                if (p.lon > Math.Min(p1lon, p2lon))
                {
                    if (p.lon <= Math.Max(p1lon, p2lon))
                        if (p.lat <= Math.Max(p1lat, p2lat))
                        {
                            double xints = 0.0;
                            if (p1lon != p2lon)
                                xints = (p.lon - p1lon) * (p2lat - p1lat) / (p2lon - p1lon) + p1lat;
                            if (p1lat == p2lat || p.lat <= xints)
                                inside = !inside;
                        }
                }
                p1lat = p2lat;
                p1lon = p2lon;
            }
            return inside;
        }
    }

    public static class globe
    {
        /// <summary>
        /// return the distance (in meters) between two given points
        /// </summary>
        /// <param name="lat1"></param>
        /// <param name="lon1"></param>
        /// <param name="lat2"></param>
        /// <param name="lon2"></param>
        /// <returns></returns>
        public static double distance(double lat1, double lon1, double lat2, double lon2)
        {
            double dist = Math.Sin(deg2rad(lat1)) * Math.Sin(deg2rad(lat2)) + Math.Cos(deg2rad(lat1)) * Math.Cos(deg2rad(lat2)) * Math.Cos(deg2rad(lon1 - lon2));
            return rad2deg(Math.Acos(dist)) * 111194.93;     // in meters...  (if you rad2deg(111194.93) you get 6371000.2 ~ earth's radius in meters)
        }

        /// <summary>
        /// Converts degrees to radians
        /// </summary>
        /// <param name="deg">Degrees</param>
        /// <returns>Radians</returns>
        public static double deg2rad(double deg)
        {
            return (deg * Math.PI / 180.0);
        }

        /// <summary>
        /// Converts Radians to Degrees
        /// </summary>
        /// <param name="rad">Degrees</param>
        /// <returns>Radians</returns>
        public static double rad2deg(double rad)
        {
            return (rad * 180.0 / Math.PI);
        }
    }
}
