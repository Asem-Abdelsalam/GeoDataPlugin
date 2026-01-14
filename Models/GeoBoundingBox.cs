using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeoDataPlugin.Models
{
    /// <summary>
    /// Represents a rectangular geographic area defined by its southern, western, northern, and eastern boundaries in
    /// latitude and longitude coordinates.
    /// </summary>
    /// <remarks>Use this class to specify or query a bounding box for geographic operations, such as spatial
    /// searches or map rendering. The boundaries are expressed in WGS84 coordinates. The class provides properties to
    /// access the center point of the box and methods to estimate its width and height in meters.</remarks>
    public partial class GeoBoundingBox
    {
        //coordinates, in latitude & longitude
        public double South { get; set; }
        public double West { get; set; }
        public double North { get; set; }
        public double East { get; set; }

        public GeoBoundingBox(double south, double west, double north, double east)
        {
            South = south;
            West = west;
            North = north;
            East = east;
        }
        // CenterLat & CenterLon 
        public double CenterLat => (South + North) / 2.0;
        public double CenterLon => (West + East) / 2.0;

        // Calculate approximate width in meters
        public void WidthMeters()
        {
            return GeoConverter.HaversineDistance(South, West, South, East);
        }

        // Calculate approximate height in meters
        public void HeightMeters()
        {
            return GeoConverter.HaversineDistance(South, West, North, West);
        }
        // Represents a single geographic point in WGS84 coordinates.
        public class GeoPoint
        {
            public double Lat { get; set; }
            public double Lon { get; set; } 
            public GeoPoint(double lat, double lon)
            {
                Lat = lat;
                Lon = lon;
            }
        }

    }
}
/*
 * NOTE ON COORDINATES (WGS84):
 *
 * All geographic positions in this file use WGS84 latitude/longitude coordinates,
 * which is the standard system used by GPS, Google Maps, and OpenStreetMap.
 *
 * - Latitude (Lat):  north / south position on Earth
 *   Range: -90 (South Pole) to +90 (North Pole)
 *
 * - Longitude (Lon): east / west position on Earth
 *   Range: -180 (west) to +180 (east), with 0 at Greenwich (London)
 *
 * These values are expressed in DEGREES, not meters.
 * Because the Earth is curved, latitude/longitude cannot be used directly
 * for measuring distances or sizes.
 *
 * Whenever real-world distances are needed (meters),
 * the coordinates must first be converted using a geographic distance formula
 * (e.g. Haversine).
 */
