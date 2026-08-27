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

        public bool PdfNoMargin { get; set; } = true;

        public PdfColorMode PdfColorMode { get; set; } = PdfColorMode.Color;

        public PdfRasterQuality PdfRasterQuality { get; set; } = PdfRasterQuality.High;

        public PdfHiddenLineProcessing PdfHiddenLineProcessing { get; set; } = PdfHiddenLineProcessing.Vector;

        public bool PdfViewLinksInBlue { get; set; }

        public bool PdfReplaceHalftoneWithThinLines { get; set; }

        public bool PdfMaskCoincidentLines { get; set; } = true;

        // DWG
        public DwgFileVersion DwgFileVersion { get; set; } = DwgFileVersion.Default;

        public bool DwgMergeViews { get; set; } = true;

        public bool DwgSharedCoordinates { get; set; }

        public bool DwgAlsoExportImage { get; set; }
    }
}
