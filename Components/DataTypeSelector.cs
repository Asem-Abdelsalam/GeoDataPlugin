using System;
using Grasshopper.Kernel;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Components
{
    public class DataTypeSelector : GH_Component
    {
        public DataTypeSelector()
          : base("Data Type Selector", "Type",
              "Select which type of OSM data to extract",
              "GeoData", "Query")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Type", "T", "Data type:\n0=Buildings\n1=Streets\n2=Parks\n3=Water\n4=Railways\n5=Landuse\n6=Amenities\n7=All", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Data Type", "Type", "Selected data type (enum value)", GH_ParamAccess.item);
            pManager.AddTextParameter("Type Name", "Name", "Human-readable type name", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int typeIndex = 0;

            if (!DA.GetData(0, ref typeIndex)) return;

            // Validate range
            if (typeIndex < 0 || typeIndex > 7)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Type must be 0-7");
                return;
            }

            var dataType = (OSMDataType)typeIndex;

            DA.SetData(0, typeIndex);
            DA.SetData(1, dataType.ToString());

            Message = dataType.ToString();
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("29bd57b2-b79d-4f86-9721-2765da75d0bc");
    }
}