using System.Collections.Generic;

namespace GeoDataPlugin.Models
{
public partial class GeoBoundingBox
    {
        /// <summary>
        /// Represents a building with geometric footprint, height, and related metadata.
        /// </summary>
        /// <remarks>The Building class provides properties for identifying a building, describing its
        /// footprint as a collection of geographic points, and specifying its height and number of levels. The class
        /// can be used to model buildings in mapping, GIS, or architectural applications. All properties are optional
        /// except for Footprint, which is initialized as an empty list by default.</remarks>
        public class Building
        {
            public string Id { get; set; }
            public List<GeoPoint> Footprint { get; set; }
            public double? Height { get; set; }
            public int? Levels { get; set; }
            public string BuildingType { get; set; }

            public Building()
            {
                Footprint = new List<GeoPoint>();
            }

            public double GetHeight()
            {
                if (Height.HasValue) return Height.Value;
                if (Levels.HasValue) return Levels.Value * 3.5;
                return 10.0;
            }
        }

    }
}
