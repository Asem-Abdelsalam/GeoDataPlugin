namespace GeoDataPlugin.Models
{
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