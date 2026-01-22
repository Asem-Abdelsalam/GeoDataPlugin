using GeoDataPlugin.Models;
using GeoDataPlugin.Services;
using GeoDataPlugin.Utils;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GeoDataPlugin.Components
{
    public class StreetsComponent : GH_Component
    {
        private List<Curve> cachedCenterlines = null;
        private List<Brep> cachedSurfaces = null;
        private string lastCacheKey = "";
        private bool isProcessing = false;

        public StreetsComponent()
          : base("OSM Streets", "Streets",
              "Download street network from OpenStreetMap",
              "GeoData", "Import")
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
            pManager.AddBooleanParameter("3D Surface", "3D", "Create 3D road surfaces (slower)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Run", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset Cache", "Reset", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Centerlines", "C", "Street centerline curves", GH_ParamAccess.list);
            pManager.AddBrepParameter("Surfaces", "S", "Road surface breps (if 3D enabled)", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number of streets", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 500;
            bool includeMajor = true, includeResidential = true, includeService = false;
            bool create3D = false, run = false, reset = false;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;
            if (!DA.GetData(2, ref radius)) return;
            if (!DA.GetData(3, ref includeMajor)) return;
            if (!DA.GetData(4, ref includeResidential)) return;
            if (!DA.GetData(5, ref includeService)) return;
            if (!DA.GetData(6, ref create3D)) return;
            if (!DA.GetData(7, ref run)) return;
            if (!DA.GetData(8, ref reset)) return;

            string cacheKey = $"{lat:F6}_{lon:F6}_{radius:F1}_{includeMajor}_{includeResidential}_{includeService}_{create3D}";

            if (reset)
            {
                cachedCenterlines = null;
                cachedSurfaces = null;
                lastCacheKey = "";
                DA.SetData(2, "Cache cleared. Set Run to True to download.");
                DA.SetData(3, 0);
                return;
            }

            if (cachedCenterlines != null && cacheKey == lastCacheKey)
            {
                DA.SetDataList(0, cachedCenterlines);
                if (create3D && cachedSurfaces != null) DA.SetDataList(1, cachedSurfaces);
                DA.SetData(2, $"✓ Using cached data: {cachedCenterlines.Count} streets");
                DA.SetData(3, cachedCenterlines.Count);
                return;
            }

            if (!run)
            {
                if (cachedCenterlines != null)
                {
                    DA.SetDataList(0, cachedCenterlines);
                    if (create3D && cachedSurfaces != null) DA.SetDataList(1, cachedSurfaces);
                    DA.SetData(2, $"✓ Cached: {cachedCenterlines.Count} streets. Set Run=True to re-download.");
                    DA.SetData(3, cachedCenterlines.Count);
                }
                else
                {
                    DA.SetData(2, "Set Run to True to download streets");
                    DA.SetData(3, 0);
                }
                return;
            }

            if (isProcessing)
            {
                DA.SetData(2, "Processing...");
                return;
            }

            try
            {
                isProcessing = true;
                var stopwatch = Stopwatch.StartNew();

                var bbox = GeoConverter.CreateBBox(lat, lon, radius);

                // Build filter
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

                if (streets.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No streets found");
                    DA.SetData(2, "No streets found in this area");
                    DA.SetData(3, 0);
                    return;
                }

                Message = "Building geometry...";

                // Create centerline curves
                var centerlines = new List<Curve>();

                foreach (var street in streets)
                {
                    var points = new List<Point3d>();
                    foreach (var geoPoint in street.Centerline)
                    {
                        points.Add(GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, lat, lon));
                    }

                    if (points.Count >= 2)
                    {
                        var polyline = new Polyline(points);
                        var curve = polyline.ToNurbsCurve();
                        if (curve != null && curve.IsValid)
                        {
                            centerlines.Add(curve);
                        }
                    }
                }

                cachedCenterlines = centerlines;

                // Create 3D surfaces if requested
                List<Brep> surfaces = null;
                if (create3D)
                {
                    surfaces = CreateRoadSurfaces(streets, lat, lon);
                    cachedSurfaces = surfaces;
                }

                stopwatch.Stop();
                lastCacheKey = cacheKey;

                DA.SetDataList(0, centerlines);
                if (create3D && surfaces != null) DA.SetDataList(1, surfaces);
                DA.SetData(2, $"✓ Success! {centerlines.Count} streets in {stopwatch.ElapsedMilliseconds}ms" +
                             (create3D ? $"\n{surfaces?.Count ?? 0} road surfaces created" : ""));
                DA.SetData(3, centerlines.Count);

                Message = $"{centerlines.Count} streets";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(2, $"❌ Error: {ex.Message}");
                DA.SetData(3, 0);
                Message = "Error";
            }
            finally
            {
                isProcessing = false;
            }
        }

        private List<Brep> CreateRoadSurfaces(List<Street> streets, double originLat, double originLon)
        {
            var surfaces = new List<Brep>();

            foreach (var street in streets)
            {
                try
                {
                    var points = new List<Point3d>();
                    foreach (var geoPoint in street.Centerline)
                    {
                        points.Add(GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon));
                    }

                    if (points.Count < 2) continue;

                    var centerline = new Polyline(points).ToNurbsCurve();
                    if (centerline == null || !centerline.IsValid) continue;

                    // Create road surface by offsetting centerline
                    double halfWidth = street.Width / 2.0;

                    var leftCurve = centerline.Offset(Plane.WorldXY, halfWidth, 0.01, CurveOffsetCornerStyle.Sharp);
                    var rightCurve = centerline.Offset(Plane.WorldXY, -halfWidth, 0.01, CurveOffsetCornerStyle.Sharp);

                    if (leftCurve != null && leftCurve.Length > 0 && rightCurve != null && rightCurve.Length > 0)
                    {
                        // Create loft between offset curves
                        var loft = Brep.CreateFromLoft(
                            new Curve[] { leftCurve[0], rightCurve[0] },
                            Point3d.Unset,
                            Point3d.Unset,
                            LoftType.Straight,
                            false
                        );

                        if (loft != null && loft.Length > 0)
                        {
                            surfaces.Add(loft[0]);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return surfaces;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("543CBE95-ABEC-4F24-8D9D-CCAE6ECDB9BB");
    }
}