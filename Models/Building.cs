using System.Collections.Generic;

namespace GeoDataPlugin.Models
{
    public class Building
    {
        public string Id { get; set; }
        public List<GeoPoint> Footprint { get; set; }
        public double? Height { get; set; }
        public int? Levels { get; set; }
        public string BuildingType { get; set; }

        public Building()
        {
            Footprint = new List<GeoPoint>();
        }

        public double GetHeight()
        {
            if (Height.HasValue) return Height.Value;
            if (Levels.HasValue) return Levels.Value * 3.5; // 3.5m is floor height
            return 10.0;  // Default value for height
        }
    }
}

