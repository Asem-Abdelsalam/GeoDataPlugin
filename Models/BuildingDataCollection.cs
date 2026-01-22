using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPlugin.Models
{
    // Container for raw OSM building data (serializable)
    [Serializable]
    public class BuildingDataCollection
    {
        public List<Building> Buildings { get; set; }
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public GeoBoundingBox BoundingBox { get; set; }
        public DateTime DownloadTime { get; set; }

        public BuildingDataCollection()
        {
            Buildings = new List<Building>();
            DownloadTime = DateTime.Now;
        }

        public string GetSummary()
        {
            var typeGroups = Buildings.GroupBy(b => b.BuildingType ?? "unknown");
            var summary = $"Buildings: {Buildings.Count}\n";
            summary += $"Origin: ({OriginLat:F6}, {OriginLon:F6})\n";
            summary += $"Downloaded: {DownloadTime:g}\n";
            summary += "Types:\n";
            foreach (var group in typeGroups.OrderByDescending(g => g.Count()).Take(5))
            {
                summary += $"  {group.Key}: {group.Count()}\n";
            }
            return summary;
        }
    }
}
