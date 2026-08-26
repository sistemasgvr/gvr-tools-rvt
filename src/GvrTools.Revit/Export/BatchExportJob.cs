using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.IO;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Model;

namespace GvrTools.Revit.Export
{
    /// <summary>
    /// Drives one export run as a step job: one sheet per step, so the scheduler can hand control
    /// back to Revit in between and the window stays responsive and cancellable.
    ///
    /// The job owns the session lifetime and the result collection; it knows nothing about which
    /// format is being written.
    /// </summary>
    public sealed class BatchExportJob : IRevitStepJob
    {
        private readonly IExportEngine _engine;
        private readonly ExportRequest _request;
        private readonly IReadOnlyList<SheetSnapshot> _sheets;
        private readonly Action<BatchProgress> _onProgress;
        private readonly Action<BatchItemResult> _onItemCompleted;
        private readonly Action<BatchResult> _onFinished;
        private readonly List<BatchItemResult> _results;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private IExportSession _session;

        public BatchExportJob(
            IExportEngine engine,
            ExportRequest request,
            IReadOnlyList<SheetSnapshot> sheets,
            Action<BatchProgress> onProgress = null,
            Action<BatchItemResult> onItemCompleted = null,
            Action<BatchResult> onFinished = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
            _onProgress = onProgress;
            _onItemCompleted = onItemCompleted;
            _onFinished = onFinished;
            _results = new List<BatchItemResult>(sheets.Count);
        }

        public string Name => $"Exportación {ExportFormatInfo.Label(_engine.Format)}";

        public int StepCount => _sheets.Count;

        public void Begin(UIApplication application)
        {
            _stopwatch.Restart();
            CreateDestinationFolder();

            _request.Log.Info($"{Name}: {_sheets.Count} lámina(s) hacia '{_request.DestinationFolder}' " +
                              $"({_engine.StrategyDescription}).");

            _session = _engine.BeginSession(_request);
        }

        public void ExecuteStep(UIApplication application, int stepIndex)
        {
            SheetSnapshot sheet = _sheets[stepIndex];
            _onProgress?.Invoke(new BatchProgress(stepIndex, _sheets.Count, sheet.Label));

            BatchItemResult result = ExportOne(sheet);
            _results.Add(result);
            _onItemCompleted?.Invoke(result);

            _onProgress?.Invoke(new BatchProgress(stepIndex + 1, _sheets.Count, sheet.Label));
        }

        public void End(UIApplication application, bool cancelled, Exception failure)
        {
            try
            {
                _session?.Dispose();
            }
            catch (Exception ex)
            {
                _request.Log.Warn("El cierre de la sesión de exportación falló: " + ex.Message);
            }
            finally
            {
                _session = null;
                _stopwatch.Stop();
            }

            string setupError = failure is ExportSetupException setup
                ? setup.Message
                : failure != null
                    ? "Error inesperado durante la exportación: " + failure.Message
                    : null;

            var result = new BatchResult(_results, cancelled, _request.DestinationFolder, _stopwatch.Elapsed, setupError);

            _request.Log.Info($"{Name} finalizada: {result.SucceededCount} correcta(s), {result.FailedCount} con error, " +
                              $"cancelada={cancelled}, {result.Elapsed.TotalSeconds:0.0} s.");

            _onFinished?.Invoke(result);
        }

        /// <summary>
        /// A per-sheet failure never aborts the batch: engines are expected to return a failed
        /// result, and this catch is the backstop for the ones that misbehave.
        /// </summary>
        private BatchItemResult ExportOne(SheetSnapshot sheet)
        {
            try
            {
                return _session.Export(sheet);
            }
            catch (Exception ex)
            {
                _request.Log.Error($"Fallo al exportar la lámina {sheet.Label}.", ex);
                return BatchItemResult.Failure(sheet.Label, ex.Message);
            }
        }

        private void CreateDestinationFolder()
        {
            if (ExportPathHelper.TryEnsureWritable(_request.DestinationFolder, out string error))
                return;

            throw new ExportSetupException(error);
        }
    }
}
