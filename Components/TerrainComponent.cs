using System;
using System.Threading.Tasks;
using System.Diagnostics;
using Grasshopper.Kernel;
using Rhino.Geometry;
using GeoDataPlugin.Models;
using GeoDataPlugin.Services;
using GeoDataPlugin.Utils;

namespace GeoDataPlugin.Components
{
    public class TerrainComponent : GH_Component
    {
        private Mesh cachedMesh = null;
        private string lastCacheKey = "";
        private bool isProcessing = false;

        public TerrainComponent()
          : base("Terrain Mesh", "Terrain",
              "Download elevation data and create terrain mesh",
              "GeoData", "Import")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Center latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Center longitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "Radius in meters", GH_ParamAccess.item, 1000.0);
            pManager.AddIntegerParameter("Resolution", "Res", "Resolution: 30m or 90m", GH_ParamAccess.item, 30);
            pManager.AddNumberParameter("Z Scale", "Z", "Vertical exaggeration factor", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Run", "Run", "Execute download", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset Cache", "Reset", "Clear cached data", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Terrain", "T", "Terrain mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Information", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Elevation", "Min", "Minimum elevation (m)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Elevation", "Max", "Maximum elevation (m)", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Bounds", "B", "Terrain boundary rectangle", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0, radius = 1000, zScale = 1.0;
            int resolution = 30;
            bool run = false, reset = false;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;
            if (!DA.GetData(2, ref radius)) return;
            if (!DA.GetData(3, ref resolution)) return;
            if (!DA.GetData(4, ref zScale)) return;
            if (!DA.GetData(5, ref run)) return;
            if (!DA.GetData(6, ref reset)) return;

            // Validate resolution
            if (resolution != 30 && resolution != 90)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Resolution must be 30 or 90. Using 30m.");
                resolution = 30;
            }

            string cacheKey = $"{lat:F6}_{lon:F6}_{radius:F1}_{resolution}_{zScale:F2}";

            if (reset)
            {
                cachedMesh = null;
                lastCacheKey = "";
                DA.SetData(1, "Cache cleared. Set Run to True to download.");
                return;
            }

            if (cachedMesh != null && cacheKey == lastCacheKey)
            {
                DA.SetData(0, cachedMesh);
                DA.SetData(1, "✓ Using cached terrain (Set Reset=True to clear)");
                return;
            }

            if (!run)
            {
                if (cachedMesh != null)
                {
                    DA.SetData(0, cachedMesh);
                    DA.SetData(1, "✓ Cached terrain. Set Run=True to re-download.");
                }
                else
                {
                    DA.SetData(1, "Set Run to True to download terrain.\n\nNOTE: OpenTopography requires a free API key.\nGet one at: https://opentopography.org/\nFor now, using demo mode with synthetic data.");
                }
                return;
            }

            if (isProcessing)
            {
                DA.SetData(1, "Processing... Please wait.");
                return;
            }

            if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid coordinates");
                return;
            }

            if (radius > 10000)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Large radius (>10km) may be slow and use lots of memory.");
            }

            try
            {
                isProcessing = true;
                var stopwatch = Stopwatch.StartNew();

                var bbox = GeoConverter.CreateBBox(lat, lon, radius);

                Message = "Downloading...";

                // For MVP: Generate synthetic terrain
                // Users need to add their OpenTopography API key for real data
                var elevationGrid = GenerateSyntheticTerrain(bbox, resolution);

                /* Uncomment this when API key is added:
                var task = Task.Run(async () => await OpenTopoService.GetElevationAsync(bbox, resolution));
                
                if (!task.Wait(TimeSpan.FromSeconds(120)))
                {
                    throw new Exception("Download timeout. Try smaller area.");
                }
                
                var elevationGrid = task.Result;
                */

                Message = "Building mesh...";

                // Apply Z scale
                var scaledElevations = new double[elevationGrid.Rows, elevationGrid.Cols];
                for (int i = 0; i < elevationGrid.Rows; i++)
                {
                    for (int j = 0; j < elevationGrid.Cols; j++)
                    {
                        scaledElevations[i, j] = elevationGrid.Elevations[i, j] * zScale;
                    }
                }

                // Calculate origin to align with buildings
                // Buildings are centered at (0,0), so terrain should be too
                double meshWidth = elevationGrid.Cols * elevationGrid.CellSize;
                double meshHeight = elevationGrid.Rows * elevationGrid.CellSize;

                // Center the terrain mesh at origin
                Point3d origin = new Point3d(-meshWidth / 2.0, -meshHeight / 2.0, 0);

                var mesh = MeshBuilder.BuildTerrainMesh(
                    scaledElevations,
                    elevationGrid.CellSize,
                    origin
                );

                stopwatch.Stop();

                cachedMesh = mesh;
                lastCacheKey = cacheKey;

                DA.SetData(0, mesh);
                DA.SetData(1, $"✓ Terrain created in {stopwatch.ElapsedMilliseconds}ms\n" +
                             $"Grid: {elevationGrid.Rows}×{elevationGrid.Cols} cells\n" +
                             $"Resolution: {resolution}m\n" +
                             $"Area: {meshWidth:F1}m × {meshHeight:F1}m\n" +
                             $"NOTE: Using synthetic data. Add OpenTopography API key for real terrain.");
                DA.SetData(2, elevationGrid.MinElevation * zScale);
                DA.SetData(3, elevationGrid.MaxElevation * zScale);

                // Create boundary rectangle for visualization
                var plane = Plane.WorldXY;
                var bounds = new Rectangle3d(plane, new Interval(-meshWidth / 2, meshWidth / 2), new Interval(-meshHeight / 2, meshHeight / 2));
                DA.SetData(4, bounds);

                Message = $"{elevationGrid.Rows}×{elevationGrid.Cols}";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(1, $"❌ Error: {ex.Message}");
                Message = "Error";
            }
            finally
            {
                isProcessing = false;
            }
        }

        // Generate synthetic terrain for testing without API key
        private ElevationGrid GenerateSyntheticTerrain(GeoBoundingBox bbox, int resolution)
        {
            double widthMeters = bbox.WidthMeters();
            double heightMeters = bbox.HeightMeters();

            int cols = Math.Max(2, (int)(widthMeters / resolution));
            int rows = Math.Max(2, (int)(heightMeters / resolution));

            if (cols > 200) cols = 200;
            if (rows > 200) rows = 200;

            var elevations = new double[rows, cols];

            // Generate realistic-looking terrain using Perlin-like noise
            var random = new Random(42);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double x = i / (double)rows;
                    double y = j / (double)cols;

                    // Multiple octaves for terrain-like appearance
                    double elevation = 0;
                    elevation += Math.Sin(x * Math.PI * 4) * Math.Cos(y * Math.PI * 4) * 30.0;
                    elevation += Math.Sin(x * Math.PI * 8) * Math.Cos(y * Math.PI * 8) * 15.0;
                    elevation += Math.Sin(x * Math.PI * 16) * Math.Cos(y * Math.PI * 16) * 7.0;
                    elevation += random.NextDouble() * 3.0;

                    // Base elevation
                    elevation += 100.0;

                    elevations[i, j] = elevation;
                }
            }

            return new ElevationGrid
            {
                Elevations = elevations,
                Rows = rows,
                Cols = cols,
                CellSize = resolution,
                BoundingBox = bbox
            };
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("D1234567-89AB-CDEF-0123-456789ABCDEF");
    }
}