using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using GeoDataPlugin.Models;
namespace GeoDataPlugin.Utils
{
    public static class MeshBuilder
    {
        // Build terrain mesh from elevation grid
        public static Mesh BuildTerrainMesh(double[,] elevations, double cellSize, Point3d origin)
        {
            int rows = elevations.GetLength(0);
            int cols = elevations.GetLength(1);

            var mesh = new Mesh();

            // Add vertices
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double x = origin.X + j * cellSize;
                    double y = origin.Y + i * cellSize;
                    double z = elevations[i, j];

                    mesh.Vertices.Add(x, y, z);
                }
            }

            // Add faces
            for (int i = 0; i < rows - 1; i++)
            {
                for (int j = 0; j < cols - 1; j++)
                {
                    int v1 = i * cols + j;
                    int v2 = i * cols + (j + 1);
                    int v3 = (i + 1) * cols + (j + 1);
                    int v4 = (i + 1) * cols + j;

                    mesh.Faces.AddFace(v1, v2, v3, v4);
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.Compact();

            return mesh;
        }

        // Build building breps from footprints
        public static List<Brep> BuildBuildingBreps(List<Building> buildings, double originLat, double originLon)
        {
            var breps = new List<Brep>();

            // Process in parallel for speed
            var results = new Brep[buildings.Count];

            System.Threading.Tasks.Parallel.For(0, buildings.Count, i =>
            {
                try
                {
                    results[i] = CreateBuildingBrepFast(buildings[i], originLat, originLon);
                }
                catch
                {
                    results[i] = null;
                }
            });

            // Collect valid results
            foreach (var brep in results)
            {
                if (brep != null && brep.IsValid)
                {
                    breps.Add(brep);
                }
            }

            return breps;
        }        
        private static Brep CreateBuildingBrepFast(Building building, double originLat, double originLon)
        {
            // Convert and clean points
            var points = new List<Point3d>(building.Footprint.Count);

            foreach (var geoPoint in building.Footprint)
            {
                points.Add(GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon));
            }

            if (points.Count < 3) return null;

            // Remove duplicates in one pass
            var cleaned = new List<Point3d>(points.Count) { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].DistanceTo(cleaned[cleaned.Count - 1]) > 0.01)
                {
                    cleaned.Add(points[i]);
                }
            }

            // Ensure closed
            if (cleaned[0].DistanceTo(cleaned[cleaned.Count - 1]) > 0.01)
            {
                cleaned.Add(cleaned[0]);
            }

            if (cleaned.Count < 4) return null;

            double height = building.GetHeight();
            if (height <= 0) height = 10.0;

            // Single fast method: Loft
            try
            {
                var bottomCurve = new Polyline(cleaned).ToNurbsCurve();
                if (bottomCurve == null || !bottomCurve.IsClosed) return null;

                // Create top curve
                var topCurve = (Curve)bottomCurve.Duplicate();
                topCurve.Transform(Transform.Translation(0, 0, height));

                // Loft (fastest method)
                var lofted = Brep.CreateFromLoft(
                    new[] { bottomCurve, topCurve },
                    Point3d.Unset,
                    Point3d.Unset,
                    LoftType.Straight,
                    false
                );

                if (lofted != null && lofted.Length > 0)
                {
                    var brep = lofted[0].CapPlanarHoles(0.01);
                    return brep;
                }
            }
            catch
            {
                // If loft fails, skip (don't waste time on fallbacks)
                return null;
            }

            return null;
        }
    }
}