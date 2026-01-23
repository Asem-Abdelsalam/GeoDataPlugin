using GeoDataPlugin.Models;
using GeoDataPlugin.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GeoDataPlugin.Components
{
    public class ParksProcessor : GH_Component
    {
        public ParksProcessor()
          : base("Parks Processor", "Process Parks",
              "Convert park/garden features to geometry (boundaries and surfaces)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Features", "F", "OSM features from Query component", GH_ParamAccess.list);
            pManager.AddNumberParameter("Origin Lat", "OriginLat", "Origin latitude from Query", GH_ParamAccess.item);
            pManager.AddNumberParameter("Origin Lon", "OriginLon", "Origin longitude from Query", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Create Boundaries", "Bound", "Generate boundary curves", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Surfaces", "Surf", "Generate planar surfaces", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Offset Height", "H", "Offset surface height above ground", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundaries", "B", "Park boundary curves", GH_ParamAccess.list);
            pManager.AddBrepParameter("Surfaces", "S", "Park surface breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Park names", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "Cnt", "Number processed", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            double originLat = 0, originLon = 0;
            bool createBoundaries = true, createSurfaces = false, process = true;
            double offsetHeight = 0.0;

            if (!DA.GetDataList(0, wrappers)) return;
            if (!DA.GetData(1, ref originLat)) return;
            if (!DA.GetData(2, ref originLon)) return;
            if (!DA.GetData(3, ref createBoundaries)) return;
            if (!DA.GetData(4, ref createSurfaces)) return;
            if (!DA.GetData(5, ref offsetHeight)) return;
            if (!DA.GetData(6, ref process)) return;

            if (!process)
            {
                DA.SetData(3, "Set Process=True to generate geometry");
                DA.SetData(4, 0);
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                Message = "Processing...";

                var boundaries = new List<Curve>();
                var surfaces = new List<Brep>();
                var names = new List<string>();

                foreach (var wrapper in wrappers)
                {
                    var feature = wrapper.Value as OSMFeature;
                    if (feature == null || feature.Geometry.Count < 3) continue;

                    var points = new List<Point3d>();
                    foreach (var geoPoint in feature.Geometry)
                    {
                        var pt = GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon);
                        points.Add(new Point3d(pt.X, pt.Y, offsetHeight));
                    }

                    // Close polygon if needed
                    if (points.Count > 0 && points[0].DistanceTo(points[points.Count - 1]) > 0.01)
                    {
                        points.Add(points[0]);
                    }

                    if (points.Count < 4) continue;

                    var polyline = new Polyline(points);
                    var curve = polyline.ToNurbsCurve();

                    if (curve == null || !curve.IsValid || !curve.IsClosed) continue;

                    if (createBoundaries)
                    {
                        boundaries.Add(curve);
                        names.Add(feature.Name ?? "Unnamed");
                    }

                    if (createSurfaces)
                    {
                        var planars = Brep.CreatePlanarBreps(curve, 0.01);
                        if (planars != null && planars.Length > 0)
                        {
                            surfaces.Add(planars[0]);
                        }
                    }
                }

                stopwatch.Stop();

                DA.SetDataList(0, boundaries);
                DA.SetDataList(1, surfaces);
                DA.SetDataList(2, names);

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Parks/Gardens: {boundaries.Count}\n";
                info += $"Boundaries: {boundaries.Count}\n";
                info += $"Surfaces: {surfaces.Count}\n";
                if (offsetHeight != 0)
                    info += $"Height offset: {offsetHeight:F2}m\n";

                DA.SetData(3, info);
                DA.SetData(4, boundaries.Count);

                Message = $"{boundaries.Count} parks";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(3, $"❌ Error: {ex.Message}");
                DA.SetData(4, 0);
                Message = "Error";
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("62bfe8a6-0fcb-4315-9475-28aed6b4e41b");
    }
}