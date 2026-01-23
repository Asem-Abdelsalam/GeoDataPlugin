using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPlugin.Models
{
    /// <summary>
    /// Unified container for all OSM data types (buildings, streets, etc.)
    /// Replaces separate BuildingDataCollection and StreetDataCollection
    /// </summary>
    [Serializable]
    public class OSMDataCollection
    {
        public List<Building> Buildings { get; set; }
        public List<Street> Streets { get; set; }

        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public GeoBoundingBox BoundingBox { get; set; }
        public DateTime DownloadTime { get; set; }

        public OSMDataCollection()
        {
            Buildings = new List<Building>();
            Streets = new List<Street>();
            DownloadTime = DateTime.Now;
        }

        /// <summary>
        /// Check if collection is empty
        /// </summary>
        public bool IsEmpty => Buildings.Count == 0 && Streets.Count == 0;

        /// <summary>
        /// Get total feature count
        /// </summary>
        public int TotalFeatures => Buildings.Count + Streets.Count;

        /// <summary>
        /// Generate summary of all data
        /// </summary>
        public string GetSummary()
        {
            var summary = $"OSM Data Summary:\n";
            summary += $"━━━━━━━━━━━━━━━━━━━━━━\n";
            summary += $"Origin: ({OriginLat:F6}, {OriginLon:F6})\n";
            summary += $"Downloaded: {DownloadTime:g}\n";
            summary += $"Total Features: {TotalFeatures}\n\n";

            // Buildings summary
            if (Buildings.Count > 0)
            {
                summary += $"Buildings: {Buildings.Count}\n";
                var buildingTypes = Buildings
                    .GroupBy(b => b.BuildingType ?? "unknown")
                    .OrderByDescending(g => g.Count())
                    .Take(5);

                foreach (var group in buildingTypes)
                {
                    summary += $"  • {group.Key}: {group.Count()}\n";
                }
                summary += "\n";
            }

            // Streets summary
            if (Streets.Count > 0)
            {
                summary += $"Streets: {Streets.Count}\n";

                // Calculate total length
                double totalLength = 0;
                foreach (var street in Streets)
                {
                    for (int i = 0; i < street.Centerline.Count - 1; i++)
                    {
                        var p1 = street.Centerline[i];
                        var p2 = street.Centerline[i + 1];
                        totalLength += Utils.GeoConverter.HaversineDistance(p1.Lat, p1.Lon, p2.Lat, p2.Lon);
                    }
                }
                summary += $"  Total length: {totalLength / 1000:F2} km\n";

                var streetTypes = Streets
                    .GroupBy(s => s.Type ?? "unknown")
                    .OrderByDescending(g => g.Count())
                    .Take(5);

                foreach (var group in streetTypes)
                {
                    summary += $"  • {group.Key}: {group.Count()}\n";
                }
            }

            if (IsEmpty)
            {
                summary = "No data available";
            }

            return summary;
        }

        /// <summary>
        /// Filter buildings by type
        /// </summary>
        public List<Building> GetBuildingsByType(params string[] types)
        {
            if (types == null || types.Length == 0) return Buildings;

            var lowerTypes = types.Select(t => t.ToLower()).ToList();
            return Buildings.Where(b =>
                lowerTypes.Any(t => (b.BuildingType ?? "yes").ToLower().Contains(t))
            ).ToList();
        }

        /// <summary>
        /// Filter buildings by height range
        /// </summary>
        public List<Building> GetBuildingsByHeight(double minHeight, double maxHeight = double.MaxValue)
        {
            return Buildings.Where(b =>
            {
                double height = b.GetHeight();
                return height >= minHeight && height <= maxHeight;
            }).ToList();
        }

        /// <summary>
        /// Filter streets by type
        /// </summary>
        public List<Street> GetStreetsByType(params string[] types)
        {
            if (types == null || types.Length == 0) return Streets;

            var lowerTypes = types.Select(t => t.ToLower()).ToList();
            return Streets.Where(s =>
                lowerTypes.Any(t => (s.Type ?? "unknown").ToLower().Contains(t))
            ).ToList();
        }

        /// <summary>
        /// Get major roads (motorways, trunks, primary, secondary)
        /// </summary>
        public List<Street> GetMajorRoads()
        {
            return Streets.Where(s =>
            {
                var type = (s.Type ?? "").ToLower();
                return type == "motorway" || type == "trunk" ||
                       type == "primary" || type == "secondary";
            }).ToList();
        }

        /// <summary>
        /// Get residential streets
        /// </summary>
        public List<Street> GetResidentialStreets()
        {
            return Streets.Where(s =>
            {
                var type = (s.Type ?? "").ToLower();
                return type == "residential" || type == "tertiary";
            }).ToList();
        }
    }
}