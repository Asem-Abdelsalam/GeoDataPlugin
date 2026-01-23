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
    public class RailwaysProcessor : GH_Component
    {
        public RailwaysProcessor()
          : base("Railways Processor", "Process Railways",
              "Convert railway features to geometry (tracks, stations, etc.)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Features", "F", "OSM features from Query component", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Create Centerlines", "Lines", "Generate track centerlines", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Rails", "Rails", "Generate 3D rail geometry", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Track Height", "Z", "Height above ground", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Gauge", "G", "Track gauge (standard = 1.435m)", GH_ParamAccess.item, 1.435);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Centerlines", "C", "Railway centerlines", GH_ParamAccess.list);
            pManager.AddCurveParameter("Rails", "R", "Individual rail lines (left & right)", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Railway names", GH_ParamAccess.list);
            pManager.AddTextParameter("Types", "T", "Railway types", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            bool createCenterlines = true, createRails = false, process = true;
            double trackHeight = 0.0, gauge = 1.435;

            if (!DA.GetDataList(0, wrappers)) return;
            if (!DA.GetData(1, ref createCenterlines)) return;
            if (!DA.GetData(2, ref createRails)) return;
            if (!DA.GetData(3, ref trackHeight)) return;
            if (!DA.GetData(4, ref gauge)) return;
            if (!DA.GetData(5, ref process)) return;

            if (!process)
            {
                DA.SetData(4, "Set Process=True to generate geometry");
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                Message = "Processing...";

                double originLat = 0, originLon = 0;
                bool firstFeature = true;

                var centerlines = new List<Curve>();
                var rails = new List<Curve>();
                var names = new List<string>();
                var types = new List<string>();

                foreach (var wrapper in wrappers)
                {
                    var feature = wrapper.Value as OSMFeature;
                    if (feature == null || feature.Geometry.Count < 2) continue;

                    if (firstFeature && feature.Geometry.Count > 0)
                    {
                        originLat = feature.Geometry[0].Lat;
                        originLon = feature.Geometry[0].Lon;
                        firstFeature = false;
                    }

                    var points = new List<Point3d>();
                    foreach (var geoPoint in feature.Geometry)
                    {
                        var pt = GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon);
                        points.Add(new Point3d(pt.X, pt.Y, trackHeight));
                    }

                    if (points.Count < 2) continue;

                    var polyline = new Polyline(points);
                    var centerline = polyline.ToNurbsCurve();

                    if (centerline == null || !centerline.IsValid) continue;

                    string railType = feature.GetTag("railway") ?? "rail";

                    if (createCenterlines)
                    {
                        centerlines.Add(centerline);
                        names.Add(feature.Name ?? "Unnamed");
                        types.Add(railType);
                    }

                    if (createRails)
                    {
                        // Create two rails offset from centerline
                        double halfGauge = gauge / 2.0;

                        var leftRail = centerline.Offset(Plane.WorldXY, halfGauge, 0.01, CurveOffsetCornerStyle.Sharp);
                        var rightRail = centerline.Offset(Plane.WorldXY, -halfGauge, 0.01, CurveOffsetCornerStyle.Sharp);

                        if (leftRail != null && leftRail.Length > 0)
                        {
                            rails.Add(leftRail[0]);
                        }

                        if (rightRail != null && rightRail.Length > 0)
                        {
                            rails.Add(rightRail[0]);
                        }
                    }
                }

                stopwatch.Stop();

                DA.SetDataList(0, centerlines);
                DA.SetDataList(1, rails);
                DA.SetDataList(2, names);
                DA.SetDataList(3, types);

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Railway tracks: {centerlines.Count}\n";
                info += $"Individual rails: {rails.Count}\n";
                info += $"Track height: {trackHeight:F2}m\n";
                info += $"Gauge: {gauge:F3}m\n";

                DA.SetData(4, info);

                Message = $"{centerlines.Count} tracks";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(4, $"❌ Error: {ex.Message}");
                Message = "Error";
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("6f382d8a-f706-401b-9f35-c2fcf1b6e5c3");
    }
}