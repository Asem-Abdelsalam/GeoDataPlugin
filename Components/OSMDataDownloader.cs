using GeoDataPlugin.Models;
using GeoDataPlugin.Services;
using GeoDataPlugin.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GeoDataPlugin.Components
{
    public class OSMDataDownloader : GH_Component
    {
        private BuildingDataCollection cachedData = null;
        private string lastCacheKey = "";
        private bool isDownloading = false;

        public OSMDataDownloader()
          : base("OSM Data Download", "OSM Download",
              "Download raw building data from OpenStreetMap (no geometry processing)",
              "GeoData", "Data")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Center latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Center longitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Radius in meters", GH_ParamAccess.item, 200.0);
            pManager.AddBooleanParameter("Download", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Clear Cache", "Clear", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Building Data", "Data", "Raw building data collection", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Info", "Data summary", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number of buildings", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 200;
            bool download = false, clear = false;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;
            if (!DA.GetData(2, ref radius)) return;
            if (!DA.GetData(3, ref download)) return;
            if (!DA.GetData(4, ref clear)) return;

            string cacheKey = $"{lat:F6}_{lon:F6}_{radius:F1}";

            if (clear)
            {
                cachedData = null;
                lastCacheKey = "";
                DA.SetData(1, "Cache cleared. Set Download=True to fetch data.");
                DA.SetData(2, 0);
                return;
            }

            // Return cached data if available
            if (cachedData != null && cacheKey == lastCacheKey)
            {
                DA.SetData(0, new GH_ObjectWrapper(cachedData));
                DA.SetData(1, "✓ Cached Data\n" + cachedData.GetSummary());
                DA.SetData(2, cachedData.Buildings.Count);
                return;
            }

            if (!download)
            {
                if (cachedData != null)
                {
                    DA.SetData(0, new GH_ObjectWrapper(cachedData));
                    DA.SetData(1, "✓ Cached\n" + cachedData.GetSummary());
                    DA.SetData(2, cachedData.Buildings.Count);
                }
                else
                {
                    DA.SetData(1, "Set Download=True to fetch building data from OpenStreetMap");
                    DA.SetData(2, 0);
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

            try
            {
                isDownloading = true;
                var stopwatch = Stopwatch.StartNew();

                var bbox = GeoConverter.CreateBBox(lat, lon, radius);

                Message = "Downloading...";

                var task = Task.Run(async () => await OverpassService.GetBuildingsAsync(bbox));

                if (!task.Wait(TimeSpan.FromSeconds(65)))
                {
                    throw new Exception("Download timeout. Try reducing radius.");
                }

                var buildings = task.Result;

                stopwatch.Stop();

                if (buildings.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No buildings found");
                    DA.SetData(1, "No buildings found in this area");
                    DA.SetData(2, 0);
                    return;
                }

                // Create data collection
                var dataCollection = new BuildingDataCollection
                {
                    Buildings = buildings,
                    OriginLat = lat,
                    OriginLon = lon,
                    BoundingBox = bbox,
                    DownloadTime = DateTime.Now
                };

                cachedData = dataCollection;
                lastCacheKey = cacheKey;

                DA.SetData(0, new GH_ObjectWrapper(dataCollection));
                DA.SetData(1, $"✓ Downloaded in {stopwatch.ElapsedMilliseconds}ms\n" + dataCollection.GetSummary());
                DA.SetData(2, buildings.Count);

                Message = $"{buildings.Count} buildings";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(1, $"❌ Error: {ex.Message}");
                DA.SetData(2, 0);
                Message = "Error";
            }
            finally
            {
                isDownloading = false;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("AEDF3B70-2640-41E8-BADA-813A9080BD13");
    }
}