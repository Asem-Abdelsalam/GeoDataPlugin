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
    public class WaterProcessor : GH_Component
    {
        public WaterProcessor()
          : base("Water Processor", "Process Water",
              "Convert water body features to geometry (rivers, lakes, etc.)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Features", "F", "OSM features from Query component", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Create Boundaries", "Bound", "Generate boundary curves", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Surfaces", "Surf", "Generate water surfaces", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Water Level", "Z", "Water surface height", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Width", "W", "Width for linear waterways (rivers)", GH_ParamAccess.item, 5.0);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundaries", "B", "Water body boundaries", GH_ParamAccess.list);
            pManager.AddBrepParameter("Surfaces", "S", "Water surface breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Water body names", GH_ParamAccess.list);
            pManager.AddTextParameter("Types", "T", "Water types (lake/river/stream)", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            bool createBoundaries = true, createSurfaces = true, process = true;
            double waterLevel = 0.0, rivWidth = 5.0;

            if (!DA.GetDataList(0, wrappers)) return;
            if (!DA.GetData(1, ref createBoundaries)) return;
            if (!DA.GetData(2, ref createSurfaces)) return;
            if (!DA.GetData(3, ref waterLevel)) return;
            if (!DA.GetData(4, ref rivWidth)) return;
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

                var boundaries = new List<Curve>();
                var surfaces = new List<Brep>();
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
                        points.Add(new Point3d(pt.X, pt.Y, waterLevel));
                    }

                    if (points.Count < 2) continue;

                    // Determine if this is a closed water body (lake) or linear (river)
                    bool isClosed = points[0].DistanceTo(points[points.Count - 1]) < 1.0;
                    string waterType = feature.GetTag("waterway") ?? feature.GetTag("natural") ?? "water";

                    if (isClosed || waterType == "water")
                    {
                        // Lake/pond - create closed boundary
                        if (!isClosed) points.Add(points[0]);

                        var polyline = new Polyline(points);
                        var curve = polyline.ToNurbsCurve();

                        if (curve != null && curve.IsValid && curve.IsClosed)
                        {
                            if (createBoundaries)
                            {
                                boundaries.Add(curve);
                            }

                            if (createSurfaces)
                            {
                                var planars = Brep.CreatePlanarBreps(curve, 0.01);
                                if (planars != null && planars.Length > 0)
                                {
                                    surfaces.Add(planars[0]);
                                }
                            }

                            names.Add(feature.Name ?? "Unnamed");
                            types.Add("Lake/Pond");
                        }
                    }
                    else
                    {
                        // River/stream - create centerline and offset
                        var polyline = new Polyline(points);
                        var centerline = polyline.ToNurbsCurve();

                        if (centerline != null && centerline.IsValid)
                        {
                            if (createBoundaries)
                            {
                                boundaries.Add(centerline);
                            }

                            if (createSurfaces)
                            {
                                // Create surface by offsetting
                                double halfWidth = rivWidth / 2.0;
                                var left = centerline.Offset(Plane.WorldXY, halfWidth, 0.01, CurveOffsetCornerStyle.Round);
                                var right = centerline.Offset(Plane.WorldXY, -halfWidth, 0.01, CurveOffsetCornerStyle.Round);

                                if (left != null && left.Length > 0 && right != null && right.Length > 0)
                                {
                                    var loft = Brep.CreateFromLoft(
                                        new Curve[] { left[0], right[0] },
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

                            names.Add(feature.Name ?? "Unnamed");
                            types.Add($"River ({waterType})");
                        }
                    }
                }

                stopwatch.Stop();

                DA.SetDataList(0, boundaries);
                DA.SetDataList(1, surfaces);
                DA.SetDataList(2, names);
                DA.SetDataList(3, types);

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Water bodies: {boundaries.Count}\n";
                info += $"Boundaries: {boundaries.Count}\n";
                info += $"Surfaces: {surfaces.Count}\n";
                info += $"Water level: {waterLevel:F2}m\n";

                DA.SetData(4, info);

                Message = $"{boundaries.Count} water bodies";
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

        public override Guid ComponentGuid => new Guid("0d18e973-3dbd-45ed-97f4-7e51363b59b7");
    }
}