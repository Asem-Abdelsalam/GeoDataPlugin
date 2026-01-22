using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GeoDataPlugin.Components
{
    public class QuickTestComponent : GH_Component
    {
        public QuickTestComponent()
          : base("Quick Test Buildings", "TestBuildings",
              "Generate sample buildings for testing (no API call needed)",
              "GeoData", "Testing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Count", "N", "Number of sample buildings", GH_ParamAccess.item, 5);
            pManager.AddNumberParameter("Size", "S", "Average building size", GH_ParamAccess.item, 20.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Buildings", "B", "Sample building breps", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int count = 5;
            double size = 20.0;

            if (!DA.GetData(0, ref count)) return;
            if (!DA.GetData(1, ref size)) return;

            if (count < 1 || count > 100)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Count must be between 1 and 100");
                return;
            }

            var breps = new List<Brep>();
            var random = new Random(42); // Fixed seed for consistency

            for (int i = 0; i < count; i++)
            {
                try
                {
                    // Random position in a grid
                    double x = (i % 5) * size * 2.5;
                    double y = (i / 5) * size * 2.5;

                    // Random building dimensions
                    double width = size * (0.7 + random.NextDouble() * 0.6);
                    double depth = size * (0.7 + random.NextDouble() * 0.6);
                    double height = 10 + random.NextDouble() * 30;

                    // Create footprint rectangle
                    var plane = Plane.WorldXY;
                    plane.Origin = new Point3d(x, y, 0);

                    var rect = new Rectangle3d(plane, width, depth);
                    var baseCurve = rect.ToNurbsCurve();

                    // Create base surface
                    var baseBreps = Brep.CreatePlanarBreps(baseCurve, 0.01);
                    if (baseBreps == null || baseBreps.Length == 0) continue;

                    // Extrude upward
                    var surface = Surface.CreateExtrusion(baseCurve, new Vector3d(0, 0, height));
                    if (surface != null)
                    {
                        var building = surface.ToBrep();
                        if (building != null && building.IsValid)
                        {
                            // Cap the top and bottom
                            building = building.CapPlanarHoles(0.01);
                            if (building != null && building.IsValid && building.IsSolid)
                            {
                                breps.Add(building);
                            }
                            else
                            {
                                // Try alternative method
                                var box = new Box(plane, new Interval(0, width), new Interval(0, depth), new Interval(0, height));
                                var boxBrep = box.ToBrep();
                                if (boxBrep != null && boxBrep.IsValid)
                                {
                                    breps.Add(boxBrep);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Skip failed buildings
                    continue;
                }
            }

            if (breps.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Failed to generate buildings");
            }

            DA.SetDataList(0, breps);
            DA.SetData(1, $"Generated {breps.Count} sample buildings. Use this to test before downloading real data.");
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("C1234567-89AB-CDEF-0123-456789ABCDEF");
    }
}