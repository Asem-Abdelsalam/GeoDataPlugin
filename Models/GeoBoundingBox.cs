using GeoDataPlugin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPlugin.Models
{
    // Geographic bounding box in WGS84 coordinates
    public class GeoBoundingBox
    {
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

        public double CenterLat => (South + North) / 2.0;
        public double CenterLon => (West + East) / 2.0;

        public double WidthMeters()
        {
            return GeoConverter.HaversineDistance(South, West, South, East);
        }

        public double HeightMeters()
        {
            return GeoConverter.HaversineDistance(South, West, North, West);
        }
    }

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

    // Enumeration of OSM data types
    public enum OSMDataType
    {
        Buildings,
        Streets,
        Parks,
        Water,
        Railways,
        Landuse,
        Amenities,
        All
    }

    // Universal OSM feature (can be building, street, park, etc.)
    [Serializable]
    public class OSMFeature
    {
        public string Id { get; set; }
        public OSMDataType Type { get; set; }
        public Dictionary<string, string> Tags { get; set; }
        public List<GeoPoint> Geometry { get; set; }
        public string Name { get; set; }

        public OSMFeature()
        {
            Tags = new Dictionary<string, string>();
            Geometry = new List<GeoPoint>();
        }

        // Get specific tag value
        public string GetTag(string key)
        {
            return Tags.ContainsKey(key) ? Tags[key] : null;
        }

        // Check if has tag
        public bool HasTag(string key)
        {
            return Tags.ContainsKey(key);
        }
    }

    // Complete OSM dataset for a region
    [Serializable]
    public class OSMDataset
    {
        public List<OSMFeature> Features { get; set; }
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public GeoBoundingBox BoundingBox { get; set; }
        public DateTime DownloadTime { get; set; }

        public OSMDataset()
        {
            Features = new List<OSMFeature>();
            DownloadTime = DateTime.Now;
        }

        // Get features by type
        public List<OSMFeature> GetFeaturesByType(OSMDataType type)
        {
            if (type == OSMDataType.All)
                return Features;

            return Features.Where(f => f.Type == type).ToList();
        }

        // Get summary
        public string GetSummary()
        {
            var typeGroups = Features.GroupBy(f => f.Type);
            var summary = $"Total Features: {Features.Count}\n";
            summary += $"Origin: ({OriginLat:F6}, {OriginLon:F6})\n";
            summary += $"Downloaded: {DownloadTime:g}\n";
            summary += "Breakdown:\n";
            foreach (var group in typeGroups.OrderByDescending(g => g.Count()))
            {
                summary += $"  {group.Key}: {group.Count()}\n";
            }
            return summary;
        }

        // Query by tag
        public List<OSMFeature> QueryByTag(string key, string value = null)
        {
            if (value == null)
            {
                return Features.Where(f => f.HasTag(key)).ToList();
            }
            else
            {
                return Features.Where(f => f.GetTag(key) == value).ToList();
            }
        }
    }

    // Legacy models for backward compatibility with processors
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
            if (Levels.HasValue) return Levels.Value * 3.5;
            return 10.0;
        }

        // Convert from OSMFeature
        public static Building FromOSMFeature(OSMFeature feature)
        {
            var building = new Building
            {
                Id = feature.Id,
                Footprint = feature.Geometry,
                BuildingType = feature.GetTag("building")
            };

            // Parse height
            var heightStr = feature.GetTag("height");
            if (!string.IsNullOrEmpty(heightStr))
            {
                heightStr = heightStr.Replace("m", "").Trim();
                if (double.TryParse(heightStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double h))
                {
                    building.Height = h;
                }
            }

            // Parse levels
            var levelsStr = feature.GetTag("building:levels");
            if (!string.IsNullOrEmpty(levelsStr))
            {
                if (int.TryParse(levelsStr, out int levels))
                {
                    building.Levels = levels;
                }
            }

            return building;
        }
    }

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

        // Convert from OSMFeature
        public static Street FromOSMFeature(OSMFeature feature)
        {
            var street = new Street
            {
                Id = feature.Id,
                Name = feature.Name,
                Type = feature.GetTag("highway"),
                Centerline = feature.Geometry
            };

            // Parse lanes
            var lanesStr = feature.GetTag("lanes");
            if (!string.IsNullOrEmpty(lanesStr))
            {
                if (int.TryParse(lanesStr, out int lanes))
                {
                    street.Lanes = lanes;
                }
            }

            // Parse width
            var widthStr = feature.GetTag("width");
            if (!string.IsNullOrEmpty(widthStr))
            {
                widthStr = widthStr.Replace("m", "").Trim();
                if (double.TryParse(widthStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double width))
                {
                    street.Width = width;
                }
            }

            // Estimate width if not specified
            if (street.Width == 0)
            {
                street.Width = EstimateStreetWidth(street.Type, street.Lanes);
            }

            return street;
        }

        private static double EstimateStreetWidth(string type, int? lanes)
        {
            if (string.IsNullOrEmpty(type)) return 5.0;

            switch (type.ToLower())
            {
                case "motorway": return lanes.HasValue ? lanes.Value * 3.7 : 15.0;
                case "trunk": return lanes.HasValue ? lanes.Value * 3.5 : 12.0;
                case "primary": return lanes.HasValue ? lanes.Value * 3.5 : 10.0;
                case "secondary": return lanes.HasValue ? lanes.Value * 3.5 : 8.0;
                case "tertiary": return lanes.HasValue ? lanes.Value * 3.0 : 6.0;
                case "residential": return lanes.HasValue ? lanes.Value * 3.0 : 6.0;
                case "service": return 4.0;
                case "pedestrian": return 3.0;
                case "footway": return 2.0;
                case "path": return 1.5;
                default: return 5.0;
            }
        }
    }

    // Elevation grid data
    public class ElevationGrid
    {
        public double[,] Elevations { get; set; }
        public int Rows { get; set; }
        public int Cols { get; set; }
        public double CellSize { get; set; }
        public GeoBoundingBox BoundingBox { get; set; }

        public double MinElevation
        {
            get
            {
                double min = double.MaxValue;
                for (int i = 0; i < Rows; i++)
                {
                    for (int j = 0; j < Cols; j++)
                    {
                        if (Elevations[i, j] < min) min = Elevations[i, j];
                    }
                }
                return min;
            }
        }

        public double MaxElevation
        {
            get
            {
                double max = double.MinValue;
                for (int i = 0; i < Rows; i++)
                {
                    for (int j = 0; j < Cols; j++)
                    {
                        if (Elevations[i, j] > max) max = Elevations[i, j];
                    }
                }
                return max;
            }
        }
    }
}