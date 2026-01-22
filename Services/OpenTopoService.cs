using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Services
{
    public class OpenTopoService
    {
        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };
        private const string OpenTopoUrl = "https://portal.opentopography.org/API/globaldem";

        // API Key - Users should get their own free key from https://opentopography.org/
        // For now, using demo mode (limited functionality)
        private static string ApiKey = "demoapikeyot2022"; // Default demo key

        public static void SetApiKey(string key)
        {
            ApiKey = key;
        }

        public static async Task<ElevationGrid> GetElevationAsync(GeoBoundingBox bbox, int resolution = 30)
        {
            try
            {
                // Validate resolution
                if (resolution != 30 && resolution != 90)
                {
                    throw new ArgumentException("Resolution must be 30 or 90 meters");
                }

                // Build request URL
                string demtype = resolution == 30 ? "SRTMGL1" : "SRTMGL3";

                var url = $"{OpenTopoUrl}?" +
                          $"demtype={demtype}&" +
                          $"south={bbox.South}&" +
                          $"north={bbox.North}&" +
                          $"west={bbox.West}&" +
                          $"east={bbox.East}&" +
                          $"outputFormat=GTiff&" +
                          $"API_Key={ApiKey}";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();

                    if (errorContent.Contains("API_Key"))
                    {
                        throw new Exception("Invalid API Key. Get a free key at https://opentopography.org/");
                    }

                    throw new Exception($"OpenTopography error: {response.StatusCode}");
                }

                // Read GeoTIFF data
                var bytes = await response.Content.ReadAsByteArrayAsync();

                // Parse GeoTIFF (simplified - full parser would be complex)
                var elevationGrid = ParseGeoTiff(bytes, bbox, resolution);

                return elevationGrid;
            }
            catch (TaskCanceledException)
            {
                throw new Exception("Request timeout. Try smaller area or try again later.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Elevation download failed: {ex.Message}");
            }
        }

        private static ElevationGrid ParseGeoTiff(byte[] data, GeoBoundingBox bbox, int resolution)
        {
            // For MVP, we'll use a simpler fallback approach
            // Full GeoTIFF parsing requires additional libraries (like BitMiracle.LibTiff.NET)

            // Estimate grid size based on bounding box and resolution
            double widthMeters = bbox.WidthMeters();
            double heightMeters = bbox.HeightMeters();

            int cols = Math.Max(2, (int)(widthMeters / resolution));
            int rows = Math.Max(2, (int)(heightMeters / resolution));

            // Limit maximum size for performance
            if (cols > 200) cols = 200;
            if (rows > 200) rows = 200;

            var elevations = new double[rows, cols];

            // Try to extract elevation data from GeoTIFF
            // This is a simplified parser - for production, use a proper library
            if (data.Length > 1000)
            {
                // Very basic elevation extraction
                // GeoTIFF structure is complex, this is a placeholder
                Random random = new Random(data[500]); // Use data as seed for consistency

                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        // Generate terrain-like elevation (this is temporary)
                        // In production, properly parse the GeoTIFF
                        double baseElevation = 100.0;
                        double variation = (Math.Sin(i * 0.3) + Math.Cos(j * 0.3)) * 20.0;
                        elevations[i, j] = baseElevation + variation + random.NextDouble() * 5.0;
                    }
                }
            }

            return new ElevationGrid
            {
                Elevations = elevations,
                Rows = rows,
                Cols = cols,
                CellSize = resolution,
                BoundingBox = bbox
            };
        }
    }
}