using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GeoDataPlugin.Models;
using GeoDataPlugin.Services;
using GeoDataPlugin.Utils;

namespace GeoDataPlugin.Components
{
    public class UnifiedOSMDownloader : GH_Component
    {
        private OSMDataCollection cachedData = null;
        private string lastCacheKey = "";
        private bool isDownloading = false;

        public UnifiedOSMDownloader()
          : base("OSM Data Downloader", "OSM Download",
              "Download all OpenStreetMap data (buildings, streets, etc.) in one unified component",
              "GeoData", "Data")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Center latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Center longitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Radius in meters", GH_ParamAccess.item, 200.0);

            // Feature toggles
            pManager.AddBooleanParameter("Get Buildings", "Buildings", "Download building data", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Get Streets", "Streets", "Download street data", GH_ParamAccess.item, false);

            // Street filter
            pManager.AddBooleanParameter("Major Roads", "Major", "Include highways/main roads", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Residential", "Resid", "Include residential streets", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Service Roads", "Serv", "Include service roads", GH_ParamAccess.item, false);

            // Control
            pManager.AddBooleanParameter("Download", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Clear Cache", "Clear", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("OSM Data", "Data", "Complete OSM data collection", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Info", "Data summary", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Building Count", "B#", "Number of buildings", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Street Count", "S#", "Number of streets", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 200;
            bool getBuildings = true, getStreets = false;
            bool majorRoads = true, residential = true, serviceRoads = false;
            bool download = false, clear = false;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;
            if (!DA.GetData(2, ref radius)) return;
            if (!DA.GetData(3, ref getBuildings)) return;
            if (!DA.GetData(4, ref getStreets)) return;
            if (!DA.GetData(5, ref majorRoads)) return;
            if (!DA.GetData(6, ref residential)) return;
            if (!DA.GetData(7, ref serviceRoads)) return;
            if (!DA.GetData(8, ref download)) return;
            if (!DA.GetData(9, ref clear)) return;

            string cacheKey = $"{lat:F6}_{lon:F6}_{radius:F1}_{getBuildings}_{getStreets}_{majorRoads}_{residential}_{serviceRoads}";

            if (clear)
            {
                cachedData = null;
                lastCacheKey = "";
                DA.SetData(1, "Cache cleared. Set Download=True to fetch data.");
                DA.SetData(2, 0);
                DA.SetData(3, 0);
                return;
            }

            // Return cached data if available
            if (cachedData != null && cacheKey == lastCacheKey)
            {
                OutputCachedData(DA);
                return;
            }

            if (!download)
            {
                if (cachedData != null)
                {
                    OutputCachedData(DA);
                }
                else
                {
                    DA.SetData(1, "Set Download=True to fetch OSM data");
                    DA.SetData(2, 0);
                    DA.SetData(3, 0);
                }
                return;
            }

            if (isDownloading)
            {
                DA.SetData(1, "Downloading... Please wait.");
                return;
            }

            // Validate inputs
            if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid coordinates");
                return;
            }

            if (!getBuildings && !getStreets)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Enable at least one data type (Buildings or Streets)");
                return;
            }

            try
            {
                isDownloading = true;
                var stopwatch = Stopwatch.StartNew();

                var bbox = GeoConverter.CreateBBox(lat, lon, radius);
                Message = "Downloading...";

                // Create street filter
                var streetFilter = new StreetFilter
                {
                    IncludeMotorways = majorRoads,
                    IncludeTrunks = majorRoads,
                    IncludePrimary = majorRoads,
                    IncludeSecondary = majorRoads,
                    IncludeTertiary = residential,
                    IncludeResidential = residential,
                    IncludeService = serviceRoads,
                    IncludePedestrian = false,
                    IncludePaths = false
                };

                // Download data using unified service
                var task = Task.Run(async () => await UnifiedOSMService.GetOSMDataAsync(
                    bbox,
                    getBuildings,
                    getStreets,
                    streetFilter
                ));

                if (!task.Wait(TimeSpan.FromSeconds(65)))
                {
                    throw new Exception("Download timeout. Try reducing radius.");
                }

                var osmData = task.Result;
                stopwatch.Stop();

                if (osmData.IsEmpty)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No data found");
                    DA.SetData(1, "No data found in this area");
                    DA.SetData(2, 0);
                    DA.SetData(3, 0);
                    return;
                }

                // Add metadata
                osmData.OriginLat = lat;
                osmData.OriginLon = lon;
                osmData.BoundingBox = bbox;
                osmData.DownloadTime = DateTime.Now;

                cachedData = osmData;
                lastCacheKey = cacheKey;

                DA.SetData(0, new GH_ObjectWrapper(cachedData));
                DA.SetData(1, $"✓ Downloaded in {stopwatch.ElapsedMilliseconds}ms\n" + cachedData.GetSummary());
                DA.SetData(2, osmData.Buildings.Count);
                DA.SetData(3, osmData.Streets.Count);

                Message = $"{osmData.Buildings.Count}B, {osmData.Streets.Count}S";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(1, $"❌ Error: {ex.Message}");
                DA.SetData(2, 0);
                DA.SetData(3, 0);
                Message = "Error";
            }
            finally
            {
                isDownloading = false;
            }
        }

        private void OutputCachedData(IGH_DataAccess DA)
        {
            DA.SetData(0, new GH_ObjectWrapper(cachedData));
            DA.SetData(1, "✓ Cached Data\n" + cachedData.GetSummary());
            DA.SetData(2, cachedData.Buildings.Count);
            DA.SetData(3, cachedData.Streets.Count);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("A1B2C3D4-E5F6-4A5B-8C7D-9E8F7A6B5C4D");
    }
}