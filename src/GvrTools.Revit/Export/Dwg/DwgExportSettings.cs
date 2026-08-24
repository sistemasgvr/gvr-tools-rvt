namespace GvrTools.Revit.Export.Dwg
{
    public enum DwgFileVersion
    {
        Default,
        R2007,
        R2010,
        R2013,
        R2018
    }

    /// <summary>DWG options that apply to every sheet of a run.</summary>
    public sealed class DwgExportSettings : IExportFormatSettings
    {
        public ExportFormat Format => ExportFormat.Dwg;

        public DwgFileVersion FileVersion { get; set; } = DwgFileVersion.Default;

        /// <summary>Export each view of the sheet merged into a single model space.</summary>
        public bool MergeViews { get; set; } = true;

        /// <summary>Hide crop boundaries, scope boxes, reference planes and unreferenced view tags.</summary>
        public bool HideHelperGraphics { get; set; } = true;

        /// <summary>Export using shared (survey) coordinates instead of project internal ones.</summary>
        public bool UseSharedCoordinates { get; set; }

        /// <summary>
        /// When true, also drop a PNG next to each .dwg — same base name — using Revit's
        /// ImageExportOptions. Off by default because the extra file is only useful when the DWG
        /// will be shown as a raster preview elsewhere, and doubling the output size surprises
        /// most users.
        /// </summary>
        public bool AlsoExportImage { get; set; }
    }
}
