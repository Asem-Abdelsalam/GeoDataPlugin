using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Services
{
    /// <summary>
    /// Unified service for downloading all OSM data types in a single optimized query
    /// </summary>
    public class UnifiedOSMService
    {
        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };

        private static readonly string[] OverpassUrls = new[]
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass.openstreetmap.ru/api/interpreter"
        };

        private static int currentServerIndex = 0;

        /// <summary>
        /// Download all requested OSM data in a single optimized query
        /// </summary>
        public static async Task<OSMDataCollection> GetOSMDataAsync(
            GeoBoundingBox bbox,
            bool includeBuildings,
            bool includeStreets,
            StreetFilter streetFilter = null)
        {
            if (streetFilter == null) streetFilter = StreetFilter.Default;

            // Build unified query for all requested data
            string query = BuildUnifiedQuery(bbox, includeBuildings, includeStreets, streetFilter);

            // Try up to 3 times with different servers
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string url = OverpassUrls[currentServerIndex % OverpassUrls.Length];

                    var content = new StringContent($"data={Uri.EscapeDataString(query)}");
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

                    var response = await client.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        return ParseUnifiedResponse(json, includeBuildings, includeStreets);
                    }
                    else if ((int)response.StatusCode == 429 || (int)response.StatusCode == 504)
                    {
                        currentServerIndex++;
                        if (attempt < 2)
                        {
                            await Task.Delay(2000 * (attempt + 1));
                            continue;
                        }
                    }

                    throw new Exception($"Server returned: {response.StatusCode}");
                }
                catch (TaskCanceledException)
                {
                    currentServerIndex++;
                    if (attempt < 2)
                    {
                        await Task.Delay(1000);
                        continue;
                    }
                    throw new Exception("Request timeout - try reducing radius");
                }
                catch (Exception ex) when (attempt < 2)
                {
                    currentServerIndex++;
                    await Task.Delay(2000);
                    continue;
                }
            }

            throw new Exception("Failed to connect after 3 attempts");
        }

        /// <summary>
        /// Build a single optimized Overpass query for all requested data types
        /// </summary>
        private static string BuildUnifiedQuery(
            GeoBoundingBox bbox,
            bool includeBuildings,
            bool includeStreets,
            StreetFilter filter)
        {
            var queries = new List<string>();

            // Buildings query
            if (includeBuildings)
            {
                queries.Add($"way[\"building\"]({bbox.South},{bbox.West},{bbox.North},{bbox.East});");
            }

            // Streets query
            if (includeStreets)
            {
                var streetTypes = new List<string>();

                if (filter.IncludeMotorways) streetTypes.Add("\"highway\"=\"motorway\"");
                if (filter.IncludeTrunks) streetTypes.Add("\"highway\"=\"trunk\"");
                if (filter.IncludePrimary) streetTypes.Add("\"highway\"=\"primary\"");
                if (filter.IncludeSecondary) streetTypes.Add("\"highway\"=\"secondary\"");
                if (filter.IncludeTertiary) streetTypes.Add("\"highway\"=\"tertiary\"");
                if (filter.IncludeResidential) streetTypes.Add("\"highway\"=\"residential\"");
                if (filter.IncludeService) streetTypes.Add("\"highway\"=\"service\"");
                if (filter.IncludePedestrian) streetTypes.Add("\"highway\"=\"pedestrian\"");
                if (filter.IncludePaths) streetTypes.Add("\"highway\"=\"footway\"");

                foreach (var type in streetTypes)
                {
                    queries.Add($"way[{type}]({bbox.South},{bbox.West},{bbox.North},{bbox.East});");
                }
            }

            string combinedQueries = string.Join("\n  ", queries);

            return $@"
[out:json][timeout:30];
(
  {combinedQueries}
);
out body;
>;
out skel qt;";
        }

        /// <summary>
        /// Parse unified response and separate into buildings and streets
        /// </summary>
        private static OSMDataCollection ParseUnifiedResponse(
            string json,
            bool includeBuildings,
            bool includeStreets)
        {
            var result = new OSMDataCollection();

            try
            {
                var data = JObject.Parse(json);
                var elements = data["elements"];

                if (elements == null) return result;

                // Build node lookup (shared by both buildings and streets)
                var nodes = new Dictionary<long, GeoPoint>();
                foreach (var element in elements)
                {
                    if (element["type"]?.ToString() == "node")
                    {
                        long id = element["id"].Value<long>();
                        double lat = element["lat"].Value<double>();
                        double lon = element["lon"].Value<double>();
                        nodes[id] = new GeoPoint(lat, lon);
                    }
                }

                // Parse ways - separate into buildings and streets based on tags
                foreach (var element in elements)
                {
                    if (element["type"]?.ToString() == "way" && element["tags"] != null)
                    {
                        var tags = element["tags"];

                        // Check if it's a building
                        if (includeBuildings && tags["building"] != null)
                        {
                            var building = ParseBuilding(element, tags, nodes);
                            if (building != null && building.Footprint.Count >= 3)
                            {
                                result.Buildings.Add(building);
                            }
                        }
                        // Check if it's a street
                        else if (includeStreets && tags["highway"] != null)
                        {
                            var street = ParseStreet(element, tags, nodes);
                            if (street != null && street.Centerline.Count >= 2)
                            {
                                result.Streets.Add(street);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse response: {ex.Message}");
            }

            return result;
        }

        private static Building ParseBuilding(JToken element, JToken tags, Dictionary<long, GeoPoint> nodes)
        {
            var building = new Building
            {
                Id = element["id"].ToString(),
                BuildingType = tags["building"]?.ToString() ?? "yes"
            };

            // Parse height
            if (tags["height"] != null)
            {
                string heightStr = tags["height"].ToString().Replace("m", "").Trim();
                if (double.TryParse(heightStr, out double height))
                    building.Height = height;
            }

            // Parse levels
            if (tags["building:levels"] != null)
            {
                if (int.TryParse(tags["building:levels"].ToString(), out int levels))
                    building.Levels = levels;
            }

            // Get footprint nodes
            var nodeRefs = element["nodes"];
            if (nodeRefs != null)
            {
                foreach (var nodeRef in nodeRefs)
                {
                    long nodeId = nodeRef.Value<long>();
                    if (nodes.ContainsKey(nodeId))
                    {
                        building.Footprint.Add(nodes[nodeId]);
                    }
                }
            }

            return building;
        }

        private static Street ParseStreet(JToken element, JToken tags, Dictionary<long, GeoPoint> nodes)
        {
            var highwayType = tags["highway"]?.ToString();
            if (string.IsNullOrEmpty(highwayType)) return null;

            var street = new Street
            {
                Id = element["id"].ToString(),
                Type = highwayType,
                Name = tags["name"]?.ToString() ?? "Unnamed",
                Width = 0
            };

            // Parse lanes
            if (tags["lanes"] != null)
            {
                if (int.TryParse(tags["lanes"].ToString(), out int lanes))
                {
                    street.Lanes = lanes;
                }
            }

            // Parse width
            if (tags["width"] != null)
            {
                string widthStr = tags["width"].ToString().Replace("m", "").Trim();
                if (double.TryParse(widthStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double width))
                {
                    street.Width = width;
                }
            }

            // Estimate width from type if not specified
            if (street.Width == 0)
            {
                street.Width = EstimateStreetWidth(highwayType, street.Lanes);
            }

            // Get centerline nodes
            var nodeRefs = element["nodes"];
            if (nodeRefs != null)
            {
                foreach (var nodeRef in nodeRefs)
                {
                    long nodeId = nodeRef.Value<long>();
                    if (nodes.ContainsKey(nodeId))
                    {
                        street.Centerline.Add(nodes[nodeId]);
                    }
                }
            }

            return street;
        }

        private static double EstimateStreetWidth(string type, int? lanes)
        {
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
}