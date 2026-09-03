#if REVIT2022_OR_GREATER
using Autodesk.Revit.DB;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Builds a <see cref="PDFExportOptions"/> from a <see cref="PdfExportSettings"/>, shared by
    /// <see cref="NativePdfExportEngine"/> (one sheet/view per call) and
    /// <see cref="CombinedPdfExportJob"/> (every selected sheet/view in one call) so the two paths
    /// can never drift apart on what a given checkbox actually does.
    /// </summary>
    internal static class PdfExportOptionsFactory
    {
        /// <summary>
        /// Typical non-printable margin (inches) used for "Límite de impresora" on the native API,
        /// which has no <c>MarginType.PrinterLimit</c> under corner placement.
        /// </summary>
        private const double TypicalPrinterMarginInches = 0.25;

        /// <summary>
        /// Fixed paper when "Usar el tamaño de cada lámina" is off — one size for the whole run.
        /// </summary>
        private const ExportPaperFormat FixedPaperWhenNotMatching = ExportPaperFormat.ANSI_D;

        /// <param name="sheetSize">
        /// Measured title-block size for the sheet being exported (or a representative sheet for
        /// combined). Used when a named <see cref="ExportPaperFormat"/> is required so Zoom /
        /// corner placement are not ignored under <c>PaperFormat.Default</c>.
        /// </param>
        public static PDFExportOptions Build(
            PdfExportSettings settings,
            string fileName,
            PageOrientationType orientation,
            SheetSize sheetSize = default)
        {
            ExportPaperFormat paperFormat = ResolvePaperFormat(settings, sheetSize);

            var options = new PDFExportOptions
            {
                // Combine is what makes Revit honour FileName verbatim (with it off, Revit applies
                // its own naming rule and ignores FileName) -- true here regardless of whether this
                // call covers one sheet or the whole set.
                Combine = true,
                FileName = fileName,
                StopOnError = false,
                AlwaysUseRaster = settings.HiddenLineProcessing == PdfHiddenLineProcessing.Raster,
                ReplaceHalftoneWithThinLines = settings.ReplaceHalftoneWithThinLines,
                ViewLinksInBlue = settings.ViewLinksInBlue,
                MaskCoincidentLines = settings.MaskCoincidentLines,
                ColorDepth = PdfExportSettings.ToColorDepth(settings.ColorMode),
                RasterQuality = PdfExportSettings.ToRasterQuality(settings.RasterQuality),
                ExportQuality = PDFExportQualityType.DPI600,
                HideCropBoundaries = settings.HideCropBoundaries,
                HideScopeBoxes = settings.HideScopeBoxes,
                HideUnreferencedViewTags = settings.HideUnreferencedViewTags,
                HideReferencePlane = settings.HideReferencePlanes,
                ZoomType = settings.FitToPage ? ZoomType.FitToPage : ZoomType.Zoom,
                ZoomPercentage = settings.FitToPage ? 100 : ClampZoom(settings.ZoomPercentage),
                PaperPlacement = ToPaperPlacement(settings),
                PaperFormat = paperFormat,
                PaperOrientation = orientation
            };

            // LowerLeft is the only PaperPlacement that accepts OriginOffsetX/Y.
            // Autodesk: with PaperFormat.Default, non-Center placement is unreliable — 
            // ResolvePaperFormat already forces a named format whenever placement is not Center.
            if (options.PaperPlacement == PaperPlacementType.LowerLeft)
            {
                ResolveCornerOffsets(settings, out double ox, out double oy);
                options.OriginOffsetX = ox;
                options.OriginOffsetY = oy;
            }

#if REVIT2025_OR_GREATER
            // Background export would return before the file exists, which would break the
            // per-item verification both callers do right after Document.Export.
            options.SetExportInBackground(false);
#endif

            return options;
        }

        /// <summary>
        /// <c>Default</c> ("use sheet size") only when Fit-to-page + center + match sheet size.
        /// Zoom / corner placement need a named format (else Revit ignores them).
        /// When match-sheet-size is off, every sheet uses the same fixed named paper.
        /// </summary>
        internal static ExportPaperFormat ResolvePaperFormat(PdfExportSettings settings, SheetSize sheetSize)
        {
            if (!settings.MatchSheetSize)
                return FixedPaperWhenNotMatching;

            bool needsNamedPaper =
                !settings.FitToPage
                || settings.PaperPlacement == PdfPaperPlacement.OffsetFromCorner;

            if (!needsNamedPaper)
                return ExportPaperFormat.Default;

            return ExportPaperFormatMatcher.ResolveRequired(sheetSize);
        }

        private static void ResolveCornerOffsets(PdfExportSettings settings, out double x, out double y)
        {
            switch (settings.CornerMarginMode)
            {
                case PdfCornerMarginMode.UserDefined:
                    x = settings.OffsetXInches;
                    y = settings.OffsetYInches;
                    return;
                case PdfCornerMarginMode.PrinterLimit:
                    // Native API has no MarginType.PrinterLimit under LowerLeft; approximate the
                    // usual non-printable margin so the drawing is not flush to the edge.
                    x = TypicalPrinterMarginInches;
                    y = TypicalPrinterMarginInches;
                    return;
                default:
                    x = 0;
                    y = 0;
                    return;
            }
        }

        private static int ClampZoom(int percent) =>
            percent < 1 ? 1 : (percent > 999 ? 999 : percent);

        /// <summary>
        /// Offset-from-corner always uses LowerLeft so X/Y (or the printer-limit approximation)
        /// apply. Mapping PrinterLimit to Margins was wrong: Margins centers in the margin box.
        /// </summary>
        public static PaperPlacementType ToPaperPlacement(PdfExportSettings settings)
        {
            return settings.PaperPlacement == PdfPaperPlacement.OffsetFromCorner
                ? PaperPlacementType.LowerLeft
                : PaperPlacementType.Center;
        }
    }
}
#endif
