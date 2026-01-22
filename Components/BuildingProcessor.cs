using System;
using System.Collections.Generic;
using System.Diagnostics;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using GeoDataPlugin.Models;
using GeoDataPlugin.Utils;

namespace GeoDataPlugin.Components
{
    public class BuildingProcessor : GH_Component
    {
        public BuildingProcessor()
          : base("Building Processor", "Process Buildings",
              "Convert raw building data to 3D geometry",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Building Data", "Data", "Raw building data from OSM Download", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Create Breps", "Breps", "Generate Brep geometry (slower, precise)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Meshes", "Meshes", "Generate Mesh geometry (faster, approximate)", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Min Height", "MinH", "Minimum building height (filter)", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Height Scale", "Scale", "Height multiplier", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Building Breps", "B", "Building breps", GH_ParamAccess.list);
            pManager.AddMeshParameter("Building Meshes", "M", "Building meshes", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number processed", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            bool createBreps = true, createMeshes = false, process = true;
            double minHeight = 0, heightScale = 1.0;

            if (!DA.GetData(0, ref wrapper)) return;
            if (!DA.GetData(1, ref createBreps)) return;
            if (!DA.GetData(2, ref createMeshes)) return;
            if (!DA.GetData(3, ref minHeight)) return;
            if (!DA.GetData(4, ref heightScale)) return;
            if (!DA.GetData(5, ref process)) return;

            if (!process)
            {
                DA.SetData(2, "Set Process=True to generate geometry");
                DA.SetData(3, 0);
                return;
            }

            // Extract data
            var dataCollection = wrapper.Value as BuildingDataCollection;
            if (dataCollection == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid building data. Connect to OSM Data Download component.");
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                Message = "Processing...";

                var buildings = dataCollection.Buildings;
                var originLat = dataCollection.OriginLat;
                var originLon = dataCollection.OriginLon;

                // Filter buildings by height
                var filteredBuildings = new List<Building>();
                foreach (var building in buildings)
                {
                    double height = building.GetHeight() * heightScale;
                    if (height >= minHeight)
                    {
                        filteredBuildings.Add(building);
                    }
                }

                List<Brep> breps = null;
                List<Mesh> meshes = null;

                // Create Breps
                if (createBreps)
                {
                    breps = ProcessAsBreps(filteredBuildings, originLat, originLon, heightScale);
                    DA.SetDataList(0, breps);
                }

                // Create Meshes
                if (createMeshes)
                {
                    meshes = ProcessAsMeshes(filteredBuildings, originLat, originLon, heightScale);
                    DA.SetDataList(1, meshes);
                }

                stopwatch.Stop();

                int processedCount = 0;
                if (breps != null) processedCount = breps.Count;
                else if (meshes != null) processedCount = meshes.Count;

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Input: {buildings.Count} buildings\n";
                info += $"Filtered: {filteredBuildings.Count} buildings\n";
                info += $"Output: {processedCount} geometries\n";
                info += $"Height scale: {heightScale:F2}x\n";

                if (filteredBuildings.Count - processedCount > 0)
                {
                    info += $"Skipped: {filteredBuildings.Count - processedCount} (invalid geometry)";
                }

                DA.SetData(2, info);
                DA.SetData(3, processedCount);

                Message = $"{processedCount} buildings";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(2, $"❌ Error: {ex.Message}");
                DA.SetData(3, 0);
                Message = "Error";
            }
        }

        private List<Brep> ProcessAsBreps(List<Building> buildings, double originLat, double originLon, double heightScale)
        {
            // Scale heights
            var scaledBuildings = new List<Building>();
            foreach (var b in buildings)
            {
                var scaled = new Building
                {
                    Id = b.Id,
                    BuildingType = b.BuildingType,
                    Footprint = b.Footprint,
                    Height = b.Height.HasValue ? b.Height.Value * heightScale : (double?)null,
                    Levels = b.Levels
                };
                scaledBuildings.Add(scaled);
            }

            return MeshBuilder.BuildBuildingBreps(scaledBuildings, originLat, originLon);
        }

        private List<Mesh> ProcessAsMeshes(List<Building> buildings, double originLat, double originLon, double heightScale)
        {
            var meshes = new List<Mesh>();

            System.Threading.Tasks.Parallel.For(0, buildings.Count, i =>
            {
                try
                {
                    var building = buildings[i];
                    var mesh = CreateBuildingMesh(building, originLat, originLon, heightScale);
                    if (mesh != null && mesh.IsValid)
                    {
                        lock (meshes)
                        {
                            meshes.Add(mesh);
                        }
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

            // Close if needed
            if (points[0].DistanceTo(points[points.Count - 1]) > 0.01)
            {
                points.Add(points[0]);
            }

            double height = building.GetHeight() * heightScale;
            if (height <= 0) height = 10.0;

            var mesh = new Mesh();

            // Add bottom vertices
            int baseIndex = mesh.Vertices.Count;
            foreach (var pt in points)
            {
                mesh.Vertices.Add(pt);
            }

            // Add top vertices
            int topIndex = mesh.Vertices.Count;
            foreach (var pt in points)
            {
                mesh.Vertices.Add(new Point3d(pt.X, pt.Y, pt.Z + height));
            }

            // Add side faces
            for (int i = 0; i < points.Count - 1; i++)
            {
                int v1 = baseIndex + i;
                int v2 = baseIndex + i + 1;
                int v3 = topIndex + i + 1;
                int v4 = topIndex + i;

                mesh.Faces.AddFace(v1, v2, v3, v4);
            }

            // Add top face (triangulate)
            if (points.Count > 3)
            {
                for (int i = 1; i < points.Count - 2; i++)
                {
                    mesh.Faces.AddFace(topIndex, topIndex + i, topIndex + i + 1);
                }
            }

            // Add bottom face
            if (points.Count > 3)
            {
                for (int i = 1; i < points.Count - 2; i++)
                {
                    mesh.Faces.AddFace(baseIndex, baseIndex + i + 1, baseIndex + i);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();

            return mesh;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("578B9294-F51A-4BDE-9374-A4778A41735C");
    }
}