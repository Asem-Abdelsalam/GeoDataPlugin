using GeoDataPlugin.Models;
using GeoDataPlugin.Utils;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GeoDataPlugin.Components
{
    public class AmenitiesProcessor : GH_Component
    {
        public AmenitiesProcessor()
          : base("Amenities Processor", "Process Amenities",
              "Convert amenity features to points/markers (restaurants, shops, transit, etc.)",
              "GeoData", "Process")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Features", "F", "OSM features from Query component", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Create Points", "Pts", "Generate point locations", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Create Markers", "Mark", "Generate vertical marker lines", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Marker Height", "H", "Height of marker lines", GH_ParamAccess.item, 5.0);
            pManager.AddNumberParameter("Base Height", "Z", "Height above ground", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Process", "Run", "Execute processing", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Amenity point locations", GH_ParamAccess.list);
            pManager.AddLineParameter("Markers", "M", "Vertical marker lines", GH_ParamAccess.list);
            pManager.AddTextParameter("Names", "N", "Amenity names", GH_ParamAccess.list);
            pManager.AddTextParameter("Types", "T", "Amenity types", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Processing information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var wrappers = new List<GH_ObjectWrapper>();
            bool createPoints = true, createMarkers = false, process = true;
            double markerHeight = 5.0, baseHeight = 0.0;

            if (!DA.GetDataList(0, wrappers)) return;
            if (!DA.GetData(1, ref createPoints)) return;
            if (!DA.GetData(2, ref createMarkers)) return;
            if (!DA.GetData(3, ref markerHeight)) return;
            if (!DA.GetData(4, ref baseHeight)) return;
            if (!DA.GetData(5, ref process)) return;

            if (!process)
            {
                DA.SetData(4, "Set Process=True to generate geometry");
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                Message = "Processing...";

                double originLat = 0, originLon = 0;
                bool firstFeature = true;

                var points = new List<Point3d>();
                var markers = new List<Line>();
                var names = new List<string>();
                var types = new List<string>();

                foreach (var wrapper in wrappers)
                {
                    var feature = wrapper.Value as OSMFeature;
                    if (feature == null || feature.Geometry.Count == 0) continue;

                    if (firstFeature)
                    {
                        originLat = feature.Geometry[0].Lat;
                        originLon = feature.Geometry[0].Lon;
                        firstFeature = false;
                    }

                    // Use centroid for polygonal amenities, first point for point amenities
                    Point3d location;

                    if (feature.Geometry.Count == 1)
                    {
                        // Point amenity
                        var pt = GeoConverter.GeoToLocal(feature.Geometry[0].Lat, feature.Geometry[0].Lon, originLat, originLon);
                        location = new Point3d(pt.X, pt.Y, baseHeight);
                    }
                    else
                    {
                        // Polygonal amenity - calculate centroid
                        var polyPoints = new List<Point3d>();
                        foreach (var geoPoint in feature.Geometry)
                        {
                            var pt = GeoConverter.GeoToLocal(geoPoint.Lat, geoPoint.Lon, originLat, originLon);
                            polyPoints.Add(pt);
                        }

                        // Simple centroid calculation
                        double sumX = 0, sumY = 0;
                        foreach (var pt in polyPoints)
                        {
                            sumX += pt.X;
                            sumY += pt.Y;
                        }
                        location = new Point3d(sumX / polyPoints.Count, sumY / polyPoints.Count, baseHeight);
                    }

                    string amenityType = feature.GetTag("amenity") ?? "unknown";

                    if (createPoints)
                    {
                        points.Add(location);
                        names.Add(feature.Name ?? "Unnamed");
                        types.Add(amenityType);
                    }

                    if (createMarkers)
                    {
                        var markerLine = new Line(
                            location,
                            new Point3d(location.X, location.Y, location.Z + markerHeight)
                        );
                        markers.Add(markerLine);
                    }
                }

                stopwatch.Stop();

                DA.SetDataList(0, points);
                DA.SetDataList(1, markers);
                DA.SetDataList(2, names);
                DA.SetDataList(3, types);

                // Group by type
                var typeGroups = new Dictionary<string, int>();
                foreach (var type in types)
                {
                    if (!typeGroups.ContainsKey(type))
                        typeGroups[type] = 0;
                    typeGroups[type]++;
                }

                string info = $"✓ Processed in {stopwatch.ElapsedMilliseconds}ms\n";
                info += $"Total amenities: {points.Count}\n";
                info += $"Points: {points.Count}\n";
                info += $"Markers: {markers.Count}\n";
                info += "Types:\n";
                foreach (var kvp in typeGroups)
                {
                    info += $"  {kvp.Key}: {kvp.Value}\n";
                }

                DA.SetData(4, info);

                Message = $"{points.Count} amenities";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(4, $"❌ Error: {ex.Message}");
                Message = "Error";
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("26c66b76-824d-406d-a21b-1991a8f6a8df");
    }
}