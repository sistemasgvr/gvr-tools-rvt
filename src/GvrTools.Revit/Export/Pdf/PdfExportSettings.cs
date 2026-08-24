namespace GvrTools.Revit.Export.Pdf
{
    public enum PdfColorMode
    {
        Color,
        GrayScale,
        BlackAndWhite
    }

    public enum PdfRasterQuality
    {
        Low,
        Medium,
        High,
        Presentation
    }

    /// <summary>
    /// PDF plot options that apply to every sheet of a run.
    ///
    /// One settings class for both PDF engines on purpose: the native API and the printer-driver
    /// fallback expose the same choices to the user, so the window (and the saved preferences) do
    /// not change shape with the Revit version.
    /// </summary>
    public sealed class PdfExportSettings : IExportFormatSettings
    {
        public ExportFormat Format => ExportFormat.Pdf;

        /// <summary>Use each sheet's own paper size instead of one fixed size for the whole set.</summary>
        public bool MatchSheetSize { get; set; } = true;

        /// <summary>Scale the drawing to fill the page; when false it plots at true 100%.</summary>
        public bool FitToPage { get; set; } = true;

        /// <summary>Centre the drawing with no margin; when false the printer's minimum margin is used.</summary>
        public bool NoMargin { get; set; } = true;

        public PdfColorMode ColorMode { get; set; } = PdfColorMode.Color;

        public PdfRasterQuality RasterQuality { get; set; } = PdfRasterQuality.High;

        /// <summary>Hide crop boundaries, scope boxes, reference planes and unreferenced view tags.</summary>
        public bool HideHelperGraphics { get; set; } = true;

        /// <summary>
        /// Windows printer used only by the Revit 2021 build, which has no PDF export API.
        /// Empty means "detect a suitable PDF printer automatically".
        /// </summary>
        public string PrinterName { get; set; } = string.Empty;
    }
}
