using Autodesk.Revit.DB;

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

    /// <summary>Cómo se resuelven las líneas ocultas al exportar -- "Hidden Line Views" en la API de Revit.</summary>
    public enum PdfHiddenLineProcessing
    {
        Vector,
        Raster
    }

    /// <summary>
    /// Dónde se ubica el dibujo en la hoja -- "Paper Placement" en la API de Revit
    /// (<c>PaperPlacementType</c>). Centrado y Margen de impresora son los dos modos que ya existían
    /// bajo el nombre "Sin margen"; Desde una esquina es nuevo (paridad con ProSheets).
    /// </summary>
    public enum PdfPaperPlacement
    {
        /// <summary>Centra el dibujo en la hoja sin dejar margen.</summary>
        Center,

        /// <summary>Ancla el dibujo a la esquina inferior izquierda, desplazado por <see cref="PdfExportSettings.OffsetXInches"/>/<see cref="PdfExportSettings.OffsetYInches"/>.</summary>
        OffsetFromCorner,

        /// <summary>Respeta el margen mínimo que declara la impresora.</summary>
        PrinterMargin
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

        /// <summary>Scale the drawing to fill the page; when false it plots at <see cref="ZoomPercentage"/>.</summary>
        public bool FitToPage { get; set; } = true;

        /// <summary>Plot scale used when <see cref="FitToPage"/> is false. 100 = true scale.</summary>
        public int ZoomPercentage { get; set; } = 100;

        /// <summary>Where the drawing sits on the sheet.</summary>
        public PdfPaperPlacement PaperPlacement { get; set; } = PdfPaperPlacement.Center;

        /// <summary>
        /// Offset from the lower-left corner. Only used when <see cref="PaperPlacement"/> is
        /// OffsetFromCorner. In inches -- per the Revit API contract for both consumers of this
        /// value: <c>PDFExportOptions.OriginOffsetX/Y</c> and <c>PrintParameters.UserDefinedMarginX/Y</c>
        /// are paper-space measurements (like <c>PaperSize</c>), not model-space feet.
        /// </summary>
        public double OffsetXInches { get; set; }

        /// <summary>Offset from the lower-left corner, in inches -- see <see cref="OffsetXInches"/>.</summary>
        public double OffsetYInches { get; set; }

        public PdfColorMode ColorMode { get; set; } = PdfColorMode.Color;

        public PdfRasterQuality RasterQuality { get; set; } = PdfRasterQuality.High;

        /// <summary>
        /// The four "hide helper graphics" toggles, split out for parity with ProSheets (were one
        /// combined, hardcoded-true switch before).
        /// </summary>
        public bool HideCropBoundaries { get; set; } = true;

        public bool HideScopeBoxes { get; set; } = true;

        public bool HideUnreferencedViewTags { get; set; } = true;

        public bool HideReferencePlanes { get; set; } = true;

        /// <summary>Vector (por defecto) resuelve más rápido y con más nitidez; Raster evita artefactos en geometría muy compleja.</summary>
        public PdfHiddenLineProcessing HiddenLineProcessing { get; set; } = PdfHiddenLineProcessing.Vector;

        /// <summary>Muestra los vínculos de vista (view links) resaltados en azul.</summary>
        public bool ViewLinksInBlue { get; set; }

        /// <summary>Reemplaza el medio tono (halftone) por líneas finas -- útil si el PDF se va a imprimir en blanco y negro.</summary>
        public bool ReplaceHalftoneWithThinLines { get; set; }

        /// <summary>Enmascara líneas coincidentes en los bordes de una región recortada.</summary>
        public bool MaskCoincidentLines { get; set; } = true;

        /// <summary>
        /// Windows printer used only by the Revit 2021 build, which has no PDF export API.
        /// Empty means "detect a suitable PDF printer automatically".
        /// </summary>
        public string PrinterName { get; set; } = string.Empty;

        /// <summary>
        /// When true, every selected sheet/view is written into ONE PDF file instead of one file
        /// each. 2022+ only -- see <c>CombinedPdfExportJob</c> for why Revit 2021 does not offer
        /// this. The window is expected to keep this false and hide the control on 2021.
        /// </summary>
        public bool CombineIntoSingleFile { get; set; }

        /// <summary>
        /// Naming pattern for the combined file, expanded against project-level tokens only
        /// (ProjectTitle/ProjectNumber/ProjectName/ClientName/Date) -- a combined file has no single
        /// sheet number/name/revision to fill {SheetNumber} etc. with.
        /// </summary>
        public string CombinedFileName { get; set; } = "{ProjectTitle}_combinado";

        // ColorDepthType/RasterQualityType existen igual en la API de Revit 2021 y 2022+ (a
        // diferencia de PDFExportOptions, que es 2022+ únicamente) -- de ahí que este mapeo viva
        // acá, sin #if, y lo usen tanto PdfExportOptionsFactory (2022+) como
        // PrintDriverPdfExportEngine (2021) en vez de cada uno tener su propia copia idéntica.
        public static ColorDepthType ToColorDepth(PdfColorMode mode)
        {
            switch (mode)
            {
                case PdfColorMode.GrayScale: return ColorDepthType.GrayScale;
                case PdfColorMode.BlackAndWhite: return ColorDepthType.BlackLine;
                default: return ColorDepthType.Color;
            }
        }

        public static RasterQualityType ToRasterQuality(PdfRasterQuality quality)
        {
            switch (quality)
            {
                case PdfRasterQuality.Low: return RasterQualityType.Low;
                case PdfRasterQuality.Medium: return RasterQualityType.Medium;
                case PdfRasterQuality.Presentation: return RasterQualityType.Presentation;
                default: return RasterQualityType.High;
            }
        }
    }
}
