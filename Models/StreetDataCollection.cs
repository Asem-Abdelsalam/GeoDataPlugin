using GeoDataPlugin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPlugin.Models
{
    // Container for raw OSM street data (serializable)
    [Serializable]
    public class StreetDataCollection
    {
        public List<Street> Streets { get; set; }
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public GeoBoundingBox BoundingBox { get; set; }
        public DateTime DownloadTime { get; set; }

        public StreetDataCollection()
        {
            Streets = new List<Street>();
            DownloadTime = DateTime.Now;
        }

        public string GetSummary()
        {
            var typeGroups = Streets.GroupBy(s => s.Type ?? "unknown");
            var summary = $"Streets: {Streets.Count}\n";
            summary += $"Origin: ({OriginLat:F6}, {OriginLon:F6})\n";
            summary += $"Downloaded: {DownloadTime:g}\n";

            double totalLength = 0;
            foreach (var street in Streets)
            {
                for (int i = 0; i < street.Centerline.Count - 1; i++)
                {
                    var p1 = street.Centerline[i];
                    var p2 = street.Centerline[i + 1];
                    totalLength += GeoConverter.HaversineDistance(p1.Lat, p1.Lon, p2.Lat, p2.Lon);
                }
            }

            summary += $"Total length: {totalLength / 1000:F2} km\n";
            summary += "Types:\n";
            foreach (var group in typeGroups.OrderByDescending(g => g.Count()).Take(5))
            {
                summary += $"  {group.Key}: {group.Count()}\n";
            }
            return summary;
        }
    }
}
