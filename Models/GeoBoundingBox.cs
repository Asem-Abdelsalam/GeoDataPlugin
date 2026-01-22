using GeoDataPlugin.Utils;
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
    public class GeoBoundingBox
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
        public double WidthMeters()
        {
            return GeoConverter.HaversineDistance(South, West, South, East);
        }

        // Calculate approximate height in meters
        public double HeightMeters()
        {
            return GeoConverter.HaversineDistance(South, West, North, West);
        }        
    }
}
