using GeoDataPlugin.Models;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeoDataPlugin.Utils
{
    /// <summary>
    /// Provides static methods for converting between geographic coordinates and local Cartesian coordinates,
    /// calculating distances, and creating geographic bounding boxes.
    /// </summary>
    /// <remarks>The GeoConverter class is intended for use with small geographic areas where the effects of
    /// Earth's curvature can be approximated using simple projections. All methods assume coordinates are specified in
    /// decimal degrees and distances in meters. The class is thread-safe as it contains only static methods and does
    /// not maintain any internal state.</remarks>
    public class GeoConverter
    {
        // average radius of Earth in meters
        private const double EarthRadius = 6371000;

        // GeoToLocal Converts lat/lon to local XY coordinates using a reference origin 
        public static Point3d GeoToLocal(double lat, double lon, double originLat, double originLon)
        {
            // Simple equirectangular projection (good for small areas)
            double x = (lon - originLon) * Math.Cos(originLat * Math.PI / 180.0) * EarthRadius * Math.PI / 180.0;
            double y = (lat - originLat) * EarthRadius * Math.PI / 180.0;

            return new Point3d(x, y, 0);
        }

        // Haversine distance between two points in meters
        // returns a real-world distance in meters from given lat/lon of 2 points
        public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return EarthRadius * c;
        }
        // Create a bounding box around a center point with radius in meters
        // used by api/s to get data within a rectangle
        public static GeoBoundingBox CreateBBox(double centerLat, double centerLon, double radiusMeters)
        {
            double latDelta = (radiusMeters / EarthRadius) * (180.0 / Math.PI);
            double lonDelta = (radiusMeters / (EarthRadius * Math.Cos(centerLat * Math.PI / 180.0))) * (180.0 / Math.PI);

            return new GeoBoundingBox(
                centerLat - latDelta,
                centerLon - lonDelta,
                centerLat + latDelta,
                centerLon + lonDelta
            );
        }
    }
}
