using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Exports sheets to PDF one at a time via Document.PrintManager, printing through an
    /// installed PDF printer driver (e.g. "Microsoft Print to PDF"). Revit 2021 has no PDF export
    /// API of its own — that only exists from Revit 2022 onward — so this is the one path that
    /// actually works on 2021.
    ///
    /// Repeated real-Revit testing showed PrintRange.Select combined with
    /// ViewSheetSetting.InSession losing the selected sheet by the time SubmitPrint() actually ran,
    /// even when verified present right beforehand — that whole API area proved too flaky to trust.
    /// Instead, each sheet is made Revit's active view and printed with PrintRange.Current, which is
    /// the same "print what's on screen" path Revit's own UI relies on and needs no separate
    /// view-selection object at all.
    ///
    /// Paper/zoom/margin configuration is best-effort and never blocks the export: if it fails for
    /// any reason, the sheet still gets printed with whatever settings the printer currently has,
    /// because producing a PDF matters more than a perfect page size.
    /// </summary>
    public sealed class PdfExportService
    {
        public ExportSummary ExportSheets(
            UIDocument uiDoc,
            IList<(ViewSheet Sheet, SheetExportInfo Info)> sheets,
            string destinationFolder,
            string namingPattern,
            PdfExportOptions options,
            Action<ExportProgress> onProgress,
            Func<bool> isCancellationRequested)
        {
            Document doc = uiDoc.Document;
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

            printManager.PrintRange = PrintRange.Current;
            printManager.CombinedFile = true;
            printManager.PrintToFile = true;

            View originalActiveView = uiDoc.ActiveView;
            var results = new List<SheetExportResult>();
            bool cancelled = false;

            try
            {
                for (int i = 0; i < sheets.Count; i++)
                {
                    if (isCancellationRequested())
                    {
                        cancelled = true;
                        break;
                    }

                    var (sheet, info) = sheets[i];
                    onProgress?.Invoke(new ExportProgress(i + 1, sheets.Count, info));

                    results.Add(ExportOne(uiDoc, printManager, printerName, sheet, info, destinationFolder, namingPattern, options));
                }
            }
            finally
            {
                try
                {
                    if (originalActiveView != null && originalActiveView.IsValidObject)
                        uiDoc.ActiveView = originalActiveView;
                }
                catch { /* restoring the original view is a courtesy, never fatal */ }
            }

            return new ExportSummary(results, cancelled, destinationFolder);
        }

        private static SheetExportResult ExportOne(
            UIDocument uiDoc,
            PrintManager printManager,
            string printerName,
            ViewSheet sheet,
            SheetExportInfo info,
            string destinationFolder,
            string namingPattern,
            PdfExportOptions options)
        {
            string fileName = FileNaming.BuildFileName(namingPattern, info, ".pdf");
            string destPath = FileNaming.GetUniquePath(destinationFolder, fileName);
            string diagnostics = $"impresora: {printerName}";

            try
            {
                uiDoc.ActiveView = sheet;

                try
                {
                    diagnostics = ApplyPrintSettings(uiDoc.Document, printManager, printerName, sheet, options);
                }
                catch (Exception ex)
                {
                    diagnostics += $" [aviso: no se pudo aplicar tamaño/zoom, se usó la config. actual de la impresora: {ex.Message}]";
                }

                printManager.PrintToFileName = destPath;
                printManager.Apply();

                // "Microsoft Print to PDF" pops its own native Save dialog on every job and ignores
                // PrintToFileName, which would otherwise block unattended export. SubmitPrint() below
                // blocks until that dialog is dismissed, so the watcher has to start before it.
                SaveDialogAutomator.WatchAndFillIn(destPath, TimeSpan.FromSeconds(20));

                bool success = printManager.SubmitPrint();

                if (!success || !WaitForFile(destPath, TimeSpan.FromSeconds(5)))
                    return SheetExportResult.Fail(info, $"Revit no generó el archivo PDF para esta lámina ({diagnostics}).");

                return SheetExportResult.Ok(info, destPath);
            }
            catch (Exception ex)
            {
                return SheetExportResult.Fail(info, $"Error al exportar la lámina ({diagnostics}): {ex.Message}");
            }
        }

        /// <summary>The PDF write can finish a moment after SubmitPrint() returns; poll briefly instead of failing immediately.</summary>
        private static bool WaitForFile(string path, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path)) return true;
                System.Threading.Thread.Sleep(200);
            }
            return File.Exists(path);
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

            // MarginType can only be set while PaperPlacement is Margins — Revit throws if you set
            // it under Center, so Center (the "no margin" mode) must leave MarginType untouched.
            if (options.NoMargin)
            {
                parameters.PaperPlacement = PaperPlacementType.Center;
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
