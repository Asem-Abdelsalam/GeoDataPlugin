using GeoDataPlugin.Models;
using GeoDataPlugin.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GeoDataPlugin.Components
{
    public class StreetProcessor : GH_Component
    {
        public StreetProcessor()
          : base("Street Processor", "Process Streets",
              "Convert street features to geometry (centerlines and/or surfaces)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Features", "F", "OSM features from Query component", GH_ParamAccess.list);
            pManager.AddNumberParameter("Origin Lat", "OriginLat", "Origin latitude from Query", GH_ParamAccess.item);
            pManager.AddNumberParameter("Origin Lon", "OriginLon", "Origin longitude from Query", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Create Centerlines", "Lines", "Generate centerline curves", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Surfaces", "Surf", "Generate 3D road surfaces", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Width Scale", "WScale", "Road width multiplier", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Simplify Tolerance", "Tol", "Simplify curves (0 = no simplification)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Centerlines", "C", "Street centerline curves", GH_ParamAccess.list);
            pManager.AddBrepParameter("Surfaces", "S", "Road surface breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Street names", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "Cnt", "Number processed", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            double originLat = 0, originLon = 0;
            bool createCenterlines = true, createSurfaces = false, process = true;
            double widthScale = 1.0, simplifyTol = 0.0;

            if (!DA.GetDataList(0, wrappers)) return;
            if (!DA.GetData(1, ref originLat)) return;
            if (!DA.GetData(2, ref originLon)) return;
            if (!DA.GetData(3, ref createCenterlines)) return;
            if (!DA.GetData(4, ref createSurfaces)) return;
            if (!DA.GetData(5, ref widthScale)) return;
            if (!DA.GetData(6, ref simplifyTol)) return;
            if (!DA.GetData(7, ref process)) return;

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

                // Convert OSMFeatures to Streets
                var streets = new List<Street>();

                foreach (var wrapper in wrappers)
                {
                    var feature = wrapper.Value as OSMFeature;
                    if (feature != null)
                    {
                        var street = Street.FromOSMFeature(feature);
                        streets.Add(street);
                    }
                }

                if (streets.Count == 0)
                {
                    DA.SetData(3, "No street features to process");
                    DA.SetData(4, 0);
                    return;
                }

                var centerlines = new List<Curve>();
                var surfaces = new List<Brep>();
                var names = new List<string>();

                foreach (var street in streets)
                {
                    var points = new List<Point3d>();
                    foreach (var geoPoint in street.Centerline)
                    {
                        points.Add(GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon));
                    }

                    if (points.Count < 2) continue;

                    var polyline = new Polyline(points);
                    var curve = polyline.ToNurbsCurve();

                    if (curve == null || !curve.IsValid) continue;

                    if (simplifyTol > 0)
                    {
                        Curve simplified = curve.Simplify(CurveSimplifyOptions.All, simplifyTol, 0.1);
                        curve = simplified?.ToNurbsCurve();
                    }

                    if (createCenterlines)
                    {
                        centerlines.Add(curve);
                        names.Add(street.Name ?? "Unnamed");
                    }

                    if (createSurfaces)
                    {
                        double width = street.Width * widthScale;
                        var surface = CreateRoadSurface(curve, width);
                        if (surface != null)
                        {
                            surfaces.Add(surface);
                        }
                    }
                }

                stopwatch.Stop();

                DA.SetDataList(0, centerlines);
                DA.SetDataList(1, surfaces);
                DA.SetDataList(2, names);

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Input: {streets.Count} streets\n";
                info += $"Centerlines: {centerlines.Count}\n";
                info += $"Surfaces: {surfaces.Count}\n";
                if (simplifyTol > 0)
                    info += $"Simplified with tolerance: {simplifyTol}\n";

                DA.SetData(3, info);
                DA.SetData(4, centerlines.Count);

                Message = $"{centerlines.Count} streets";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(3, $"❌ Error: {ex.Message}");
                DA.SetData(4, 0);
                Message = "Error";
            }
        }

        private Brep CreateRoadSurface(Curve centerline, double width)
        {
            try
            {
                double halfWidth = width / 2.0;

                var leftCurve = centerline.Offset(Plane.WorldXY, halfWidth, 0.01, CurveOffsetCornerStyle.Sharp);
                var rightCurve = centerline.Offset(Plane.WorldXY, -halfWidth, 0.01, CurveOffsetCornerStyle.Sharp);

                if (leftCurve != null && leftCurve.Length > 0 && rightCurve != null && rightCurve.Length > 0)
                {
                    var loft = Brep.CreateFromLoft(
                        new Curve[] { leftCurve[0], rightCurve[0] },
                        Point3d.Unset,
                        Point3d.Unset,
                        LoftType.Straight,
                        false
                    );

                    if (loft != null && loft.Length > 0)
                    {
                        return loft[0];
                    }
                }
            }
            catch { }

            return null;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("c6c579a5-38e7-4533-8af3-60a93951566e");
    }
}