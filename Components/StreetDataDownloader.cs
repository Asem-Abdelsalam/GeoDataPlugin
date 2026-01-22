using GeoDataPlugin.Models;
using GeoDataPlugin.Services;
using GeoDataPlugin.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GeoDataPlugin.Components
{
    public class StreetDataDownloader : GH_Component
    {
        private StreetDataCollection cachedData = null;
        private string lastCacheKey = "";
        private bool isDownloading = false;

        public StreetDataDownloader()
          : base("OSM Street Download", "Street Download",
              "Download raw street/road data from OpenStreetMap",
              "GeoData", "Data")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Center latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Center longitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Radius in meters", GH_ParamAccess.item, 500.0);
            pManager.AddBooleanParameter("Include Major", "Major", "Include highways/main roads", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Include Residential", "Resid", "Include residential streets", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Include Service", "Serv", "Include service roads", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Download", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Clear Cache", "Clear", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Street Data", "Data", "Raw street data collection", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Info", "Data summary", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number of streets", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 500;
            bool includeMajor = true, includeResidential = true, includeService = false;
            bool download = false, clear = false;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;
            if (!DA.GetData(2, ref radius)) return;
            if (!DA.GetData(3, ref includeMajor)) return;
            if (!DA.GetData(4, ref includeResidential)) return;
            if (!DA.GetData(5, ref includeService)) return;
            if (!DA.GetData(6, ref download)) return;
            if (!DA.GetData(7, ref clear)) return;

            string cacheKey = $"{lat:F6}_{lon:F6}_{radius:F1}_{includeMajor}_{includeResidential}_{includeService}";

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
                DA.SetData(1, "✓ Cached Data\n" + cachedData.GetSummary());
                DA.SetData(2, cachedData.Streets.Count);
                return;
            }

            if (!download)
            {
                if (cachedData != null)
                {
                    DA.SetData(0, new GH_ObjectWrapper(cachedData));
                    DA.SetData(1, "✓ Cached\n" + cachedData.GetSummary());
                    DA.SetData(2, cachedData.Streets.Count);
                }
                else
                {
                    DA.SetData(1, "Set Download=True to fetch street data from OpenStreetMap");
                    DA.SetData(2, 0);
                }
                return;
            }

            if (isDownloading)
            {
                DA.SetData(1, "Downloading...");
                return;
            }

            try
            {
                isDownloading = true;
                var stopwatch = Stopwatch.StartNew();

                var bbox = GeoConverter.CreateBBox(lat, lon, radius);

                var filter = new StreetFilter
                {
                    IncludeMotorways = includeMajor,
                    IncludeTrunks = includeMajor,
                    IncludePrimary = includeMajor,
                    IncludeSecondary = includeMajor,
                    IncludeTertiary = includeResidential,
                    IncludeResidential = includeResidential,
                    IncludeService = includeService,
                    IncludePedestrian = false,
                    IncludePaths = false
                };

                Message = "Downloading...";

                var task = Task.Run(async () => await StreetsService.GetStreetsAsync(bbox, filter));

                if (!task.Wait(TimeSpan.FromSeconds(65)))
                {
                    throw new Exception("Download timeout. Try reducing radius.");
                }

                var streets = task.Result;

                stopwatch.Stop();

                if (streets.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No streets found");
                    DA.SetData(1, "No streets found in this area");
                    DA.SetData(2, 0);
                    return;
                }

                var dataCollection = new StreetDataCollection
                {
                    Streets = streets,
                    OriginLat = lat,
                    OriginLon = lon,
                    BoundingBox = bbox,
                    DownloadTime = DateTime.Now
                };

                cachedData = dataCollection;
                lastCacheKey = cacheKey;

                DA.SetData(0, new GH_ObjectWrapper(dataCollection));
                DA.SetData(1, $"✓ Downloaded in {stopwatch.ElapsedMilliseconds}ms\n" + dataCollection.GetSummary());
                DA.SetData(2, streets.Count);

                Message = $"{streets.Count} streets";
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

        public override Guid ComponentGuid => new Guid("9B904D25-D1CF-4C4B-A47F-7E460F83A71C");
    }
}