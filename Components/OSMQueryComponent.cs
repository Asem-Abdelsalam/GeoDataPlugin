using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Components
{
    public class OSMQueryComponent : GH_Component
    {
        public OSMQueryComponent()
          : base("OSM Query", "Query",
              "Query OSM dataset by type and filters",
              "GeoData", "Query")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("OSM Dataset", "Data", "Complete OSM dataset", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Data Type", "Type", "Type from Data Type Selector (0-7)", GH_ParamAccess.item, 0);
            pManager.AddTextParameter("Tag Filter", "Tag", "Filter by tag key (optional, e.g., 'building:levels')", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Tag Value", "Value", "Filter by tag value (optional)", GH_ParamAccess.item, "");
            pManager.AddTextParameter("Name Filter", "Name", "Filter by name contains (optional)", GH_ParamAccess.item, "");

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Filtered Features", "Features", "Filtered OSM features", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Query results", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number of features", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            int typeIndex = 0;
            string tagFilter = "";
            string tagValue = "";
            string nameFilter = "";

            if (!DA.GetData(0, ref wrapper)) return;
            if (!DA.GetData(1, ref typeIndex)) return;
            DA.GetData(2, ref tagFilter);
            DA.GetData(3, ref tagValue);
            DA.GetData(4, ref nameFilter);

            var dataset = wrapper.Value as OSMDataset;
            if (dataset == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid OSM dataset. Connect to Universal OSM Download component.");
                return;
            }

            try
            {
                // Get features by type
                var dataType = (OSMDataType)typeIndex;
                var features = dataset.GetFeaturesByType(dataType);

                // Apply tag filter
                if (!string.IsNullOrWhiteSpace(tagFilter))
                {
                    if (!string.IsNullOrWhiteSpace(tagValue))
                    {
                        features = features.Where(f => f.GetTag(tagFilter) == tagValue).ToList();
                    }
                    else
                    {
                        features = features.Where(f => f.HasTag(tagFilter)).ToList();
                    }
                }

                // Apply name filter
                if (!string.IsNullOrWhiteSpace(nameFilter))
                {
                    features = features.Where(f =>
                        f.Name != null &&
                        f.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0
                    ).ToList();
                }

                // Convert to GH_ObjectWrapper list
                var wrappedFeatures = features.Select(f => new GH_ObjectWrapper(f)).ToList();

                DA.SetDataList(0, wrappedFeatures);

                string info = $"Query Results:\n";
                info += $"Type: {dataType}\n";
                info += $"Total in dataset: {dataset.GetFeaturesByType(dataType).Count}\n";
                info += $"After filtering: {features.Count}\n";

                if (!string.IsNullOrWhiteSpace(tagFilter))
                {
                    info += $"Tag filter: {tagFilter}";
                    if (!string.IsNullOrWhiteSpace(tagValue))
                        info += $" = {tagValue}";
                    info += "\n";
                }

                if (!string.IsNullOrWhiteSpace(nameFilter))
                    info += $"Name contains: {nameFilter}\n";

                DA.SetData(1, info);
                DA.SetData(2, features.Count);

                Message = $"{features.Count} {dataType}";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(1, $"Error: {ex.Message}");
                DA.SetData(2, 0);
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("355f61ec-67c6-46f3-9231-2b57a23fa0d0");
    }
}