using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Services
{
    public class StreetsService
    {
        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };

        private static readonly string[] OverpassUrls = new[]
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass.openstreetmap.ru/api/interpreter"
        };

        private static int currentServerIndex = 0;

        public static async Task<List<Street>> GetStreetsAsync(GeoBoundingBox bbox, StreetFilter filter = null)
        {
            if (filter == null) filter = StreetFilter.Default;

            string query = BuildStreetsQuery(bbox, filter);

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
                        return ParseStreets(json);
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

        private static string BuildStreetsQuery(GeoBoundingBox bbox, StreetFilter filter)
        {
            var types = new List<string>();

            if (filter.IncludeMotorways) types.Add("\"highway\"=\"motorway\"");
            if (filter.IncludeTrunks) types.Add("\"highway\"=\"trunk\"");
            if (filter.IncludePrimary) types.Add("\"highway\"=\"primary\"");
            if (filter.IncludeSecondary) types.Add("\"highway\"=\"secondary\"");
            if (filter.IncludeTertiary) types.Add("\"highway\"=\"tertiary\"");
            if (filter.IncludeResidential) types.Add("\"highway\"=\"residential\"");
            if (filter.IncludeService) types.Add("\"highway\"=\"service\"");
            if (filter.IncludePedestrian) types.Add("\"highway\"=\"pedestrian\"");
            if (filter.IncludePaths) types.Add("\"highway\"=\"footway\"");

            string typeFilter = string.Join("", types.Select(t => $"way[{t}]({bbox.South},{bbox.West},{bbox.North},{bbox.East});"));

            return $@"
[out:json][timeout:30];
(
  {typeFilter}
);
out body;
>;
out skel qt;";
        }

        private static List<Street> ParseStreets(string json)
        {
            var streets = new List<Street>();

            try
            {
                var data = JObject.Parse(json);
                var elements = data["elements"];

                if (elements == null) return streets;

                // Create node lookup
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

                // Parse ways (streets)
                foreach (var element in elements)
                {
                    if (element["type"]?.ToString() == "way" && element["tags"] != null)
                    {
                        var tags = element["tags"];
                        var highwayType = tags["highway"]?.ToString();

                        if (!string.IsNullOrEmpty(highwayType))
                        {
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

                            if (street.Centerline.Count >= 2)
                            {
                                streets.Add(street);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse streets: {ex.Message}");
            }

            return streets;
        }

        private static double EstimateStreetWidth(string type, int? lanes)
        {
            // Default widths in meters based on road type
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

    public class StreetFilter
    {
        public bool IncludeMotorways { get; set; }
        public bool IncludeTrunks { get; set; }
        public bool IncludePrimary { get; set; }
        public bool IncludeSecondary { get; set; }
        public bool IncludeTertiary { get; set; }
        public bool IncludeResidential { get; set; }
        public bool IncludeService { get; set; }
        public bool IncludePedestrian { get; set; }
        public bool IncludePaths { get; set; }

        public static StreetFilter Default => new StreetFilter
        {
            IncludeMotorways = true,
            IncludeTrunks = true,
            IncludePrimary = true,
            IncludeSecondary = true,
            IncludeTertiary = true,
            IncludeResidential = true,
            IncludeService = false,  // Too many, usually not needed
            IncludePedestrian = false,
            IncludePaths = false
        };

        public static StreetFilter All => new StreetFilter
        {
            IncludeMotorways = true,
            IncludeTrunks = true,
            IncludePrimary = true,
            IncludeSecondary = true,
            IncludeTertiary = true,
            IncludeResidential = true,
            IncludeService = true,
            IncludePedestrian = true,
            IncludePaths = true
        };
    }
}