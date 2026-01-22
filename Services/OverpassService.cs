using GeoDataPlugin.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static GeoDataPlugin.Models.GeoBoundingBox;


namespace GeoDataPlugin.Services
{
    // ask OpenStreetMap for all buildings inside a given area and converts them into usable Building objects.
    // GeoBoundingBox > Overpass API (OpenStreetMap) > JSON data > Building objects (footprints + height)
    public class OverpassService
    {
        private static readonly HttpClient client = new HttpClient();

        // Multiple Overpass API servers for load balancing
        private static readonly string[] OverpassUrls = new[]
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass.openstreetmap.ru/api/interpreter"
        };

        private static int currentServerIndex = 0;


        // get all buildings inside the geographic rectangle
        public static async Task<List<Building>> GetBuildingsAsync(GeoBoundingBox bbox)
        {
            string query = BuildOverpassQuery(bbox);

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
                        return ParseBuildings(json);
                    }
                    else if ((int)response.StatusCode == 429 || (int)response.StatusCode == 504)
                    {
                        // Rate limited or timeout - try next server
                        currentServerIndex++;

                        if (attempt < 2)
                        {
                            // Wait before retry
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
                    throw new Exception("Request timeout - try reducing the radius or try again later");
                }
                catch (Exception ex) when (attempt < 2)
                {
                    currentServerIndex++;
                    await Task.Delay(2000);
                    continue;
                }
            }

            throw new Exception("Failed to connect to Overpass API after 3 attempts. The servers may be overloaded. Try again in a few minutes or reduce the search radius.");
        }


        // Overpass QL query to get buildings in bounding box
        // Format: (south, west, north, east)
        private static string BuildOverpassQuery(GeoBoundingBox bbox)
        {
            return $@"
[out:json][timeout:30];
(
  way[""building""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
);
out body;
>;
out skel qt;";
        }



        // Parses Overpass API JSON response into Building objects
        private static List<Building> ParseBuildings(string json)
        {
            var buildings = new List<Building>();

            try
            {
                var data = JObject.Parse(json);
                var elements = data["elements"];

                if (elements == null)
                {
                    return buildings;
                }

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

                // Parse ways (building footprints)
                // Each way contains an array of node IDs that form the building outline
                foreach (var element in elements)
                {
                    if (element["type"]?.ToString() == "way" && element["tags"] != null)
                    {
                        var tags = element["tags"];
                        if (tags["building"] != null)
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

                            if (building.Footprint.Count >= 3) // Valid polygon
                            {
                                buildings.Add(building);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse response: {ex.Message}");
            }


            return buildings;
        }
    }
}
