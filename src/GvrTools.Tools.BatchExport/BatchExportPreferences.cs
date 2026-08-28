using GvrTools.Revit.Export;
using GvrTools.Revit.Export.Dwg;
using GvrTools.Revit.Export.Pdf;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// What the tool remembers between Revit sessions.
    ///
    /// Deliberately flat scalars only: that is what the settings store persists, and it keeps the
    /// stored file readable and forward compatible (an unknown key is ignored, a missing key falls
    /// back to the default below).
    /// </summary>
    public sealed class BatchExportPreferences
    {
        /// <summary>Key this tool's preferences are stored under.</summary>
        public const string StorageKey = "batch-export";

        public string OutputFolder { get; set; } = string.Empty;

        public string NamingPattern { get; set; } = NamingTokens.DefaultPattern;

        public FormatMode Format { get; set; } = FormatMode.Pdf;

        public bool OpenFolderWhenDone { get; set; } = true;

        // PDF
        // No PdfPrinterName here: the printer is no longer a user choice (see
        // BatchExportViewModel.RequiredPdfPrinterHint) - it is always resolved fresh from whatever
        // is installed, since a stored name could point at a printer that no longer exists.

        public bool PdfMatchSheetSize { get; set; } = true;

        public bool PdfFitToPage { get; set; } = true;

        public int PdfZoomPercentage { get; set; } = 100;

        // Deprecated: reemplazado por PdfPaperPlacementRaw. Se mantiene solo para poder migrar el
        // valor guardado de quien ya lo tenía en false (impresora) la primera vez que se carga un
        // archivo .settings viejo -- ver BatchExportViewModel.ApplyPreferences.
        public bool PdfNoMargin { get; set; } = true;

        /// <summary>
        /// Nombre de un valor de <see cref="PdfPaperPlacement"/>, o vacío si nunca se guardó con
        /// este campo (se migra desde PdfNoMargin esa primera vez -- ver ApplyPreferences).
        ///
        /// String y no el enum directamente: FlatFileSettingsStore persiste por reflexión sobre un
        /// conjunto fijo de tipos soportados (string/bool/int/double/enum) y NO incluye enum
        /// nullable -- un <c>PdfPaperPlacement?</c> aquí se guardaría/leería como si no existiera,
        /// perdiendo el valor elegido en cada reinicio. String vacío como "no configurado" logra el
        /// mismo efecto sin pelear con esa lista de tipos.
        /// </summary>
        public string PdfPaperPlacementRaw { get; set; } = string.Empty;

        public double PdfOffsetXInches { get; set; }

        public double PdfOffsetYInches { get; set; }

        public bool PdfHideCropBoundaries { get; set; } = true;

        public bool PdfHideScopeBoxes { get; set; } = true;

        public bool PdfHideUnreferencedViewTags { get; set; } = true;

        public bool PdfHideReferencePlanes { get; set; } = true;

        public PdfColorMode PdfColorMode { get; set; } = PdfColorMode.Color;

        public PdfRasterQuality PdfRasterQuality { get; set; } = PdfRasterQuality.High;

        public PdfHiddenLineProcessing PdfHiddenLineProcessing { get; set; } = PdfHiddenLineProcessing.Vector;

        public bool PdfViewLinksInBlue { get; set; }

        public bool PdfReplaceHalftoneWithThinLines { get; set; }

        public bool PdfMaskCoincidentLines { get; set; } = true;

        public bool PdfCombineIntoSingleFile { get; set; }

        public string PdfCombinedFileName { get; set; } = "{ProjectTitle}_combinado";

        // DWG
        public DwgFileVersion DwgFileVersion { get; set; } = DwgFileVersion.Default;

        public bool DwgMergeViews { get; set; } = true;

        public bool DwgSharedCoordinates { get; set; }

        public bool DwgAlsoExportImage { get; set; }
    }
}
