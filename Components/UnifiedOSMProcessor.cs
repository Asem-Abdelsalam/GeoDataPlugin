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
    /// <summary>
    /// Process all OSM data types (buildings and streets) into 3D geometry
    /// </summary>
    public class UnifiedOSMProcessor : GH_Component
    {
        public UnifiedOSMProcessor()
          : base("OSM Processor", "Process OSM",
              "Convert OSM data to 3D geometry (buildings and streets)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("OSM Data", "Data", "OSM data from downloader or query", GH_ParamAccess.item);

            // Building processing
            pManager.AddBooleanParameter("Process Buildings", "Buildings", "Generate building geometry", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Building Breps", "BBreps", "Generate building Breps", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Building Meshes", "BMeshes", "Generate building Meshes", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Height Scale", "HScale", "Building height multiplier", GH_ParamAccess.item, 1.0);

            // Street processing
            pManager.AddBooleanParameter("Process Streets", "Streets", "Generate street geometry", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Street Centerlines", "SLines", "Generate centerline curves", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Street Surfaces", "SSurf", "Generate 3D road surfaces", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Width Scale", "WScale", "Street width multiplier", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Simplify", "Tol", "Curve simplification tolerance (0 = none)", GH_ParamAccess.item, 0.0);

            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Building outputs
            pManager.AddBrepParameter("Building Breps", "BB", "Building breps", GH_ParamAccess.list);
            pManager.AddMeshParameter("Building Meshes", "BM", "Building meshes", GH_ParamAccess.list);

            // Street outputs
            pManager.AddCurveParameter("Street Centerlines", "SC", "Street centerline curves", GH_ParamAccess.list);
            pManager.AddBrepParameter("Street Surfaces", "SS", "Road surface breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Street Names", "SN", "Street names", GH_ParamAccess.list);

            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Building Count", "B#", "Buildings processed", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Street Count", "S#", "Streets processed", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            bool processBuildings = true, processBuildingBreps = true, processBuildingMeshes = false;
            bool processStreets = false, processStreetLines = true, processStreetSurfaces = false;
            double heightScale = 1.0, widthScale = 1.0, simplifyTol = 0.0;
            bool process = true;

            if (!DA.GetData(0, ref wrapper)) return;
            if (!DA.GetData(1, ref processBuildings)) return;
            if (!DA.GetData(2, ref processBuildingBreps)) return;
            if (!DA.GetData(3, ref processBuildingMeshes)) return;
            if (!DA.GetData(4, ref heightScale)) return;
            if (!DA.GetData(5, ref processStreets)) return;
            if (!DA.GetData(6, ref processStreetLines)) return;
            if (!DA.GetData(7, ref processStreetSurfaces)) return;
            if (!DA.GetData(8, ref widthScale)) return;
            if (!DA.GetData(9, ref simplifyTol)) return;
            if (!DA.GetData(10, ref process)) return;

            if (!process)
            {
                DA.SetData(5, "Set Process=True to generate geometry");
                DA.SetData(6, 0);
                DA.SetData(7, 0);
                return;
            }

            // Try to extract data from wrapper
            OSMDataCollection dataCollection = null;

            if (wrapper.Value is OSMDataCollection osm)
            {
                dataCollection = osm;
            }
            else if (wrapper.Value is BuildingDataCollection bdc)
            {
                dataCollection = new OSMDataCollection
                {
                    Buildings = bdc.Buildings,
                    OriginLat = bdc.OriginLat,
                    OriginLon = bdc.OriginLon,
                    BoundingBox = bdc.BoundingBox,
                    DownloadTime = bdc.DownloadTime
                };
            }
            else if (wrapper.Value is StreetDataCollection sdc)
            {
                dataCollection = new OSMDataCollection
                {
                    Streets = sdc.Streets,
                    OriginLat = sdc.OriginLat,
                    OriginLon = sdc.OriginLon,
                    BoundingBox = sdc.BoundingBox,
                    DownloadTime = sdc.DownloadTime
                };
            }

            if (dataCollection == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid OSM data");
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                Message = "Processing...";

                int buildingCount = 0;
                int streetCount = 0;

                // Process buildings
                if (processBuildings && dataCollection.Buildings.Count > 0)
                {
                    if (processBuildingBreps)
                    {
                        var breps = ProcessBuildingsAsBreps(dataCollection, heightScale);
                        DA.SetDataList(0, breps);
                        buildingCount = breps.Count;
                    }

                    if (processBuildingMeshes)
                    {
                        var meshes = ProcessBuildingsAsMeshes(dataCollection, heightScale);
                        DA.SetDataList(1, meshes);
                        if (buildingCount == 0) buildingCount = meshes.Count;
                    }
                }

                // Process streets
                if (processStreets && dataCollection.Streets.Count > 0)
                {
                    var (centerlines, surfaces, names) = ProcessStreets(
                        dataCollection,
                        processStreetLines,
                        processStreetSurfaces,
                        widthScale,
                        simplifyTol
                    );

                    DA.SetDataList(2, centerlines);
                    DA.SetDataList(3, surfaces);
                    DA.SetDataList(4, names);
                    streetCount = centerlines.Count;
                }

                stopwatch.Stop();

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n\n";

                if (processBuildings)
                {
                    info += $"Buildings:\n";
                    info += $"  Input: {dataCollection.Buildings.Count}\n";
                    info += $"  Output: {buildingCount}\n";
                    info += $"  Height scale: {heightScale:F2}x\n";
                }

                if (processStreets)
                {
                    info += $"\nStreets:\n";
                    info += $"  Input: {dataCollection.Streets.Count}\n";
                    info += $"  Output: {streetCount}\n";
                    info += $"  Width scale: {widthScale:F2}x\n";
                    if (simplifyTol > 0)
                        info += $"  Simplified: {simplifyTol}m tolerance\n";
                }

                DA.SetData(5, info);
                DA.SetData(6, buildingCount);
                DA.SetData(7, streetCount);

                Message = $"{buildingCount}B, {streetCount}S";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(5, $"❌ Error: {ex.Message}");
                DA.SetData(6, 0);
                DA.SetData(7, 0);
                Message = "Error";
            }
        }

        private List<Brep> ProcessBuildingsAsBreps(OSMDataCollection data, double heightScale)
        {
            var scaledBuildings = data.Buildings.Select(b => new Building
            {
                Id = b.Id,
                BuildingType = b.BuildingType,
                Footprint = b.Footprint,
                Height = b.Height.HasValue ? b.Height.Value * heightScale : (double?)null,
                Levels = b.Levels
            }).ToList();

            return MeshBuilder.BuildBuildingBreps(scaledBuildings, data.OriginLat, data.OriginLon);
        }

        private List<Mesh> ProcessBuildingsAsMeshes(OSMDataCollection data, double heightScale)
        {
            var meshes = new List<Mesh>();

            System.Threading.Tasks.Parallel.For(0, data.Buildings.Count, i =>
            {
                try
                {
                    var building = data.Buildings[i];
                    var mesh = CreateBuildingMesh(building, data.OriginLat, data.OriginLon, heightScale);
                    if (mesh != null && mesh.IsValid)
                    {
                        lock (meshes) { meshes.Add(mesh); }
                    }
                }
                catch { }
            });

            return meshes;
        }

        private Mesh CreateBuildingMesh(Building building, double originLat, double originLon, double heightScale)
        {
            var points = new List<Point3d>();
            foreach (var geoPoint in building.Footprint)
            {
                points.Add(GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon));
            }

            if (points.Count < 3) return null;

            if (points[0].DistanceTo(points[points.Count - 1]) > 0.01)
                points.Add(points[0]);

            double height = building.GetHeight() * heightScale;
            if (height <= 0) height = 10.0;

            var mesh = new Mesh();

            int baseIndex = mesh.Vertices.Count;
            foreach (var pt in points) mesh.Vertices.Add(pt);

            int topIndex = mesh.Vertices.Count;
            foreach (var pt in points) mesh.Vertices.Add(new Point3d(pt.X, pt.Y, pt.Z + height));

            for (int i = 0; i < points.Count - 1; i++)
            {
                mesh.Faces.AddFace(baseIndex + i, baseIndex + i + 1, topIndex + i + 1, topIndex + i);
            }

            if (points.Count > 3)
            {
                for (int i = 1; i < points.Count - 2; i++)
                {
                    mesh.Faces.AddFace(topIndex, topIndex + i, topIndex + i + 1);
                    mesh.Faces.AddFace(baseIndex, baseIndex + i + 1, baseIndex + i);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        private (List<Curve>, List<Brep>, List<string>) ProcessStreets(
            OSMDataCollection data,
            bool createCenterlines,
            bool createSurfaces,
            double widthScale,
            double simplifyTol)
        {
            var centerlines = new List<Curve>();
            var surfaces = new List<Brep>();
            var names = new List<string>();

            foreach (var street in data.Streets)
            {
                var points = new List<Point3d>();
                foreach (var geoPoint in street.Centerline)
                {
                    points.Add(GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, data.OriginLat, data.OriginLon));
                }

                if (points.Count < 2) continue;

                var polyline = new Polyline(points);
                var curve = polyline.ToNurbsCurve();

                if (curve == null || !curve.IsValid) continue;

                if (simplifyTol > 0)
                {
                    var simplified = curve.Simplify(CurveSimplifyOptions.All, simplifyTol, 0.1);
                    if (simplified != null) curve = simplified.ToNurbsCurve();
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
                    if (surface != null) surfaces.Add(surface);
                }
            }

            return (centerlines, surfaces, names);
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
                        Point3d.Unset, Point3d.Unset,
                        LoftType.Straight, false
                    );

                    if (loft != null && loft.Length > 0) return loft[0];
                }
            }
            catch { }
            return null;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("C3D4E5F6-A7B8-4C5D-8E7F-9A8B7C6D5E4F");
    }
}