#if !REVIT2022_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.IO;
using GvrTools.Core.Naming;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Model;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Combines every selected sheet/view into ONE PDF on Revit 2021 -- the printer-driver
    /// counterpart of <see cref="CombinedPdfExportJob"/> (2022+, native <c>Document.Export</c>).
    ///
    /// Strategy (proven after PrintRange.Select + ViewSheetSetting.SaveAs failed repeatedly in
    /// real Revit 2021 logs -- empty Views, ModificationOutsideTransactionException, then
    /// "Save of the setting was unsuccessful"):
    /// <list type="number">
    ///   <item>Plot each sheet with the working per-sheet path
    ///     (<see cref="PrintDriverPdfExportEngine"/> / <c>PrintRange.Current</c> + PDF24 autoSave).</item>
    ///   <item>Join the temp PDFs with PDF24's <c>pdf24-DocTool.exe -join</c> into the final file.</item>
    ///   <item>Delete the temp parts.</item>
    /// </list>
    /// One Idling step per sheet (+ one merge step) so Cancel can take effect between sheets and
    /// the UI can repaint -- a single giant step would freeze Revit for the whole batch.
    /// </summary>
    public sealed class CombinedPrintDriverPdfExportJob : IRevitStepJob
    {
        private const int MergeTimeoutFloorSeconds = 30;
        private const int MergeTimeoutPerFileSeconds = 5;

        // CreateProcess argument limit is ~32K characters; stay well under and chunk-join.
        private const int MaxJoinArgsLength = 28000;

        private readonly ExportRequest _request;
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

        private string _combinedPath;
        private string _tempRoot;
        private IExportSession _session;
        private readonly List<string> _partPaths = new List<string>();
        private string _failureMessage;

        public CombinedPrintDriverPdfExportJob(
            ExportRequest request,
            IReadOnlyList<SheetSnapshot> items,
            Action<BatchProgress> onProgress = null,
            Action<BatchItemResult> onItemCompleted = null,
            Action<BatchResult> onFinished = null)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
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

        /// <summary>One step per sheet plus a final merge step (Cancel works between steps).</summary>
        public int StepCount => _items.Count + 1;

        public void Begin(UIApplication application)
        {
            _stopwatch.Restart();
            _partPaths.Clear();
            _failureMessage = null;
            _session = null;
            _tempRoot = null;
            _combinedPath = null;

            if (!ExportPathHelper.TryEnsureWritable(_folder, out string error))
                throw new ExportSetupException(error);

            if (FindPdf24DocTool() == null)
            {
                throw new ExportSetupException(
                    "No se encontró pdf24-DocTool.exe (necesario para combinar PDFs en Revit 2021)." +
                    Environment.NewLine + Environment.NewLine +
                    @"Instala PDF24 Creator en la ruta por defecto (C:\Program Files\PDF24\) o exporta sin combinar.");
            }

            PrintDriverPdfExportEngine.ResolvePrinter(_settings.PrinterName);

            _combinedPath = Path.Combine(_folder, BuildCombinedFileName() + ".pdf");
            _tempRoot = Path.Combine(Path.GetTempPath(), "GvrTools_Combine_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            var tempRequest = new ExportRequest(
                _request.UIDocument,
                _tempRoot,
                "{SheetNumber}_{SheetName}",
                _settings,
                _project,
                _log);

            _session = new PrintDriverPdfExportEngine().BeginSession(tempRequest);

            _log.Info($"{Name}: {_items.Count} elemento(s) → temp por lámina + unión PDF24 → '{_folder}'.");
        }

        public void ExecuteStep(UIApplication application, int stepIndex)
        {
            int totalSteps = StepCount;

            // After a failure, remaining export steps no-op; the merge step still runs to clean up
            // reporting (join is skipped when _failureMessage is set).
            if (stepIndex < _items.Count)
            {
                if (_failureMessage != null) return;

                SheetSnapshot item = _items[stepIndex];
                _onProgress?.Invoke(new BatchProgress(
                    stepIndex, totalSteps, $"Exportando lámina {stepIndex + 1} de {_items.Count}..."));

                BatchItemResult part = _session.Export(item);
                if (!part.Succeeded)
                {
                    _failureMessage = part.Message ?? ("Falló la exportación de " + item.Label + ".");
                    _log.Error($"PDF combinado: falló la parte '{item.Label}': {_failureMessage}");
                    return;
                }

                string orderedPath = Path.Combine(
                    _tempRoot,
                    (stepIndex + 1).ToString("D4") + "_" + Path.GetFileName(part.OutputPath));
                try
                {
                    if (!string.Equals(part.OutputPath, orderedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(orderedPath)) File.Delete(orderedPath);
                        File.Move(part.OutputPath, orderedPath);
                    }

                    _partPaths.Add(orderedPath);
                }
                catch (Exception ex)
                {
                    _failureMessage = "No se pudo preparar el PDF temporal de " + item.Label + ": " + ex.Message;
                    _log.Error(_failureMessage, ex);
                }

                return;
            }

            // Final step: dispose session, merge, report every row with the same outcome.
            _onProgress?.Invoke(new BatchProgress(
                _items.Count, totalSteps, "Combinando PDFs con PDF24..."));

            try
            {
                DisposeSession();

                if (_failureMessage == null)
                    _failureMessage = JoinWithPdf24(_partPaths, _combinedPath);
            }
            catch (Exception ex)
            {
                _failureMessage = "Error al combinar PDFs: " + ex.Message;
                _log.Error(_failureMessage, ex);
            }
            finally
            {
                TryDeleteDirectory(_tempRoot);
                _tempRoot = null;
            }

            foreach (SheetSnapshot item in _items)
            {
                if (_failureMessage != null)
                    Report(BatchItemResult.Failure(item.Label, _failureMessage));
                else
                    Report(BatchItemResult.Success(item.Label, _combinedPath));
            }

            _onProgress?.Invoke(new BatchProgress(totalSteps, totalSteps, "Combinando en un solo PDF..."));
        }

        /// <summary>
        /// Joins <paramref name="parts"/> into <paramref name="outputPath"/> via PDF24 DocTool.
        /// Returns null on success, or a user-facing error message.
        /// Chunks when the command line would exceed Windows' ~32K argument limit.
        /// </summary>
        private string JoinWithPdf24(IReadOnlyList<string> parts, string outputPath)
        {
            if (parts == null || parts.Count == 0)
                return "No se generó ningún PDF parcial para combinar.";

            if (parts.Count == 1)
            {
                try
                {
                    if (File.Exists(outputPath)) File.Delete(outputPath);
                    File.Move(parts[0], outputPath);
                    return null;
                }
                catch (Exception ex)
                {
                    return "No se pudo mover el PDF al destino final: " + ex.Message;
                }
            }

            string docTool = FindPdf24DocTool();
            if (docTool == null)
                return "No se encontró pdf24-DocTool.exe para unir los PDFs.";

            try
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
            catch (Exception ex)
            {
                return "No se pudo sobrescribir el PDF de destino: " + ex.Message;
            }

            // Chunked join: A+B+C → temp1, temp1+D+E → temp2, … → outputPath.
            var pending = new List<string>(parts);
            int wave = 0;
            string mergeTempDir = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? _folder,
                ".gvr_merge_" + Guid.NewGuid().ToString("N"));

            try
            {
                while (pending.Count > 1)
                {
                    var chunk = new List<string>();
                    int argsLen = EstimateJoinArgsLength(outputPath); // fixed prefix budget

                    for (int i = 0; i < pending.Count; i++)
                    {
                        int nextLen = pending[i].Length + 3; // space + quotes
                        if (chunk.Count > 0 && argsLen + nextLen > MaxJoinArgsLength)
                            break;

                        chunk.Add(pending[i]);
                        argsLen += nextLen;
                    }

                    // Always take at least 2 files when possible so we make progress.
                    if (chunk.Count < 2 && pending.Count >= 2)
                    {
                        chunk.Clear();
                        chunk.Add(pending[0]);
                        chunk.Add(pending[1]);
                    }

                    bool isLastWave = chunk.Count == pending.Count;
                    string waveOutput = isLastWave
                        ? outputPath
                        : Path.Combine(EnsureDir(mergeTempDir), "wave_" + wave.ToString("D3") + ".pdf");

                    string error = RunDocToolJoin(docTool, chunk, waveOutput);
                    if (error != null) return error;

                    pending.RemoveRange(0, chunk.Count);
                    pending.Insert(0, waveOutput);
                    wave++;
                }
            }
            finally
            {
                TryDeleteDirectory(mergeTempDir);
            }

            return null;
        }

        private static int EstimateJoinArgsLength(string outputPath) =>
            "-join -noProgress -profile \"default/good\" -outputFile ".Length + outputPath.Length + 2;

        private string RunDocToolJoin(string docTool, IReadOnlyList<string> inputs, string outputPath)
        {
            try
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
            catch (Exception ex)
            {
                return "No se pudo preparar el PDF de destino: " + ex.Message;
            }

            var args = new StringBuilder();
            args.Append("-join -noProgress -profile \"default/good\" -outputFile ");
            args.Append('"').Append(outputPath).Append('"');
            foreach (string part in inputs)
                args.Append(' ').Append('"').Append(part).Append('"');

            _log.Info($"PDF24 join ({inputs.Count} archivo(s)): \"{docTool}\" {args}");

            try
            {
                // Do NOT redirect stdout/stderr: WaitForExit + ReadToEnd after exit deadlocks when
                // the child fills the OS pipe buffer. We only care about exit code + output file.
                var start = new ProcessStartInfo
                {
                    FileName = docTool,
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    WorkingDirectory = Path.GetDirectoryName(docTool) ?? _folder
                };

                using (Process process = Process.Start(start))
                {
                    if (process == null)
                        return "No se pudo iniciar pdf24-DocTool.exe.";

                    int timeoutMs = (MergeTimeoutFloorSeconds + inputs.Count * MergeTimeoutPerFileSeconds) * 1000;
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { /* best effort */ }
                        return "PDF24 tardó demasiado en combinar los PDFs.";
                    }

                    if (process.ExitCode != 0)
                        _log.Warn($"pdf24-DocTool exit={process.ExitCode} (se comprueba si el PDF existe).");
                }
            }
            catch (Exception ex)
            {
                _log.Error("Fallo al ejecutar pdf24-DocTool.", ex);
                return "No se pudo ejecutar PDF24 para combinar: " + ex.Message;
            }

            var wait = TimeSpan.FromSeconds(MergeTimeoutFloorSeconds + inputs.Count * MergeTimeoutPerFileSeconds);
            if (!FileWait.UntilStable(outputPath, wait))
            {
                return "PDF24 no generó el archivo combinado esperado. " +
                       "Abre PDF24 una vez de forma manual por si pide aceptar un cambio, " +
                       "o exporta sin combinar.";
            }

            return null;
        }

        private static string EnsureDir(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static string FindPdf24DocTool()
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PDF24", "pdf24-DocTool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PDF24", "pdf24-DocTool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDF24", "pdf24-DocTool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PDF24", "pdf24-DocTool.exe")
            };

            // Also try beside pdf24.exe if the user installed to a custom location via PATH / App Paths.
            try
            {
                string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\pdf24.exe";
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.LocalMachine.OpenSubKey(appPaths)
                    ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(appPaths))
                {
                    string pdf24 = key?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(pdf24))
                    {
                        string dir = Path.GetDirectoryName(pdf24);
                        if (!string.IsNullOrEmpty(dir))
                            candidates.Add(Path.Combine(dir, "pdf24-DocTool.exe"));
                    }
                }
            }
            catch
            {
                // Registry lookup is best-effort.
            }

            foreach (string path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }

            return null;
        }

        private void DisposeSession()
        {
            if (_session == null) return;
            try { _session.Dispose(); }
            catch { /* best effort */ }
            _session = null;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // Best-effort temp cleanup; never fatal.
            }
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
            var resolver = new UniqueNameResolver(_folder);
            return resolver.ReserveBaseName(built, ".pdf");
        }

        public void End(UIApplication application, bool cancelled, Exception failure)
        {
            DisposeSession();
            TryDeleteDirectory(_tempRoot);
            _tempRoot = null;
            _stopwatch.Stop();

            // Reports only fire on the merge step; cancel/exception before that leaves _results empty.
            if (_results.Count == 0 && _items.Count > 0 && (cancelled || failure != null))
            {
                string msg = failure is ExportSetupException
                    ? failure.Message
                    : failure != null
                        ? "Error inesperado durante la exportación: " + failure.Message
                        : (_failureMessage ?? "Exportación cancelada antes de combinar el PDF.");

                foreach (SheetSnapshot item in _items)
                    Report(BatchItemResult.Failure(item.Label, msg));
            }

            string setupError = failure is ExportSetupException setup ? setup.Message : null;

            var result = new BatchResult(_results, cancelled, _folder, _stopwatch.Elapsed, setupError);

            _log.Info($"{Name} finalizada: {result.SucceededCount} correcta(s), {result.FailedCount} con error, " +
                      $"cancelada={cancelled}, {result.Elapsed.TotalSeconds:0.0} s.");

            _onFinished?.Invoke(result);
        }
    }
}
#endif
