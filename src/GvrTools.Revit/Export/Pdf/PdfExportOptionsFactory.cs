#if REVIT2022_OR_GREATER
using Autodesk.Revit.DB;

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
        public static PDFExportOptions Build(PdfExportSettings settings, string fileName, PageOrientationType orientation)
        {
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
                ZoomPercentage = settings.FitToPage ? 100 : settings.ZoomPercentage,
                PaperPlacement = ToPaperPlacement(settings),

                // Default asks Revit to use the sheet's own size, which is both more accurate and
                // cheaper than measuring the title block and matching a named format.
                PaperFormat = ExportPaperFormat.Default,
                PaperOrientation = orientation
            };

            // LowerLeft es el único PaperPlacement de esta API que acepta un offset numérico -- se
            // usa tanto para "Sin margen" (0,0, al ras de la esquina) como para "Definido por el
            // usuario" (los valores reales). "Límite de impresora" no tiene equivalente por esquina
            // en la API nativa, así que cae a Margins (igual que en Centrado con margen).
            if (options.PaperPlacement == PaperPlacementType.LowerLeft)
            {
                bool userDefined = settings.CornerMarginMode == PdfCornerMarginMode.UserDefined;
                options.OriginOffsetX = userDefined ? settings.OffsetXInches : 0;
                options.OriginOffsetY = userDefined ? settings.OffsetYInches : 0;
            }

#if REVIT2025_OR_GREATER
            // Background export would return before the file exists, which would break the
            // per-item verification both callers do right after Document.Export.
            options.SetExportInBackground(false);
#endif

            return options;
        }

        public static PaperPlacementType ToPaperPlacement(PdfExportSettings settings)
        {
            if (settings.PaperPlacement != PdfPaperPlacement.OffsetFromCorner)
                return PaperPlacementType.Center;

            return settings.CornerMarginMode == PdfCornerMarginMode.PrinterLimit
                ? PaperPlacementType.Margins
                : PaperPlacementType.LowerLeft;
        }
    }
}
#endif
