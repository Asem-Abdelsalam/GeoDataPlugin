using System;
using Grasshopper.Kernel;
using GeoDataPlugin.Services;

namespace GeoDataPlugin.Components
{
    public class ApiKeyComponent : GH_Component
    {
        public ApiKeyComponent()
          : base("Set API Key", "API Key",
              "Set OpenTopography API key for terrain downloads",
              "GeoData", "Settings")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("API Key", "Key", "Your OpenTopography API key", GH_ParamAccess.item, "demoapikeyot2022");
            pManager.AddBooleanParameter("Apply", "Apply", "Apply the API key", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Status message", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string apiKey = "";
            bool apply = false;

            if (!DA.GetData(0, ref apiKey)) return;
            if (!DA.GetData(1, ref apply)) return;

            if (!apply)
            {
                DA.SetData(0, "Set Apply to True to update API key.\n\nGet a free API key at:\nhttps://opentopography.org/");
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "API key cannot be empty");
                DA.SetData(0, "❌ Error: API key is empty");
                return;
            }

            try
            {
                OpenTopoService.SetApiKey(apiKey);
                DA.SetData(0, $"✓ API key applied successfully!\n\nKey: {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...\n\nYou can now download real terrain data.");
                Message = "Key set";
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(0, $"❌ Error: {ex.Message}");
                Message = "Error";
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("E1234567-89AB-CDEF-0123-456789ABCDEF");
    }
}