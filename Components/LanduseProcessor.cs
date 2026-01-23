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
    public class LanduseProcessor : GH_Component
    {
        public LanduseProcessor()
          : base("Landuse Processor", "Process Landuse",
              "Convert landuse features to geometry (residential, commercial, industrial, etc.)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Features", "F", "OSM features from Query component", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Create Boundaries", "Bound", "Generate boundary curves", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Surfaces", "Surf", "Generate planar surfaces", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Height", "Z", "Surface height above ground", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundaries", "B", "Landuse boundaries", GH_ParamAccess.list);
            pManager.AddBrepParameter("Surfaces", "S", "Landuse surface breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Area names", GH_ParamAccess.list);
            pManager.AddTextParameter("Types", "T", "Landuse types", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            bool createBoundaries = true, createSurfaces = true, process = true;
            double height = 0.0;

            if (!DA.GetDataList(0, wrappers)) return;
            if (!DA.GetData(1, ref createBoundaries)) return;
            if (!DA.GetData(2, ref createSurfaces)) return;
            if (!DA.GetData(3, ref height)) return;
            if (!DA.GetData(4, ref process)) return;

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
                    if (feature == null || feature.Geometry.Count < 3) continue;

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
                        points.Add(new Point3d(pt.X, pt.Y, height));
                    }

                    // Close polygon
                    if (points.Count > 0 && points[0].DistanceTo(points[points.Count - 1]) > 0.01)
                    {
                        points.Add(points[0]);
                    }

                    if (points.Count < 4) continue;

                    var polyline = new Polyline(points);
                    var curve = polyline.ToNurbsCurve();

                    if (curve == null || !curve.IsValid || !curve.IsClosed) continue;

                    string landuseType = feature.GetTag("landuse") ?? "unknown";

                    if (createBoundaries)
                    {
                        boundaries.Add(curve);
                        names.Add(feature.Name ?? "Unnamed");
                        types.Add(landuseType);
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
                DA.SetDataList(3, types);

                // Group by type for summary
                var typeGroups = new Dictionary<string, int>();
                foreach (var type in types)
                {
                    if (!typeGroups.ContainsKey(type))
                        typeGroups[type] = 0;
                    typeGroups[type]++;
                }

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Total areas: {boundaries.Count}\n";
                info += $"Boundaries: {boundaries.Count}\n";
                info += $"Surfaces: {surfaces.Count}\n";
                info += "Types:\n";
                foreach (var kvp in typeGroups)
                {
                    info += $"  {kvp.Key}: {kvp.Value}\n";
                }

                DA.SetData(4, info);

                Message = $"{boundaries.Count} areas";
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

        public override Guid ComponentGuid => new Guid("cce57e5c-f2ff-4ae6-8c8a-753a8c543782");
    }
}