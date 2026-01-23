using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using GeoDataPlugin.Models;

namespace GeoDataPlugin.Components
{
    public class OSMDataTypeParameter : GH_ValueList
    {
        public OSMDataTypeParameter()
        {
            Category = "GeoData";
            SubCategory = "Query";
            Name = "OSM Data Type";
            NickName = "Type";
            Description = "Select OSM data type to query (Buildings, Streets, Parks, Water, Railways, Landuse, Amenities)";

            // Use dropdown mode for cleaner interface
            ListMode = GH_ValueListMode.DropDown;

            ListItems.Clear();

            // Add all data types with descriptions
            ListItems.Add(new GH_ValueListItem("Buildings", "0"));
            ListItems.Add(new GH_ValueListItem("Streets", "1"));
            ListItems.Add(new GH_ValueListItem("Parks", "2"));
            ListItems.Add(new GH_ValueListItem("Water", "3"));
            ListItems.Add(new GH_ValueListItem("Railways", "4"));
            ListItems.Add(new GH_ValueListItem("Landuse", "5"));
            ListItems.Add(new GH_ValueListItem("Amenities", "6"));
            ListItems.Add(new GH_ValueListItem("All Types", "7"));

            // Set default to Buildings
            SelectItem(0);
        }

        public override Guid ComponentGuid => new Guid("d441b373-b4fa-4106-ab38-a6f8359f64d2");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override System.Drawing.Bitmap Icon => null;
    }
}