using System;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace GeoDataPlugin.Components
{
    public class AlignmentHelperComponent : GH_Component
    {
        public AlignmentHelperComponent()
          : base("Alignment Helper", "Align",
              "Visualize coordinate origin and alignment reference",
              "GeoData", "Utilities")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Radius", "R", "Reference radius in meters", GH_ParamAccess.item, 500.0);
            pManager.AddBooleanParameter("Show Grid", "Grid", "Show reference grid", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Coordinate origin (0,0,0)", GH_ParamAccess.item);
            pManager.AddCircleParameter("Reference Circle", "C", "Reference radius circle", GH_ParamAccess.item);
            pManager.AddLineParameter("Grid Lines", "G", "Reference grid lines", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Alignment information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double radius = 500;
            bool showGrid = true;

            if (!DA.GetData(0, ref radius)) return;
            if (!DA.GetData(1, ref showGrid)) return;

            // Origin point
            var origin = Point3d.Origin;
            DA.SetData(0, origin);

            // Reference circle
            var circle = new Circle(Plane.WorldXY, radius);
            DA.SetData(1, circle);

            // Grid lines
            var gridLines = new System.Collections.Generic.List<Line>();

            if (showGrid)
            {
                double step = radius / 5.0; // 5 divisions

                // X-axis lines
                for (double y = -radius; y <= radius; y += step)
                {
                    var p1 = new Point3d(-radius, y, 0);
                    var p2 = new Point3d(radius, y, 0);
                    gridLines.Add(new Line(p1, p2));
                }

                // Y-axis lines
                for (double x = -radius; x <= radius; x += step)
                {
                    var p1 = new Point3d(x, -radius, 0);
                    var p2 = new Point3d(x, radius, 0);
                    gridLines.Add(new Line(p1, p2));
                }

                // Main axes (thicker - can be visualized differently)
                gridLines.Add(new Line(new Point3d(-radius, 0, 0), new Point3d(radius, 0, 0)));
                gridLines.Add(new Line(new Point3d(0, -radius, 0), new Point3d(0, radius, 0)));
            }

            DA.SetDataList(2, gridLines);

            DA.SetData(3, $"Coordinate System:\n" +
                         $"• Origin: (0, 0, 0)\n" +
                         $"• Buildings are positioned relative to search center\n" +
                         $"• Terrain mesh is centered at origin\n" +
                         $"• Both use same local coordinate system\n" +
                         $"• Reference radius: {radius:F1}m");
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("F1234567-89AB-CDEF-0123-456789ABCDEF");
    }
}