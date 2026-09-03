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

        /// <summary>
        /// Picks the printer to plot through and rejects anything that would interrupt the user.
        /// Internal (not private): <see cref="CombinedPrintDriverPdfExportJob"/> reuses this exact
        /// same resolution + error messaging instead of duplicating it.
        /// </summary>
        internal static PdfPrinter ResolvePrinter(string requestedName)
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

        /// <summary>
        /// Picks the mechanism that stops <paramref name="printer"/> from asking where to save.
        /// Internal (not private, not instance): shared with <see cref="CombinedPrintDriverPdfExportJob"/>
        /// so the two Revit-2021 PDF paths (per-sheet, combined) can never pick different output
        /// strategies for the same printer.
        /// </summary>
        internal static IPdfOutputController CreateOutputController(PdfPrinter printer, ILog log, PrintManager printManager)
        {
            switch (printer.Kind)
            {
                case PdfPrinterKind.AdobeDistiller:
                    return new AdobeDistillerOutput(log, printManager);

                case PdfPrinterKind.Pdf24:
                    return new Pdf24AutoSaveOutput(log, printManager, printer.Name, printer.Port);

                case PdfPrinterKind.Unknown:
                    log.Warn($"La impresora '{printer.Name}' no está en la lista de impresoras conocidas " +
                             "(puerto " + printer.Port + "). Se asume que respeta la ruta de salida; si abre un " +
                             "cuadro de diálogo, elige otra impresora.");
                    return new RevitPrintToFileOutput(printManager);

                default:
                    return new RevitPrintToFileOutput(printManager);
            }
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

                // CreateOutputController() puede devolver un Pdf24AutoSaveOutput cuyo propio
                // constructor YA mutó el registro de Windows (BackupAndSwitch a Handler="autoSave")
                // de forma real e inmediata. Si SelectDriver() lanza después (impresora
                // desinstalada/renombrada entre resolver y seleccionar, spooler caído, etc.), este
                // constructor nunca retorna -- BeginSession() nunca asigna _session, así que
                // End()/_session?.Dispose() en el llamador es un no-op y ese Pdf24AutoSaveOutput
                // queda huérfano, sin nadie que restaure el registro. Se atrapa aquí específicamente
                // para disponer _output antes de relanzar, evitando que la impresora quede
                // permanentemente en modo autoSave.
                try
                {
                    SelectDriver();
                }
                catch
                {
                    try { _output.Dispose(); }
                    catch (Exception disposeEx)
                    {
                        _log.Warn("No se pudo restablecer la salida de la impresora tras un fallo de selección de driver: " + disposeEx.Message);
                    }
                    throw;
                }

                _log.Info($"Impresora '{_printer.Name}' (puerto {_printer.Port}, driver {_printer.Driver}) " +
                          $"clasificada como {_printer.Kind}; salida por {_output.Description}.");
            }

            public BatchItemResult Export(SheetSnapshot sheet)
            {
                View view = SheetRepository.ResolveView(_document, sheet);
                if (view == null)
                {
                    string missing = sheet.Kind == ExportItemKind.View
                        ? "La vista ya no existe en el proyecto."
                        : "La lámina ya no existe en el proyecto.";
                    return BatchItemResult.Failure(sheet.Label, missing);
                }

                string destinationPath = _namer.ReservePath(sheet);
                string appliedSettings = "impresora: " + _printer.Name;

                try
                {
                    _uiDocument.ActiveView = view;
                }
                catch (Exception ex)
                {
                    // A handful of view kinds (e.g. a 3D walkthrough) refuse to become the active
                    // view; a plotted sheet never hits this. Reported as a per-item failure instead
                    // of letting Revit's raw exception bubble up as the message the user sees.
                    return BatchItemResult.Failure(sheet.Label, "Revit no permite activar esta vista para imprimirla: " + ex.Message);
                }

                try
                {
                    appliedSettings = ApplyPrintSettings(view);
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

            private IPdfOutputController CreateOutputController() =>
                PrintDriverPdfExportEngine.CreateOutputController(_printer, _log, _printManager);

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
                // CombinedFile only applies to PrintRange.Select; with Current it is a no-op and
                // some drivers behave oddly if left true, so keep it false on the per-sheet path.
                _printManager.CombinedFile = false;
            }

            /// <summary>Configures paper, orientation, zoom and margins for one sheet or view.</summary>
            private string ApplyPrintSettings(View view)
            {
                PrintSetup setup = _printManager.PrintSetup;
                setup.CurrentPrintSetting = setup.InSession;
                PrintParameters parameters = setup.CurrentPrintSetting.PrintParameters;

                // A standalone view has no title block to measure, so it always reports "unknown"
                // and falls back to whatever paper the printer defaults to (same as a sheet whose
                // size Revit couldn't read).
                SheetSize size = view is ViewSheet sheet ? SheetSizeReader.Read(_document, sheet) : SheetSize.Unknown;
                PaperSize matched = null;
                if (_settings.MatchSheetSize)
                {
                    matched = PaperSizeMatcher.FindBestMatch(_printManager.PaperSizes, _printer.Name, size);
                }
                else
                {
                    // Same idea as native ANSI_D when MatchSheetSize is off: one fixed paper for the run.
                    matched = PaperSizeMatcher.FindBestMatch(
                        _printManager.PaperSizes,
                        _printer.Name,
                        new SheetSize(22.0, 34.0));
                }

                bool sizeApplied = false;
                if (matched != null)
                {
                    try
                    {
                        parameters.PaperSize = matched;
                        // Match on: use sheet orientation. Match off: fixed ANSI D → landscape.
                        bool landscape = _settings.MatchSheetSize
                            ? (size.IsKnown && size.IsLandscape)
                            : true;
                        parameters.PageOrientation = landscape
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
                if (!_settings.FitToPage)
                {
                    int zoom = _settings.ZoomPercentage;
                    if (zoom < 1) zoom = 1;
                    if (zoom > 999) zoom = 999;
                    parameters.Zoom = zoom;
                }

                // MarginType may only be assigned while PaperPlacement is Margins; Revit throws if it
                // is touched under Center, so each branch below only sets what applies to it.
                //
                // Revit 2021's PrintParameters has no LowerLeft placement or OriginOffsetX/Y at all
                // (those only exist on PDFExportOptions, added in 2022), but its MarginType enum
                // already has exactly the 3 variants "Desde una esquina" offers
                // (NoMargin/PrinterLimit/UserDefined) -- unlike the native 2022+ API, 2021 needs no
                // approximation here at all.
                if (_settings.PaperPlacement == PdfPaperPlacement.OffsetFromCorner)
                {
                    parameters.PaperPlacement = PaperPlacementType.Margins;
                    switch (_settings.CornerMarginMode)
                    {
                        case PdfCornerMarginMode.NoMargin:
                            parameters.MarginType = MarginType.NoMargin;
                            break;
                        case PdfCornerMarginMode.PrinterLimit:
                            parameters.MarginType = MarginType.PrinterLimit;
                            break;
                        default:
                            parameters.MarginType = MarginType.UserDefined;
                            parameters.UserDefinedMarginX = _settings.OffsetXInches;
                            parameters.UserDefinedMarginY = _settings.OffsetYInches;
                            break;
                    }
                }
                else
                {
                    parameters.PaperPlacement = PaperPlacementType.Center;
                }

                parameters.ColorDepth = PdfExportSettings.ToColorDepth(_settings.ColorMode);
                parameters.RasterQuality = PdfExportSettings.ToRasterQuality(_settings.RasterQuality);
                parameters.HideCropBoundaries = _settings.HideCropBoundaries;
                parameters.HideScopeBoxes = _settings.HideScopeBoxes;
                parameters.HideUnreferencedViewTags = _settings.HideUnreferencedViewTags;
                parameters.HideReforWorkPlanes = _settings.HideReferencePlanes;
                parameters.MaskCoincidentLines = _settings.MaskCoincidentLines;
                parameters.ViewLinksinBlue = _settings.ViewLinksInBlue;
                parameters.ReplaceHalftoneWithThinLines = _settings.ReplaceHalftoneWithThinLines;
                parameters.HiddenLineViews = _settings.HiddenLineProcessing == PdfHiddenLineProcessing.Raster
                    ? HiddenLineViewsType.RasterProcessing
                    : HiddenLineViewsType.VectorProcessing;

                // Re-assign InSession after mutating PrintParameters so Apply() commits Zoom /
                // paper / appearance (some Revit 2021 builds drop in-place edits otherwise).
                setup.CurrentPrintSetting = setup.InSession;

                string paper = sizeApplied
                    ? matched.Name
                    : _settings.MatchSheetSize ? "sin coincidencia" : "detección desactivada";

                string zoomText = _settings.FitToPage
                    ? "ajustar a página"
                    : $"zoom {_settings.ZoomPercentage}%";

                return $"impresora: {_printer.Name}, tamaño: {size}, papel: {paper}, {zoomText}";
            }
        }
    }
}
#endif
