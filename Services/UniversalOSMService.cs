using GeoDataPlugin.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GeoDataPlugin.Services
{
    public class UniversalOSMService
    {
        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };

        private static readonly string[] OverpassUrls = new[]
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass.openstreetmap.ru/api/interpreter"
        };

        private static int currentServerIndex = 0;

        public static async Task<OSMDataset> DownloadAllDataAsync(GeoBoundingBox bbox)
        {
            string query = BuildUniversalQuery(bbox);

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
                        return ParseUniversalData(json, bbox);
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
                    throw new Exception("Request timeout - try smaller area");
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

        private static string BuildUniversalQuery(GeoBoundingBox bbox)
        {
            // Download everything useful from OSM
            return $@"
[out:json][timeout:60];
(
  // Buildings
  way[""building""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  
  // Streets/Roads
  way[""highway""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  
  // Parks and green spaces
  way[""leisure""=""park""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  way[""leisure""=""garden""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  
  // Water bodies
  way[""natural""=""water""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  way[""waterway""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  
  // Railways
  way[""railway""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  
  // Land use
  way[""landuse""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  
  // Amenities (important POIs)
  way[""amenity""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
  node[""amenity""]({bbox.South},{bbox.West},{bbox.North},{bbox.East});
);
out body;
>;
out skel qt;";
        }

        private static OSMDataset ParseUniversalData(string json, GeoBoundingBox bbox)
        {
            var dataset = new OSMDataset
            {
                BoundingBox = bbox,
                OriginLat = bbox.CenterLat,
                OriginLon = bbox.CenterLon
            };

            try
            {
                var data = JObject.Parse(json);
                var elements = data["elements"];

                if (elements == null) return dataset;

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

                        // Check if node has amenity tag (POI)
                        var tags = element["tags"];
                        if (tags != null && tags["amenity"] != null)
                        {
                            var feature = new OSMFeature
                            {
                                Id = id.ToString(),
                                Type = OSMDataType.Amenities,
                                Geometry = new List<GeoPoint> { new GeoPoint(lat, lon) },
                                Name = tags["name"]?.ToString() ?? "Unnamed"
                            };

                            foreach (var tag in tags.Cast<JProperty>())
                            {
                                feature.Tags[tag.Name] = tag.Value.ToString();
                            }

                            dataset.Features.Add(feature);
                        }
                    }
                }

                // Parse ways
                foreach (var element in elements)
                {
                    if (element["type"]?.ToString() == "way" && element["tags"] != null)
                    {
                        var tags = element["tags"];
                        var feature = new OSMFeature
                        {
                            Id = element["id"].ToString(),
                            Name = tags["name"]?.ToString() ?? "Unnamed"
                        };

                        // Store all tags
                        foreach (var tag in tags.Cast<JProperty>())
                        {
                            feature.Tags[tag.Name] = tag.Value.ToString();
                        }

                        // Determine type
                        if (tags["building"] != null)
                            feature.Type = OSMDataType.Buildings;
                        else if (tags["highway"] != null)
                            feature.Type = OSMDataType.Streets;
                        else if (tags["leisure"] != null && (tags["leisure"].ToString() == "park" || tags["leisure"].ToString() == "garden"))
                            feature.Type = OSMDataType.Parks;
                        else if (tags["natural"] != null && tags["natural"].ToString() == "water" || tags["waterway"] != null)
                            feature.Type = OSMDataType.Water;
                        else if (tags["railway"] != null)
                            feature.Type = OSMDataType.Railways;
                        else if (tags["landuse"] != null)
                            feature.Type = OSMDataType.Landuse;
                        else if (tags["amenity"] != null)
                            feature.Type = OSMDataType.Amenities;
                        else
                            continue; // Skip unknown types

                        // Get geometry
                        var nodeRefs = element["nodes"];
                        if (nodeRefs != null)
                        {
                            foreach (var nodeRef in nodeRefs)
                            {
                                long nodeId = nodeRef.Value<long>();
                                if (nodes.ContainsKey(nodeId))
                                {
                                    feature.Geometry.Add(nodes[nodeId]);
                                }
                            }
                        }

                        if (feature.Geometry.Count >= 2) // Valid feature
                        {
                            dataset.Features.Add(feature);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse data: {ex.Message}");
            }

            return dataset;
        }
    }
}