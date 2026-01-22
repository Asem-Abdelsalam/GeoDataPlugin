using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using Grasshopper.Kernel;
using Rhino.Geometry;
using GeoDataPlugin.Models;
using GeoDataPlugin.Services;
using GeoDataPlugin.Utils;

namespace GeoDataPlugin.Components
{
    public class BuildingsComponent : GH_Component
    {
        private List<Brep> cachedBreps = null;
        private string lastCacheKey = "";
        private bool isProcessing = false;

        public BuildingsComponent()
          : base("OSM Buildings", "Buildings",
              "Download building footprints from OpenStreetMap and extrude to 3D",
              "GeoData", "Import")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Center latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Center longitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Radius in meters (recommended: 100-500m)", GH_ParamAccess.item, 200.0);
            pManager.AddBooleanParameter("Run", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset Cache", "Reset", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Buildings", "B", "Building breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information and status", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "C", "Number of buildings", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 200;
            bool run = false, reset = false;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;
            if (!DA.GetData(2, ref radius)) return;
            if (!DA.GetData(3, ref run)) return;
            if (!DA.GetData(4, ref reset)) return;

            // Create cache key from inputs
            string cacheKey = $"{lat:F6}_{lon:F6}_{radius:F1}";

            // Reset cache if requested
            if (reset)
            {
                cachedBreps = null;
                lastCacheKey = "";
                DA.SetData(1, "Cache cleared. Set Run to True to download.");
                DA.SetData(2, 0);
                return;
            }

            // Return cached data if available and inputs haven't changed
            if (cachedBreps != null && cacheKey == lastCacheKey && !run)
            {
                DA.SetDataList(0, cachedBreps);
                DA.SetData(1, $"✓ Using cached data: {cachedBreps.Count} buildings (Set Reset=True to clear cache)");
                DA.SetData(2, cachedBreps.Count);
                return;
            }

            if (!run)
            {
                if (cachedBreps != null)
                {
                    DA.SetDataList(0, cachedBreps);
                    DA.SetData(1, $"✓ Cached: {cachedBreps.Count} buildings. Set Run=True to re-download.");
                    DA.SetData(2, cachedBreps.Count);
                }
                else
                {
                    DA.SetData(1, "Set Run to True to download buildings. Recommended radius: 100-500m");
                    DA.SetData(2, 0);
                }
                return;
            }

            if (isProcessing)
            {
                DA.SetData(1, "Processing... Please wait.");
                return;
            }

            // Validate inputs
            if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid coordinates");
                return;
            }

            if (radius > 2000)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Large radius (>2000m) may be slow. Consider using smaller radius.");
            }

            try
            {
                isProcessing = true;
                var stopwatch = Stopwatch.StartNew();

                // Create bounding box
                var bbox = GeoConverter.CreateBBox(lat, lon, radius);

                // Download buildings
                Message = "Downloading...";
                var downloadStart = Stopwatch.StartNew();

                var task = Task.Run(async () => await OverpassService.GetBuildingsAsync(bbox));

                if (!task.Wait(TimeSpan.FromSeconds(65)))
                {
                    throw new Exception("Download timeout. Try reducing radius or try again later.");
                }

                var buildings = task.Result;
                downloadStart.Stop();

                if (buildings.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No buildings found");
                    DA.SetData(1, $"No buildings found in this area");
                    DA.SetData(2, 0);
                    cachedBreps = new List<Brep>();
                    lastCacheKey = cacheKey;
                    return;
                }

                // Build 3D geometry
                Message = "Building geometry...";
                var geomStart = Stopwatch.StartNew();

                var breps = MeshBuilder.BuildBuildingBreps(buildings, lat, lon);

                geomStart.Stop();
                stopwatch.Stop();

                // Cache results
                cachedBreps = breps;
                lastCacheKey = cacheKey;

                DA.SetDataList(0, breps);
                DA.SetData(1, $"✓ Success! {breps.Count} buildings in {stopwatch.ElapsedMilliseconds}ms " +
                             $"(download: {downloadStart.ElapsedMilliseconds}ms, geometry: {geomStart.ElapsedMilliseconds}ms)");
                DA.SetData(2, breps.Count);

                Message = $"{breps.Count} buildings";

                if (breps.Count < buildings.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"{buildings.Count - breps.Count} buildings had invalid geometry");
                }
            }
            catch (AggregateException aex)
            {
                string errorMsg = aex.InnerException?.Message ?? aex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, errorMsg);
                DA.SetData(1, $"❌ Error: {errorMsg}");
                DA.SetData(2, 0);
                Message = "Error";
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
                isProcessing = false;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("B1234567-89AB-CDEF-0123-456789ABCDEF");
    }
}