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
    public class UniversalOSMDownloader : GH_Component
    {
        private OSMDataset cachedData = null;
        private string lastCacheKey = "";
        private bool isDownloading = false;

        public UniversalOSMDownloader()
          : base("Universal OSM Download", "OSM All",
              "Download ALL useful OSM data for a region (buildings, streets, parks, water, etc.)",
              "GeoData", "Data")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Center latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Center longitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Radius in meters", GH_ParamAccess.item, 300.0);
            pManager.AddBooleanParameter("Download", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Clear Cache", "Clear", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("OSM Dataset", "Data", "Complete OSM dataset", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Info", "Dataset summary", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Total Features", "N", "Total number of features", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 300;
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

            if (cachedData != null && cacheKey == lastCacheKey)
            {
                DA.SetData(0, new GH_ObjectWrapper(cachedData));
                DA.SetData(1, "✓ Cached Dataset\n" + cachedData.GetSummary());
                DA.SetData(2, cachedData.Features.Count);
                return;
            }

            if (!download)
            {
                if (cachedData != null)
                {
                    DA.SetData(0, new GH_ObjectWrapper(cachedData));
                    DA.SetData(1, "✓ Cached\n" + cachedData.GetSummary());
                    DA.SetData(2, cachedData.Features.Count);
                }
                else
                {
                    DA.SetData(1, "Set Download=True to fetch ALL OSM data:\n• Buildings\n• Streets\n• Parks\n• Water bodies\n• Railways\n• Land use\n• Amenities");
                    DA.SetData(2, 0);
                }
                return;
            }

            if (isDownloading)
            {
                DA.SetData(1, "Downloading all OSM data... Please wait.");
                return;
            }

            if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid coordinates");
                return;
            }

            if (radius > 1000)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Large radius (>1000m) may timeout. Consider smaller area.");
            }

            try
            {
                isDownloading = true;
                var stopwatch = Stopwatch.StartNew();

                var bbox = GeoConverter.CreateBBox(lat, lon, radius);

                Message = "Downloading...";

                var task = Task.Run(async () => await UniversalOSMService.DownloadAllDataAsync(bbox));

                if (!task.Wait(TimeSpan.FromSeconds(120)))
                {
                    throw new Exception("Download timeout. Try reducing radius.");
                }

                var dataset = task.Result;

                stopwatch.Stop();

                if (dataset.Features.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No data found");
                    DA.SetData(1, "No OSM data found in this area");
                    DA.SetData(2, 0);
                    return;
                }

                cachedData = dataset;
                lastCacheKey = cacheKey;

                DA.SetData(0, new GH_ObjectWrapper(dataset));
                DA.SetData(1, $"✓ Downloaded in {stopwatch.ElapsedMilliseconds}ms\n" + dataset.GetSummary());
                DA.SetData(2, dataset.Features.Count);

                Message = $"{dataset.Features.Count} features";
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

        public override Guid ComponentGuid => new Guid("4AF85A84-B6DD-4446-98B4-47B9F35312E2");
    }
}