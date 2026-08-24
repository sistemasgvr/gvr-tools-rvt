#if !REVIT2022_OR_GREATER
using System;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Revit.Model;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// PDF export for Revit 2021, which has no PDF export API at all (it arrived in 2022). Sheets are
    /// plotted through <see cref="PrintManager"/> to an installed Windows PDF printer.
    ///
    /// The hard part is not printing, it is stopping the driver from asking where to save. There is
    /// no single mechanism for that, so the engine picks one per printer (see
    /// <see cref="IPdfOutputController"/>):
    ///
    ///  - drivers that honour Revit's own output path get it through <c>PrintToFileName</c>;
    ///  - "Adobe PDF" gets the target handed to Acrobat Distiller through its documented registry
    ///    channel, because it ignores Revit's path and would otherwise open its own dialog;
    ///  - printers on the PORTPROMPT: port ("Microsoft Print to PDF", the XPS writer) are refused
    ///    outright, with an explanation.
    ///
    /// That last refusal is deliberate. Those printers make the spooler open a native "Save print
    /// output as" dialog for every sheet and block until it is answered. Answering it from code means
    /// finding the window and simulating keystrokes, which necessarily takes over the foreground and
    /// the keyboard — a hundred-sheet export becomes a hundred windows stealing focus and a computer
    /// nobody can use. Hiding the window instead is worse, not better: the job still blocks, only now
    /// invisibly. So the engine declines and says what to install.
    ///
    /// Sheets are plotted one at a time with <see cref="PrintRange.Current"/> after making each one
    /// the active view. That is the same code path Revit's own Print dialog uses; the alternative,
    /// <see cref="PrintRange.Select"/> with an in-session sheet set, proved to lose its selection
    /// intermittently between configuring and submitting the job.
    /// </summary>
    public sealed class PrintDriverPdfExportEngine : IExportEngine
    {
        public ExportFormat Format => ExportFormat.Pdf;

        public string StrategyDescription =>
            "Ploteo a través de una impresora PDF de Windows (Revit 2021 no incluye API de PDF).";

        public IExportSession BeginSession(ExportRequest request)
        {
            PdfExportSettings settings = request.SettingsAs<PdfExportSettings>();
            PdfPrinter printer = ResolvePrinter(settings.PrinterName);

            return new Session(request, settings, printer);
        }

        /// <summary>Picks the printer to plot through and rejects anything that would interrupt the user.</summary>
        private static PdfPrinter ResolvePrinter(string requestedName)
        {
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                PdfPrinter requested = PdfPrinterCatalog.Find(requestedName);

                if (requested == null)
                    throw new ExportSetupException($"La impresora \"{requestedName}\" ya no está instalada en este equipo.");

                if (requested.Kind == PdfPrinterKind.AlwaysPrompts)
                    throw new ExportSetupException(BuildPromptingPrinterMessage(requested));

                return requested;
            }

            PdfPrinter detected = PdfPrinterCatalog.FindBestUnattended();

            if (detected == null)
                throw new ExportSetupException(BuildNoPrinterMessage());

            return detected;
        }

        private static string BuildPromptingPrinterMessage(PdfPrinter printer)
        {
            var message = new StringBuilder();

            message.AppendLine($"La impresora \"{printer.Name}\" no puede usarse para una exportación masiva.");
            message.AppendLine();
            message.AppendLine("Está conectada al puerto " + printer.Port + ", así que Windows abre su propio cuadro de");
            message.AppendLine("diálogo \"Guardar salida de impresión como\" en cada lámina y se queda esperando. Para");
            message.AppendLine("continuar automáticamente habría que tomar el control del teclado y del primer plano en");
            message.AppendLine("cada lámina, dejando el equipo inutilizable; ocultar la ventana es peor, porque el");
            message.AppendLine("trabajo se queda bloqueado igual pero sin que se vea por qué.");
            message.AppendLine();

            PdfPrinter alternative = PdfPrinterCatalog.FindBestUnattended();
            if (alternative != null)
            {
                message.AppendLine($"Solución: elige la impresora \"{alternative.Name}\", que sí puede escribir sin preguntar.");
            }
            else
            {
                message.AppendLine("Soluciones:");
                message.AppendLine("  1. Instala una impresora PDF que respete la ruta de salida, por ejemplo:");
                message.AppendLine("     " + string.Join(", ", PdfPrinterCatalog.RecommendedPrinters));
                message.AppendLine("  2. O exporta a DWG, que no depende de ninguna impresora.");
                message.AppendLine("  3. O usa Revit 2022 o superior, donde el complemento exporta PDF con la API nativa");
                message.AppendLine("     de Revit y no necesita impresora alguna.");
            }

            return message.ToString();
        }

        private static string BuildNoPrinterMessage()
        {
            var message = new StringBuilder();

            message.AppendLine("No se encontró ninguna impresora PDF apta para exportación desatendida.");
            message.AppendLine();
            message.AppendLine("Revit 2021 no tiene exportador de PDF propio, así que el complemento necesita una");
            message.AppendLine("impresora PDF a la que se le pueda indicar el archivo de destino sin preguntar. Las que");
            message.AppendLine("solo saben preguntar (como \"Microsoft Print to PDF\") no sirven para lotes.");
            message.AppendLine();
            message.AppendLine("Instala alguna de estas y vuelve a intentarlo:");
            message.AppendLine("  " + string.Join(", ", PdfPrinterCatalog.RecommendedPrinters));
            message.AppendLine();
            message.AppendLine("Alternativas: exportar a DWG, o usar Revit 2022 o superior (API nativa de PDF).");

            return message.ToString();
        }

        private sealed class Session : IExportSession
        {
            /// <summary>
            /// How long to wait for the file to appear after the job is submitted. This wait does
            /// block Revit — unavoidable, the print job lives on the API thread — so it is kept
            /// short. A working printer finishes in well under a second; Distiller, which converts in
            /// a separate process, can take a couple. The timeout only runs out when something is
            /// actually wrong.
            /// </summary>
            private static readonly TimeSpan FileWriteTimeout = TimeSpan.FromSeconds(15);

            private readonly UIDocument _uiDocument;
            private readonly Document _document;
            private readonly PrintManager _printManager;
            private readonly PdfExportSettings _settings;
            private readonly PdfPrinter _printer;
            private readonly ExportFileNamer _namer;
            private readonly ILog _log;
            private readonly View _originalActiveView;
            private readonly IPdfOutputController _output;

            internal Session(ExportRequest request, PdfExportSettings settings, PdfPrinter printer)
            {
                _uiDocument = request.UIDocument;
                _document = request.UIDocument.Document;
                _settings = settings;
                _printer = printer;
                _log = request.Log;
                _namer = new ExportFileNamer(
                    request.DestinationFolder,
                    request.NamingPattern,
                    ExportFormatInfo.Extension(ExportFormat.Pdf),
                    request.Project.ToTokens());

                _originalActiveView = _uiDocument.ActiveView;
                _printManager = _document.PrintManager;
                _output = CreateOutputController();

                SelectDriver();

                _log.Info($"Impresora '{_printer.Name}' (puerto {_printer.Port}, driver {_printer.Driver}) " +
                          $"clasificada como {_printer.Kind}; salida por {_output.Description}.");
            }

            public BatchItemResult Export(SheetSnapshot sheet)
            {
                ViewSheet viewSheet = SheetRepository.Resolve(_document, sheet);
                if (viewSheet == null)
                    return BatchItemResult.Failure(sheet.Label, "La lámina ya no existe en el proyecto.");

                string destinationPath = _namer.ReservePath(sheet);
                string appliedSettings = "impresora: " + _printer.Name;

                _uiDocument.ActiveView = viewSheet;

                try
                {
                    appliedSettings = ApplyPrintSettings(viewSheet);
                }
                catch (Exception ex)
                {
                    // Page setup is a nicety; producing the PDF matters more than a perfect size.
                    _log.Warn($"No se pudo configurar el papel para {sheet.Label}: {ex.Message}");
                    appliedSettings += " [se usó la configuración actual de la impresora]";
                }

                // Must happen before Apply/SubmitPrint: this is what stops the driver from asking.
                _output.DirectNextJob(destinationPath);
                _printManager.Apply();

                if (!_printManager.SubmitPrint())
                    return BatchItemResult.Failure(sheet.Label, $"Revit no aceptó el trabajo de impresión ({appliedSettings}).");

                if (!_output.FinalizeJob(destinationPath, FileWriteTimeout))
                    return BatchItemResult.Failure(sheet.Label, _output.DescribeFailure() + $" ({appliedSettings})");

                return BatchItemResult.Success(sheet.Label, destinationPath);
            }

            public void Dispose()
            {
                try
                {
                    _output?.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Warn("No se pudo restablecer la salida de la impresora: " + ex.Message);
                }

                try
                {
                    if (_originalActiveView != null && _originalActiveView.IsValidObject)
                        _uiDocument.ActiveView = _originalActiveView;
                }
                catch (Exception)
                {
                    // Restoring the previously active view is a courtesy, never fatal.
                }
            }

            private IPdfOutputController CreateOutputController()
            {
                switch (_printer.Kind)
                {
                    case PdfPrinterKind.AdobeDistiller:
                        return new AdobeDistillerOutput(_log, _printManager);

                    case PdfPrinterKind.Pdf24:
                        return new Pdf24AutoSaveOutput(_log, _printManager, _printer.Name, _printer.Port);

                    case PdfPrinterKind.Unknown:
                        _log.Warn($"La impresora '{_printer.Name}' no está en la lista de impresoras conocidas " +
                                  "(puerto " + _printer.Port + "). Se asume que respeta la ruta de salida; si abre un " +
                                  "cuadro de diálogo, elige otra impresora.");
                        return new RevitPrintToFileOutput(_printManager);

                    default:
                        return new RevitPrintToFileOutput(_printManager);
                }
            }

            private void SelectDriver()
            {
                try
                {
                    _printManager.SelectNewPrintDriver(_printer.Name);
                }
                catch (Exception ex)
                {
                    throw new ExportSetupException(
                        $"No se pudo seleccionar la impresora \"{_printer.Name}\": {ex.Message}", ex);
                }

                _printManager.PrintRange = PrintRange.Current;
                // PrintToFile stays true throughout: Revit rejects false for virtual printers
                // (Adobe PDF, PDF24, most PDF drivers), which is exactly what we always use here.
                _printManager.PrintToFile = true;
                _printManager.CombinedFile = true;
            }

            /// <summary>Configures paper, orientation, zoom and margins for one sheet.</summary>
            private string ApplyPrintSettings(ViewSheet sheet)
            {
                PrintSetup setup = _printManager.PrintSetup;
                setup.CurrentPrintSetting = setup.InSession;
                PrintParameters parameters = setup.CurrentPrintSetting.PrintParameters;

                SheetSize size = SheetSizeReader.Read(_document, sheet);
                PaperSize matched = _settings.MatchSheetSize
                    ? PaperSizeMatcher.FindBestMatch(_printManager.PaperSizes, _printer.Name, size)
                    : null;

                bool sizeApplied = false;
                if (matched != null)
                {
                    try
                    {
                        parameters.PaperSize = matched;
                        parameters.PageOrientation = size.IsLandscape
                            ? PageOrientationType.Landscape
                            : PageOrientationType.Portrait;
                        sizeApplied = true;
                    }
                    catch (Exception)
                    {
                        // Some drivers advertise paper sizes the API then refuses on assignment.
                    }
                }

                parameters.ZoomType = _settings.FitToPage ? ZoomType.FitToPage : ZoomType.Zoom;
                if (!_settings.FitToPage) parameters.Zoom = 100;

                // MarginType may only be assigned while PaperPlacement is Margins; Revit throws if it
                // is touched under Center, so the no-margin mode must leave it alone.
                if (_settings.NoMargin)
                {
                    parameters.PaperPlacement = PaperPlacementType.Center;
                }
                else
                {
                    parameters.PaperPlacement = PaperPlacementType.Margins;
                    parameters.MarginType = MarginType.PrinterLimit;
                }

                parameters.ColorDepth = ToColorDepth(_settings.ColorMode);
                parameters.RasterQuality = ToRasterQuality(_settings.RasterQuality);
                parameters.HideCropBoundaries = _settings.HideHelperGraphics;
                parameters.HideScopeBoxes = _settings.HideHelperGraphics;
                parameters.HideUnreferencedViewTags = _settings.HideHelperGraphics;
                parameters.HideReforWorkPlanes = _settings.HideHelperGraphics;
                parameters.MaskCoincidentLines = true;
                parameters.ViewLinksinBlue = false;

                string paper = sizeApplied
                    ? matched.Name
                    : _settings.MatchSheetSize ? "sin coincidencia" : "detección desactivada";

                return $"impresora: {_printer.Name}, lámina: {size}, papel: {paper}";
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
}
#endif
