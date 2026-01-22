using System;
using Grasshopper.Kernel;

namespace GeoDataPlugin.Components
{
    public class GeoLocationComponent : GH_Component
    {
        public GeoLocationComponent()
          : base("Geo Location", "GeoLoc",
              "Define a geographic location with latitude and longitude",
              "GeoData", "Input")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Latitude (-90 to 90)", GH_ParamAccess.item, 40.7128);
            pManager.AddNumberParameter("Longitude", "Lon", "Longitude (-180 to 180)", GH_ParamAccess.item, -74.0060);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Latitude", "Lat", "Latitude", GH_ParamAccess.item);
            pManager.AddNumberParameter("Longitude", "Lon", "Longitude", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Location info", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double lat = 0, lon = 0;

            if (!DA.GetData(0, ref lat)) return;
            if (!DA.GetData(1, ref lon)) return;

            // Validate coordinates
            if (lat < -90 || lat > 90)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Latitude must be between -90 and 90");
                return;
            }

            if (lon < -180 || lon > 180)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Longitude must be between -180 and 180");
                return;
            }

            DA.SetData(0, lat);
            DA.SetData(1, lon);
            DA.SetData(2, $"Location: {lat:F6}°, {lon:F6}°");
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("A1234567-89AB-CDEF-0123-456789ABCDEF");
    }
}