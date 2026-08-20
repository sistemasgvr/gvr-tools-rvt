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
    ///
    /// PrintManager's sub-settings (PrintSetup, ViewSheetSetting) turned out to interfere with each
    /// other across Apply() calls during testing, so each stage below is configured and Applied
    /// independently, in a fixed order, with its own try/catch so a failure names the exact stage
    /// and carries diagnostic context (printer, measured sheet size, matched paper) instead of a
    /// bare Revit exception message.
    /// </summary>
    public sealed class PdfExportService
    {
        public ExportSummary ExportSheets(
            Document doc,
            IList<(ViewSheet Sheet, SheetExportInfo Info)> sheets,
            string destinationFolder,
            string namingPattern,
            PdfExportOptions options,
            Action<ExportProgress> onProgress,
            Func<bool> isCancellationRequested)
        {
            Directory.CreateDirectory(destinationFolder);

            string printerName = string.IsNullOrWhiteSpace(options?.PrinterName)
                ? PdfPrinterLocator.FindPdfPrinterName()
                : options.PrinterName;

            if (printerName == null)
            {
                throw new InvalidOperationException(
                    "No se encontró ninguna impresora PDF instalada (por ejemplo, \"Microsoft Print to PDF\"). " +
                    "Instala una impresora PDF en Windows, o elige una impresora en la lista, e inténtalo de nuevo.");
            }

            PrintManager printManager = doc.PrintManager;
            try
            {
                printManager.SelectNewPrintDriver(printerName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"No se pudo seleccionar la impresora \"{printerName}\": {ex.Message}", ex);
            }

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

                results.Add(ExportOne(doc, printManager, printerName, sheet, info, destinationFolder, namingPattern, options));
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
            string namingPattern,
            PdfExportOptions options)
        {
            string fileName = FileNaming.BuildFileName(namingPattern, info);
            string destPath = FileNaming.GetUniquePath(destinationFolder, fileName);
            string diagnostics = $"impresora: {printerName}";

            try
            {
                diagnostics = ApplyPrintSettings(doc, printManager, printerName, sheet, options);
                printManager.Apply();
            }
            catch (Exception ex)
            {
                return SheetExportResult.Fail(info, $"Error al configurar tamaño/zoom de impresión ({diagnostics}): {ex.Message}");
            }

            try
            {
                using (var sheetSet = new ViewSet())
                {
                    sheetSet.Insert(sheet);

                    // Mirrors Autodesk's own ViewPrinter SDK sample: switch to the in-session set
                    // first, then set Views through the freshly-read CurrentViewSheetSet — not
                    // through a separately-held reference — since assigning CurrentViewSheetSet
                    // resets it. This is applied AFTER the print-parameter stage above and given its
                    // own Apply(), because applying print-parameter changes was observed to silently
                    // clear a view selection made beforehand.
                    ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;
                    viewSheetSetting.CurrentViewSheetSet = viewSheetSetting.InSession;
                    viewSheetSetting.CurrentViewSheetSet.Views = sheetSet;

                    printManager.PrintToFileName = destPath;
                    printManager.Apply();
                }
            }
            catch (Exception ex)
            {
                return SheetExportResult.Fail(info, $"Error al seleccionar la lámina para imprimir ({diagnostics}): {ex.Message}");
            }

            bool success;
            try
            {
                success = printManager.SubmitPrint();
            }
            catch (Exception ex)
            {
                return SheetExportResult.Fail(info, $"Error al enviar la lámina a la impresora ({diagnostics}): {ex.Message}");
            }

            if (!success || !File.Exists(destPath))
                return SheetExportResult.Fail(info, $"Revit no generó el archivo PDF para esta lámina ({diagnostics}).");

            return SheetExportResult.Ok(info, destPath);
        }

        /// <summary>Configures paper size/orientation/zoom/margin for one sheet. Returns a short diagnostic string for error messages.</summary>
        private static string ApplyPrintSettings(Document doc, PrintManager printManager, string printerName, ViewSheet sheet, PdfExportOptions options)
        {
            options = options ?? new PdfExportOptions();

            PrintSetup printSetup = printManager.PrintSetup;
            printSetup.CurrentPrintSetting = printSetup.InSession;
            PrintParameters parameters = printSetup.CurrentPrintSetting.PrintParameters;

            (double widthIn, double heightIn) = SheetSizeReader.GetSheetSizeInches(doc, sheet);
            string sizeText = widthIn > 0 && heightIn > 0
                ? $"{widthIn:0.0} x {heightIn:0.0} in"
                : "tamaño de lámina no detectado";

            PaperSize match = options.MatchSheetSize
                ? PaperSizeMatcher.FindBestMatch(printManager.PaperSizes, printerName, widthIn, heightIn)
                : null;

            bool sizeApplied = false;
            if (match != null)
            {
                try
                {
                    parameters.PaperSize = match;
                    parameters.PageOrientation = widthIn >= heightIn ? PageOrientationType.Landscape : PageOrientationType.Portrait;
                    sizeApplied = true;
                }
                catch (Exception)
                {
                    // Some drivers report paper sizes the API then refuses on assignment; fall back
                    // below rather than let a sizing nicety abort the whole export.
                }
            }

            parameters.ZoomType = options.FitToPage ? ZoomType.FitToPage : ZoomType.Zoom;
            if (!options.FitToPage)
                parameters.Zoom = 100;

            if (options.NoMargin)
            {
                parameters.PaperPlacement = PaperPlacementType.Center;
                parameters.MarginType = MarginType.NoMargin;
            }
            else
            {
                parameters.PaperPlacement = PaperPlacementType.Margins;
                parameters.MarginType = MarginType.PrinterLimit;
            }

            parameters.ColorDepth = ColorDepthType.Color;
            parameters.RasterQuality = RasterQualityType.High;
            parameters.HideCropBoundaries = true;
            parameters.HideScopeBoxes = true;
            parameters.HideUnreferencedViewTags = true;
            parameters.HideReforWorkPlanes = true;
            parameters.MaskCoincidentLines = true;
            parameters.ViewLinksinBlue = false;

            string paperText = sizeApplied ? (match?.Name ?? "?") : (options.MatchSheetSize ? "sin coincidencia" : "detección desactivada");
            return $"impresora: {printerName}, lámina: {sizeText}, papel: {paperText}";
        }
    }
}
