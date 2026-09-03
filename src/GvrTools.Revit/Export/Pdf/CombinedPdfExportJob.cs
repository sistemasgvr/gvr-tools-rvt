#if REVIT2022_OR_GREATER
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
    /// Exports every selected sheet/view into ONE combined PDF file, using Revit's native PDF
    /// exporter with several element ids in a single <c>Document.Export</c> call.
    ///
    /// Deliberately a separate job type from <see cref="BatchExportJob"/> rather than a mode inside
    /// it: combining is a genuinely different execution shape (one Revit call for the whole set, not
    /// one call per item), so folding it into the per-item step loop would mean either faking
    /// per-item progress for a call that has none, or complicating every engine with a branch it
    /// does not need. Both job types implement <see cref="IRevitStepJob"/>, so
    /// <see cref="RevitJobScheduler"/> does not care which one runs -- <see cref="BatchExportViewModel"/>
    /// just picks the right one when "Combinar en un solo archivo" is checked.
    ///
    /// 2022+ only (this file is under <c>REVIT2022_OR_GREATER</c>). Revit 2021 has no native PDF
    /// API; combine there is <see cref="CombinedPrintDriverPdfExportJob"/> (per-sheet PDF24 plot +
    /// <c>pdf24-DocTool -join</c>), not PrintRange.Select / ViewSheetSetting.
    /// </summary>
    public sealed class CombinedPdfExportJob : IRevitStepJob
    {
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

        public CombinedPdfExportJob(
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

        /// <summary>
        /// One step: the write is a single Revit call, so there is no meaningful midpoint to report
        /// progress at (unlike <see cref="BatchExportJob"/>'s one-step-per-sheet).
        /// </summary>
        public int StepCount => 1;

        public void Begin(UIApplication application)
        {
            _stopwatch.Restart();

            if (!ExportPathHelper.TryEnsureWritable(_folder, out string error))
                throw new ExportSetupException(error);

            _log.Info($"{Name}: {_items.Count} elemento(s) hacia '{_folder}' en un solo archivo " +
                      "(API nativa de PDF de Revit).");
        }

        public void ExecuteStep(UIApplication application, int stepIndex)
        {
            _onProgress?.Invoke(new BatchProgress(0, 1, "Combinando en un solo PDF..."));

            var ids = new List<ElementId>();
            var resolved = new HashSet<ElementId>();

            foreach (SheetSnapshot item in _items)
            {
                View view = SheetRepository.ResolveView(_document, item);
                if (view == null) continue;

                ids.Add(item.Id);
                resolved.Add(item.Id);
            }

            string failureMessage = null;
            string combinedPath = null;
            if (ids.Count > 0)
                failureMessage = RunCombinedExport(ids, out combinedPath);

            // Un solo recorrido, en el MISMO ORDEN que _items -- BatchExportViewModel.OnItemCompleted
            // asocia cada resultado a su fila emparejándolo por posición contra _pendingSheets
            // (mismo orden con el que este job se construyó), no por id; reportar los ítems que
            // faltan tan pronto se detectan (en vez de en este recorrido final) desalinearía esa
            // correspondencia apenas hubiera una lámina/vista borrada en medio del lote.
            foreach (SheetSnapshot item in _items)
            {
                if (!resolved.Contains(item.Id))
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

        /// <summary>Runs the one combined Document.Export call. Returns null on success, or a user-facing failure message.</summary>
        private string RunCombinedExport(List<ElementId> ids, out string expectedPath)
        {
            string fileName = BuildCombinedFileName();
            expectedPath = Path.Combine(_folder, fileName + ".pdf");

            // One paper setup for the whole set. Prefer the first resolvable sheet's size so Zoom
            // / corner placement get a named ExportPaperFormat (Default would ignore them).
            SheetSize representative = SheetSize.Unknown;
            PageOrientationType orientation = PageOrientationType.Auto;
            foreach (ElementId id in ids)
            {
                if (!(_document.GetElement(id) is ViewSheet sheet)) continue;
                representative = SheetSizeReader.Read(_document, sheet);
                if (representative.IsKnown)
                {
                    orientation = representative.IsLandscape
                        ? PageOrientationType.Landscape
                        : PageOrientationType.Portrait;
                    break;
                }
            }

            bool exported;
            using (PDFExportOptions options = PdfExportOptionsFactory.Build(
                _settings, fileName, orientation, representative))
            {
                if (!_settings.FitToPage)
                {
                    _log.Info(
                        $"PDF combinado: Zoom={_settings.ZoomPercentage}%, " +
                        $"PaperFormat={options.PaperFormat}, size={representative}.");
                }

                try
                {
                    exported = _document.Export(_folder, ids, options);
                }
                catch (Exception ex)
                {
                    exported = false;
                    _log.Error("Fallo al exportar el PDF combinado.", ex);
                }
            }

            if (!exported)
                return "Revit rechazó la exportación combinada de PDF.";

            if (!File.Exists(expectedPath))
            {
                _log.Warn($"Revit informó éxito pero no se encontró '{expectedPath}'.");
                return "Revit no generó el archivo PDF combinado esperado.";
            }

            return null;
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
