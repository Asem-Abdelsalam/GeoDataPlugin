using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Components
{
    /// <summary>
    /// Query and filter OSM data without re-downloading
    /// </summary>
    public class OSMDataQuery : GH_Component
    {
        public OSMDataQuery()
          : base("OSM Data Query", "Query OSM",
              "Filter and query OSM data by type, height, and other properties",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("OSM Data", "Data", "Raw OSM data collection", GH_ParamAccess.item);

            // Building filters
            pManager.AddTextParameter("Building Types", "BType", "Filter buildings by type (comma-separated, empty for all)", GH_ParamAccess.item, "");
            pManager.AddNumberParameter("Min Height", "MinH", "Minimum building height in meters", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Max Height", "MaxH", "Maximum building height (0 = no limit)", GH_ParamAccess.item, 0.0);
            pManager.AddIntegerParameter("Min Levels", "MinL", "Minimum number of levels", GH_ParamAccess.item, 0);

            // Street filters
            pManager.AddTextParameter("Street Types", "SType", "Filter streets by type (comma-separated, empty for all)", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Major Roads Only", "Major", "Filter only major roads", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Residential Only", "Resid", "Filter only residential streets", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Filtered Data", "Data", "Filtered OSM data", GH_ParamAccess.item);
            pManager.AddGenericParameter("Buildings", "B", "Building data only", GH_ParamAccess.item);
            pManager.AddGenericParameter("Streets", "S", "Street data only", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Filter results", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Building Count", "B#", "Number of buildings", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Street Count", "S#", "Number of streets", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            string buildingTypes = "", streetTypes = "";
            double minHeight = 0, maxHeight = 0;
            int minLevels = 0;
            bool majorRoadsOnly = false, residentialOnly = false;

            if (!DA.GetData(0, ref wrapper)) return;
            DA.GetData(1, ref buildingTypes);
            DA.GetData(2, ref minHeight);
            DA.GetData(3, ref maxHeight);
            DA.GetData(4, ref minLevels);
            DA.GetData(5, ref streetTypes);
            DA.GetData(6, ref majorRoadsOnly);
            DA.GetData(7, ref residentialOnly);

            var dataCollection = wrapper.Value as OSMDataCollection;
            if (dataCollection == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid OSM data. Connect to OSM Data Downloader.");
                return;
            }

            try
            {
                var filtered = new OSMDataCollection
                {
                    OriginLat = dataCollection.OriginLat,
                    OriginLon = dataCollection.OriginLon,
                    BoundingBox = dataCollection.BoundingBox,
                    DownloadTime = dataCollection.DownloadTime
                };

                // Filter buildings
                filtered.Buildings = FilterBuildings(
                    dataCollection.Buildings,
                    buildingTypes,
                    minHeight,
                    maxHeight,
                    minLevels
                );

                // Filter streets
                filtered.Streets = FilterStreets(
                    dataCollection.Streets,
                    streetTypes,
                    majorRoadsOnly,
                    residentialOnly
                );

                // Output
                DA.SetData(0, new GH_ObjectWrapper(filtered));
                DA.SetData(1, new GH_ObjectWrapper(new BuildingDataCollection
                {
                    Buildings = filtered.Buildings,
                    OriginLat = filtered.OriginLat,
                    OriginLon = filtered.OriginLon,
                    BoundingBox = filtered.BoundingBox,
                    DownloadTime = filtered.DownloadTime
                }));
                DA.SetData(2, new GH_ObjectWrapper(new StreetDataCollection
                {
                    Streets = filtered.Streets,
                    OriginLat = filtered.OriginLat,
                    OriginLon = filtered.OriginLon,
                    BoundingBox = filtered.BoundingBox,
                    DownloadTime = filtered.DownloadTime
                }));

                string info = GenerateFilterInfo(dataCollection, filtered, buildingTypes, streetTypes,
                    minHeight, maxHeight, minLevels, majorRoadsOnly, residentialOnly);

                DA.SetData(3, info);
                DA.SetData(4, filtered.Buildings.Count);
                DA.SetData(5, filtered.Streets.Count);

                Message = $"{filtered.Buildings.Count}B, {filtered.Streets.Count}S";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(3, $"Error: {ex.Message}");
            }
        }

        private List<Building> FilterBuildings(
            List<Building> buildings,
            string typeFilter,
            double minHeight,
            double maxHeight,
            int minLevels)
        {
            var filtered = buildings.AsEnumerable();

            // Type filter
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                var types = typeFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower()).ToList();

                filtered = filtered.Where(b =>
                {
                    string bType = (b.BuildingType ?? "yes").ToLower();
                    return types.Any(t => bType.Contains(t));
                });
            }

            // Height filter
            if (minHeight > 0 || maxHeight > 0)
            {
                filtered = filtered.Where(b =>
                {
                    double height = b.GetHeight();
                    bool meetsMin = height >= minHeight;
                    bool meetsMax = maxHeight <= 0 || height <= maxHeight;
                    return meetsMin && meetsMax;
                });
            }

            // Levels filter
            if (minLevels > 0)
            {
                filtered = filtered.Where(b =>
                    b.Levels.HasValue && b.Levels.Value >= minLevels
                );
            }

            return filtered.ToList();
        }

        private List<Street> FilterStreets(
            List<Street> streets,
            string typeFilter,
            bool majorRoadsOnly,
            bool residentialOnly)
        {
            var filtered = streets.AsEnumerable();

            // Type filter
            if (!string.IsNullOrWhiteSpace(typeFilter))
            {
                var types = typeFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower()).ToList();

                filtered = filtered.Where(s =>
                {
                    string sType = (s.Type ?? "unknown").ToLower();
                    return types.Any(t => sType.Contains(t));
                });
            }

            // Major roads filter
            if (majorRoadsOnly)
            {
                filtered = filtered.Where(s =>
                {
                    var type = (s.Type ?? "").ToLower();
                    return type == "motorway" || type == "trunk" ||
                           type == "primary" || type == "secondary";
                });
            }

            // Residential filter
            if (residentialOnly)
            {
                filtered = filtered.Where(s =>
                {
                    var type = (s.Type ?? "").ToLower();
                    return type == "residential" || type == "tertiary";
                });
            }

            return filtered.ToList();
        }

        private string GenerateFilterInfo(
            OSMDataCollection original,
            OSMDataCollection filtered,
            string buildingTypes,
            string streetTypes,
            double minHeight,
            double maxHeight,
            int minLevels,
            bool majorRoadsOnly,
            bool residentialOnly)
        {
            var info = "Filter Results:\n";
            info += "━━━━━━━━━━━━━━━━━━\n\n";

            info += $"Buildings:\n";
            info += $"  Input: {original.Buildings.Count}\n";
            info += $"  Output: {filtered.Buildings.Count}\n";
            info += $"  Removed: {original.Buildings.Count - filtered.Buildings.Count}\n";

            if (!string.IsNullOrWhiteSpace(buildingTypes))
                info += $"  Type filter: {buildingTypes}\n";
            if (minHeight > 0)
                info += $"  Min height: {minHeight}m\n";
            if (maxHeight > 0)
                info += $"  Max height: {maxHeight}m\n";
            if (minLevels > 0)
                info += $"  Min levels: {minLevels}\n";

            info += $"\nStreets:\n";
            info += $"  Input: {original.Streets.Count}\n";
            info += $"  Output: {filtered.Streets.Count}\n";
            info += $"  Removed: {original.Streets.Count - filtered.Streets.Count}\n";

            if (!string.IsNullOrWhiteSpace(streetTypes))
                info += $"  Type filter: {streetTypes}\n";
            if (majorRoadsOnly)
                info += $"  Filter: Major roads only\n";
            if (residentialOnly)
                info += $"  Filter: Residential only\n";

            return info;
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("B2C3D4E5-F6A7-4B5C-8D7E-9F8A7B6C5D4E");
    }
}