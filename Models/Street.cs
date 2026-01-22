using System.Collections.Generic;

namespace GeoDataPlugin.Models
{
    // Street/Road data from OSM
    public class Street
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public List<GeoPoint> Centerline { get; set; }
        public double Width { get; set; }
        public int? Lanes { get; set; }

        public Street()
        {
            Centerline = new List<GeoPoint>();
        }
    }
}
