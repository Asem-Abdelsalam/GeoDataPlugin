using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Components
{
    public class BuildingFilter : GH_Component
    {
        public BuildingFilter()
          : base("Building Filter", "Filter Buildings",
              "Filter building data by type, height, or other properties",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Building Data", "Data", "Raw building data", GH_ParamAccess.item);
            pManager.AddTextParameter("Type Filter", "Type", "Filter by building type (comma-separated, leave empty for all)", GH_ParamAccess.item, "");
            pManager.AddNumberParameter("Min Height", "MinH", "Minimum height in meters", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Max Height", "MaxH", "Maximum height in meters (0 = no limit)", GH_ParamAccess.item, 0.0);
            pManager.AddIntegerParameter("Min Levels", "MinL", "Minimum number of levels", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Filtered Data", "Data", "Filtered building data", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Filter results", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number after filtering", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            string typeFilter = "";
            double minHeight = 0, maxHeight = 0;
            int minLevels = 0;

            if (!DA.GetData(0, ref wrapper)) return;
            if (!DA.GetData(1, ref typeFilter)) return;
            if (!DA.GetData(2, ref minHeight)) return;
            if (!DA.GetData(3, ref maxHeight)) return;
            if (!DA.GetData(4, ref minLevels)) return;

            var dataCollection = wrapper.Value as BuildingDataCollection;
            if (dataCollection == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid building data");
                return;
            }

            try
            {
                var filtered = new List<Building>();
                var types = typeFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower()).ToList();

                foreach (var building in dataCollection.Buildings)
                {
                    // Type filter
                    if (types.Count > 0)
                    {
                        string bType = (building.BuildingType ?? "yes").ToLower();
                        if (!types.Any(t => bType.Contains(t)))
                            continue;
                    }

                    // Height filter
                    double height = building.GetHeight();
                    if (height < minHeight) continue;
                    if (maxHeight > 0 && height > maxHeight) continue;

                    // Levels filter
                    if (minLevels > 0 && (!building.Levels.HasValue || building.Levels.Value < minLevels))
                        continue;

                    filtered.Add(building);
                }

                var filteredCollection = new BuildingDataCollection
                {
                    Buildings = filtered,
                    OriginLat = dataCollection.OriginLat,
                    OriginLon = dataCollection.OriginLon,
                    BoundingBox = dataCollection.BoundingBox,
                    DownloadTime = dataCollection.DownloadTime
                };

                DA.SetData(0, new GH_ObjectWrapper(filteredCollection));

                string info = $"Filter Results:\n";
                info += $"Input: {dataCollection.Buildings.Count} buildings\n";
                info += $"Output: {filtered.Count} buildings\n";
                info += $"Removed: {dataCollection.Buildings.Count - filtered.Count}\n";

                if (types.Count > 0)
                    info += $"Type filter: {string.Join(", ", types)}\n";
                if (minHeight > 0)
                    info += $"Min height: {minHeight}m\n";
                if (maxHeight > 0)
                    info += $"Max height: {maxHeight}m\n";
                if (minLevels > 0)
                    info += $"Min levels: {minLevels}\n";

                DA.SetData(1, info);
                DA.SetData(2, filtered.Count);

                Message = $"{filtered.Count}/{dataCollection.Buildings.Count}";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(1, $"Error: {ex.Message}");
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("1F09A8C0-ECA4-476F-8B2C-44EDC8A91379");
    }
}