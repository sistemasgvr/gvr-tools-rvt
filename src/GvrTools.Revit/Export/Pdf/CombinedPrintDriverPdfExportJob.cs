#if !REVIT2022_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using GvrTools.Core.Naming;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Model;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Combines every selected sheet/view into ONE PDF on Revit 2021 -- the printer-driver
    /// counterpart of <see cref="CombinedPdfExportJob"/> (2022+, native <c>Document.Export</c>).
    ///
    /// Unlike <see cref="PrintDriverPdfExportEngine"/>'s per-sheet path (<c>PrintRange.Current</c>,
    /// one active view + one submitted job per sheet), this configures an in-session
    /// <c>ViewSheetSet</c> with every selected item and submits ONE combined print job via
    /// <c>PrintRange.Select</c> + <c>PrintManager.CombinedFile</c>.
    ///
    /// <see cref="PrintDriverPdfExportEngine"/>'s own header comment documents that
    /// <c>PrintRange.Select</c> "proved to lose its selection intermittently between configuring and
    /// submitting" -- that finding was for the ORIGINAL use case: reconfiguring the same in-session
    /// set once per sheet, in a loop, N times per batch. That specific risk does not carry over the
    /// same way here -- the set is configured exactly ONCE and submitted ONCE, no repeated
    /// reconfiguration cycle to go stale between. As extra insurance the output is still verified
    /// with the same file-exists/stable-size wait every other engine in this add-in uses
    /// (<see cref="IPdfOutputController.FinalizeJob"/>), so a submission that does go wrong is
    /// reported as a per-item failure instead of silently producing nothing.
    /// </summary>
    public sealed class CombinedPrintDriverPdfExportJob : IRevitStepJob
    {
        /// <summary>
        /// Floor for the wait, in seconds -- a combined job with many sheets genuinely takes longer
        /// to spool and convert than a single page, so the actual timeout scales with item count
        /// (see <see cref="RunCombinedPrint"/>) instead of using one fixed value for a run that could
        /// be 1 sheet or 150.
        /// </summary>
        private const int FileWriteTimeoutFloorSeconds = 45;

        /// <summary>Extra seconds of budget per sheet/view on top of the floor above.</summary>
        private const int FileWriteTimeoutPerItemSeconds = 3;

        private readonly Document _document;
        private readonly IReadOnlyList<SheetSnapshot> _items;
        private readonly PdfExportSettings _settings;
        private readonly ProjectSnapshot _project;
        private readonly string _folder;
        private readonly ILog _log;
        private readonly Action<BatchProgress> _onProgress;
        private readonly Action<BatchItemResult> _onItemCompleted;
        private readonly Action<BatchResult> _onFinished;
        private readonly List<BatchItemResult> _results = new List<BatchItemResult>();
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private PdfPrinter _printer;
        private PrintManager _printManager;
        private IPdfOutputController _output;

        public CombinedPrintDriverPdfExportJob(
            ExportRequest request,
            IReadOnlyList<SheetSnapshot> items,
            Action<BatchProgress> onProgress = null,
            Action<BatchItemResult> onItemCompleted = null,
            Action<BatchResult> onFinished = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            _document = request.UIDocument.Document;
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _settings = request.SettingsAs<PdfExportSettings>();
            _project = request.Project;
            _folder = request.DestinationFolder;
            _log = request.Log;
            _onProgress = onProgress;
            _onItemCompleted = onItemCompleted;
            _onFinished = onFinished;
        }

        public string Name => "Exportación PDF combinado";

        /// <summary>One step: the submission is a single Revit print job, not one per sheet.</summary>
        public int StepCount => 1;

        public void Begin(UIApplication application)
        {
            _stopwatch.Restart();

            if (!ExportPathHelper.TryEnsureWritable(_folder, out string error))
                throw new ExportSetupException(error);

            // Reuses the exact same printer resolution and output-mechanism selection as the
            // per-sheet engine (PrintDriverPdfExportEngine.ResolvePrinter/CreateOutputController are
            // internal, not private, specifically so both paths always agree on how to drive a given
            // printer).
            _printer = PrintDriverPdfExportEngine.ResolvePrinter(_settings.PrinterName);
            _printManager = _document.PrintManager;
            _output = PrintDriverPdfExportEngine.CreateOutputController(_printer, _log, _printManager);

            try
            {
                _printManager.SelectNewPrintDriver(_printer.Name);
            }
            catch (Exception ex)
            {
                throw new ExportSetupException($"No se pudo seleccionar la impresora \"{_printer.Name}\": {ex.Message}", ex);
            }

            // PrintToFile stays true throughout: Revit rejects false for virtual printers (Adobe PDF,
            // PDF24, most PDF drivers), which is exactly what this engine always uses.
            _printManager.PrintToFile = true;
            _printManager.CombinedFile = true;

            _log.Info($"{Name}: {_items.Count} elemento(s) hacia '{_folder}' en un solo archivo " +
                      $"(impresora '{_printer.Name}').");
        }

        public void ExecuteStep(UIApplication application, int stepIndex)
        {
            _onProgress?.Invoke(new BatchProgress(0, 1, "Combinando en un solo PDF..."));

            var resolvedViews = new List<View>();
            var resolvedIds = new HashSet<ElementId>();

            foreach (SheetSnapshot item in _items)
            {
                View view = SheetRepository.ResolveView(_document, item);
                if (view == null) continue;

                resolvedViews.Add(view);
                resolvedIds.Add(item.Id);
            }

            string failureMessage = null;
            string combinedPath = null;
            if (resolvedViews.Count > 0)
                failureMessage = RunCombinedPrint(resolvedViews, out combinedPath);

            // Un solo recorrido, en el MISMO ORDEN que _items -- ver el comentario equivalente en
            // CombinedPdfExportJob.ExecuteStep: BatchExportViewModel.OnItemCompleted empareja cada
            // resultado con su fila por posición, no por id.
            foreach (SheetSnapshot item in _items)
            {
                if (!resolvedIds.Contains(item.Id))
                {
                    string missing = item.Kind == ExportItemKind.View
                        ? "La vista ya no existe en el proyecto."
                        : "La lámina ya no existe en el proyecto.";
                    Report(BatchItemResult.Failure(item.Label, missing));
                }
                else if (failureMessage != null)
                {
                    Report(BatchItemResult.Failure(item.Label, failureMessage));
                }
                else
                {
                    Report(BatchItemResult.Success(item.Label, combinedPath));
                }
            }

            _onProgress?.Invoke(new BatchProgress(1, 1, "Combinando en un solo PDF..."));
        }

        /// <summary>Runs the one combined print job. Returns null on success, or a user-facing failure message.</summary>
        private string RunCombinedPrint(List<View> views, out string expectedPath)
        {
            string fileName = BuildCombinedFileName();
            expectedPath = Path.Combine(_folder, fileName + ".pdf");

            using (var viewSet = new ViewSet())
            {
                foreach (View view in views) viewSet.Insert(view);

                // InSessionViewSheetSet implements IDisposable like every other Revit API wrapper
                // this file touches (ViewSet above, the various print-manager sub-objects elsewhere
                // in this add-in) -- it is a fresh .NET wrapper handed out on each read of
                // ViewSheetSetting.InSession, not the persistent "in session set" concept itself, so
                // disposing it here only releases this wrapper's native handle and does not disturb
                // Revit's own document-level state.
                InSessionViewSheetSet inSession = null;
                try
                {
                    try
                    {
                        // Orden obligatorio, confirmado en la documentación XML oficial de Autodesk
                        // (RevitAPI.xml, empaquetada con el SDK): el GETTER de PrintManager.ViewSheetSetting
                        // lanza InvalidOperationException ("Thrown when the print range is not selected
                        // views/sheets") si PrintRange todavía no es Select en el momento de leerlo. Por
                        // eso PrintRange se fija PRIMERO, antes de tocar ViewSheetSetting.
                        _printManager.PrintRange = PrintRange.Select;
                        inSession = _printManager.ViewSheetSetting.InSession;
                        _printManager.ViewSheetSetting.CurrentViewSheetSet = inSession;
                        inSession.Views = viewSet;
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Fallo al preparar el set de láminas/vistas para el PDF combinado.", ex);
                        return "No se pudo preparar la selección de láminas/vistas para el PDF combinado.";
                    }

                    try
                    {
                        ApplyPrintSettings();
                    }
                    catch (Exception ex)
                    {
                        // Page setup is a nicety; producing the PDF matters more than a perfect size.
                        _log.Warn("No se pudo configurar el papel para el PDF combinado: " + ex.Message);
                    }

                    // Must happen before Apply/SubmitPrint: this is what stops the driver from asking.
                    _output.DirectNextJob(expectedPath);
                    _printManager.Apply();

                    bool submitted;
                    try
                    {
                        submitted = _printManager.SubmitPrint();
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Fallo al enviar el trabajo de impresión combinado.", ex);
                        return "Revit rechazó la exportación combinada de PDF.";
                    }

                    if (!submitted)
                        return "Revit rechazó la exportación combinada de PDF.";

                    // Escalado por cantidad de láminas/vistas: un combinado de 100 sheets vía
                    // Distiller/PDF24 puede tardar bastante más que uno de 1-2, y un timeout fijo
                    // reportaría "falló" para un trabajo que en realidad solo iba lento.
                    var timeout = TimeSpan.FromSeconds(FileWriteTimeoutFloorSeconds + views.Count * FileWriteTimeoutPerItemSeconds);
                    if (!_output.FinalizeJob(expectedPath, timeout))
                        return _output.DescribeFailure();
                }
                finally
                {
                    inSession?.Dispose();
                }
            }

            return null;
        }

        /// <summary>
        /// One paper size/orientation for the whole combined file -- same limitation as
        /// <see cref="CombinedPdfExportJob"/> (2022+): "usar el tamaño de cada lámina" cannot apply
        /// when several sheets of different sizes go into the same file.
        /// </summary>
        private void ApplyPrintSettings()
        {
            PrintSetup setup = _printManager.PrintSetup;
            setup.CurrentPrintSetting = setup.InSession;
            PrintParameters parameters = setup.CurrentPrintSetting.PrintParameters;

            parameters.ZoomType = _settings.FitToPage ? ZoomType.FitToPage : ZoomType.Zoom;
            if (!_settings.FitToPage) parameters.Zoom = _settings.ZoomPercentage;

            // Same MarginType mapping as PrintDriverPdfExportEngine.ApplyPrintSettings -- see that
            // method's comment for why 2021 needs no approximation for "Desde una esquina".
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
        }

        private void Report(BatchItemResult result)
        {
            _results.Add(result);
            _onItemCompleted?.Invoke(result);
        }

        private string BuildCombinedFileName()
        {
            string pattern = string.IsNullOrWhiteSpace(_settings.CombinedFileName)
                ? "{ProjectTitle}_combinado"
                : _settings.CombinedFileName;

            string built = FileNameBuilder.Build(pattern, _project.ToTokens(), _project.Title);

            // Same collision-avoidance every other export path uses, so a second combined run into
            // the same folder gets "_2" instead of silently overwriting the first.
            var resolver = new UniqueNameResolver(_folder);
            return resolver.ReserveBaseName(built, ".pdf");
        }

        public void End(UIApplication application, bool cancelled, Exception failure)
        {
            try
            {
                _output?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo restablecer la salida de la impresora: " + ex.Message);
            }

            _stopwatch.Stop();

            string setupError = failure is ExportSetupException setup
                ? setup.Message
                : failure != null
                    ? "Error inesperado durante la exportación: " + failure.Message
                    : null;

            var result = new BatchResult(_results, cancelled, _folder, _stopwatch.Elapsed, setupError);

            _log.Info($"{Name} finalizada: {result.SucceededCount} correcta(s), {result.FailedCount} con error, " +
                      $"cancelada={cancelled}, {result.Elapsed.TotalSeconds:0.0} s.");

            _onFinished?.Invoke(result);
        }
    }
}
#endif
