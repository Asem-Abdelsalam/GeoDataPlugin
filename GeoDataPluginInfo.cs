using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace GeoDataPlugin
{
    public class GeoDataPluginInfo : GH_AssemblyInfo
    {
        public override string Name => "GeoDataPlugin";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Import geospatial data from OpenStreetMap and terrain APIs to create 3D models";

        public override Guid Id => new Guid("c98d0a22-74a0-4f0b-b223-2d1b261524d2");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}