using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Exports sheets to PDF one at a time via Document.PrintManager, printing through an
    /// installed PDF printer driver (e.g. "Microsoft Print to PDF"). Revit 2021 has no PDF export
    /// API of its own — that only exists from Revit 2022 onward — so this is the one path that
    /// actually works on 2021. Each sheet becomes its own single-view "print set" so Revit writes
    /// exactly one PDF, named exactly as requested, with a paper size matched to that sheet.
    /// </summary>
    public sealed class PdfExportService
    {
        public ExportSummary ExportSheets(
            Document doc,
            IList<(ViewSheet Sheet, SheetExportInfo Info)> sheets,
            string destinationFolder,
            string namingPattern,
            Action<ExportProgress> onProgress,
            Func<bool> isCancellationRequested)
        {
            Directory.CreateDirectory(destinationFolder);

            string printerName = PdfPrinterLocator.FindPdfPrinterName();
            if (printerName == null)
            {
                throw new InvalidOperationException(
                    "No se encontró ninguna impresora PDF instalada (por ejemplo, \"Microsoft Print to PDF\"). " +
                    "Instala una impresora PDF en Windows e inténtalo de nuevo.");
            }

            PrintManager printManager = doc.PrintManager;
            printManager.SelectNewPrintDriver(printerName);
            printManager.PrintRange = PrintRange.Select;
            printManager.CombinedFile = true;
            printManager.PrintToFile = true;

            var results = new List<SheetExportResult>();
            bool cancelled = false;

            for (int i = 0; i < sheets.Count; i++)
            {
                if (isCancellationRequested())
                {
                    cancelled = true;
                    break;
                }

                var (sheet, info) = sheets[i];
                onProgress?.Invoke(new ExportProgress(i + 1, sheets.Count, info));

                results.Add(ExportOne(doc, printManager, printerName, sheet, info, destinationFolder, namingPattern));
            }

            return new ExportSummary(results, cancelled, destinationFolder);
        }

        private static SheetExportResult ExportOne(
            Document doc,
            PrintManager printManager,
            string printerName,
            ViewSheet sheet,
            SheetExportInfo info,
            string destinationFolder,
            string namingPattern)
        {
            try
            {
                string fileName = FileNaming.BuildFileName(namingPattern, info);
                string destPath = FileNaming.GetUniquePath(destinationFolder, fileName);

                using (var sheetSet = new ViewSet())
                {
                    sheetSet.Insert(sheet);

                    InSessionViewSheetSet inSessionViews = printManager.ViewSheetSetting.InSession;
                    inSessionViews.Views = sheetSet;
                    printManager.ViewSheetSetting.CurrentViewSheetSet = inSessionViews;

                    ApplyPrintSettings(doc, printManager, printerName, sheet);

                    printManager.PrintToFileName = destPath;
                    printManager.Apply();

                    bool success = printManager.SubmitPrint();

                    return success && File.Exists(destPath)
                        ? SheetExportResult.Ok(info, destPath)
                        : SheetExportResult.Fail(info, "Revit no generó el archivo PDF para esta lámina.");
                }
            }
            catch (Exception ex)
            {
                return SheetExportResult.Fail(info, ex.Message);
            }
        }

        private static void ApplyPrintSettings(Document doc, PrintManager printManager, string printerName, ViewSheet sheet)
        {
            InSessionPrintSetting settings = printManager.PrintSetup.InSession;
            PrintParameters parameters = settings.PrintParameters;

            (double widthIn, double heightIn) = SheetSizeReader.GetSheetSizeInches(doc, sheet);
            PaperSize match = PaperSizeMatcher.FindBestMatch(printManager.PaperSizes, printerName, widthIn, heightIn);

            if (match != null)
            {
                parameters.PaperSize = match;
                parameters.PageOrientation = widthIn >= heightIn ? PageOrientationType.Landscape : PageOrientationType.Portrait;
                parameters.ZoomType = ZoomType.Zoom;
                parameters.Zoom = 100;
            }
            else
            {
                parameters.ZoomType = ZoomType.FitToPage;
            }

            parameters.PaperPlacement = PaperPlacementType.Center;
            parameters.ColorDepth = ColorDepthType.Color;
            parameters.RasterQuality = RasterQualityType.High;
            parameters.HideCropBoundaries = true;
            parameters.HideScopeBoxes = true;
            parameters.HideUnreferencedViewTags = true;
            parameters.HideReforWorkPlanes = true;
            parameters.MaskCoincidentLines = true;
            parameters.ViewLinksinBlue = false;

            printManager.PrintSetup.CurrentPrintSetting = settings;
        }
    }
}
