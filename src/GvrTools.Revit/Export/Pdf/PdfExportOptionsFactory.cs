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
                PaperPlacement = ToPaperPlacement(settings.PaperPlacement),

                // Default asks Revit to use the sheet's own size, which is both more accurate and
                // cheaper than measuring the title block and matching a named format.
                PaperFormat = ExportPaperFormat.Default,
                PaperOrientation = orientation
            };

            if (settings.PaperPlacement == PdfPaperPlacement.OffsetFromCorner)
            {
                options.OriginOffsetX = settings.OffsetXInches;
                options.OriginOffsetY = settings.OffsetYInches;
            }

#if REVIT2025_OR_GREATER
            // Background export would return before the file exists, which would break the
            // per-item verification both callers do right after Document.Export.
            options.SetExportInBackground(false);
#endif

            return options;
        }

        public static PaperPlacementType ToPaperPlacement(PdfPaperPlacement placement)
        {
            switch (placement)
            {
                case PdfPaperPlacement.OffsetFromCorner: return PaperPlacementType.LowerLeft;
                case PdfPaperPlacement.PrinterMargin: return PaperPlacementType.Margins;
                default: return PaperPlacementType.Center;
            }
        }

        private static ColorDepthType ToColorDepth(PdfColorMode mode)
        {
            switch (mode)
            {
                case PdfColorMode.GrayScale: return ColorDepthType.GrayScale;
                case PdfColorMode.BlackAndWhite: return ColorDepthType.BlackLine;
                default: return ColorDepthType.Color;
            }
        }

        private static RasterQualityType ToRasterQuality(PdfRasterQuality quality)
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
#endif
