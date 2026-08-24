#if !REVIT2022_OR_GREATER
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Autodesk.Revit.DB;
using GvrTools.Core.Diagnostics;
using Microsoft.Win32;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Tells a PDF printer where to put the next job, so it never asks.
    ///
    /// This exists because "print to a file" is not one mechanism but several, and which one applies
    /// depends on the driver. Revit only knows about its own (<c>PrintToFileName</c>); a driver that
    /// ignores it needs to be told in its own language, before the job is submitted.
    /// </summary>
    internal interface IPdfOutputController : IDisposable
    {
        /// <summary>Short description of the mechanism, for the log and for error messages.</summary>
        string Description { get; }

        /// <summary>Arranges for the next submitted job to be written to <paramref name="path"/>.</summary>
        void DirectNextJob(string path);

        /// <summary>
        /// Waits for the job directed by the last <see cref="DirectNextJob"/> to finish, moving it
        /// into place first if this controller's mechanism cannot write directly to that path (e.g.
        /// it has to land in a scratch folder first). Returns false on timeout.
        /// </summary>
        bool FinalizeJob(string path, TimeSpan timeout);

        /// <summary>What to tell the user when the file never appeared.</summary>
        string DescribeFailure();
    }

    /// <summary>Small file-polling helpers shared by every <see cref="IPdfOutputController"/>.</summary>
    internal static class FileWait
    {
        /// <summary>
        /// Waits for a file to appear and its size to stop growing - the driver can still be writing
        /// it when the file first appears - so a half-written PDF is never reported as done.
        /// </summary>
        public static bool UntilStable(string path, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            long lastLength = -1;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists && file.Length > 0)
                    {
                        if (file.Length == lastLength) return true;
                        lastLength = file.Length;
                    }
                }
                catch (IOException)
                {
                    // Still locked by the spooler; keep waiting.
                }

                Thread.Sleep(150);
            }

            return File.Exists(path);
        }

        /// <summary>
        /// Waits for exactly one file to appear in a folder that started empty, and for its size to
        /// stop growing. For a controller that cannot be told the final path directly and instead
        /// writes into a scratch folder guaranteed to hold only this one job's output.
        /// </summary>
        public static string ForSingleFileIn(string folder, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            string candidate = null;
            long lastLength = -1;

            while (DateTime.UtcNow < deadline)
            {
                string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();

                if (files.Length >= 1)
                {
                    candidate = files[0];

                    try
                    {
                        long length = new FileInfo(candidate).Length;
                        if (length > 0 && length == lastLength) return candidate;
                        lastLength = length;
                    }
                    catch (IOException)
                    {
                        // Still locked; keep waiting.
                    }
                }

                Thread.Sleep(150);
            }

            return candidate;
        }
    }

    /// <summary>
    /// For drivers that honour the path Revit gives them: Revit captures the print output straight
    /// into the file and the driver is never consulted about where it goes.
    /// </summary>
    internal sealed class RevitPrintToFileOutput : IPdfOutputController
    {
        private readonly PrintManager _printManager;

        public RevitPrintToFileOutput(PrintManager printManager)
        {
            _printManager = printManager;
        }

        public string Description => "ruta de salida entregada por Revit (PrintToFileName)";


        public void DirectNextJob(string path) => _printManager.PrintToFileName = path;

        public bool FinalizeJob(string path, TimeSpan timeout) => FileWait.UntilStable(path, timeout);

        public string DescribeFailure() =>
            "La impresora no respetó la ruta de salida. Elige otra impresora PDF " +
            "(" + string.Join(", ", PdfPrinterCatalog.RecommendedPrinters) + ") o exporta a DWG.";

        public void Dispose()
        {
            // Nothing to undo: PrintToFileName is per-document print state, reset by Revit itself.
        }
    }

    /// <summary>
    /// For the "Adobe PDF" printer, which ignores Revit's output path and opens its own
    /// "Save PDF File As" dialog.
    ///
    /// Acrobat Distiller reads the target file name for the *next* job from
    /// <c>HKCU\Software\Adobe\Acrobat Distiller\PrinterJobControl</c>, using the full path of the
    /// printing application's executable as the value name. Writing that value before submitting is
    /// Adobe's own documented hand-off: no dialog appears, nothing takes the foreground, and no
    /// keyboard input is simulated. It is consumed per job, so it has to be set for every sheet.
    ///
    /// The value is removed again on <see cref="Dispose"/>: leaving it behind would silently
    /// redirect the user's next manual print from Revit to a stale path.
    /// </summary>
    internal sealed class AdobeDistillerOutput : IPdfOutputController
    {
        private const string JobControlKey = @"Software\Adobe\Acrobat Distiller\PrinterJobControl";

                private readonly ILog _log;
        private readonly PrintManager _printManager;
        private readonly string _hostExecutable;
        private bool _valueWritten;

        public AdobeDistillerOutput(ILog log, PrintManager printManager)
        {
            _log = log ?? NullLog.Instance;
            _printManager = printManager;
            _hostExecutable = ResolveHostExecutable();

            if (_hostExecutable == null)
                throw new ExportSetupException(
                    "No se pudo determinar la ruta del ejecutable de Revit, que es lo que Adobe PDF " +
                    "necesita para saber dónde escribir sin preguntar. Elige otra impresora PDF o exporta a DWG.");
        }

        public string Description => "PrintToFileName + hint en el registro para Acrobat Distiller";

        /// <summary>
        /// Both mechanisms are set on purpose. Revit rejects PrintToFile=false for a virtual printer
        /// like Adobe PDF ("The PrintToFile property cannot be set to false while the printer is
        /// Virtual"), so PrintToFileName has to be assigned. Distiller then reads the same path from
        /// PrinterJobControl and produces the PDF there without opening its Save dialog. Setting
        /// both is what pins the destination on every one of Adobe's independent paths.
        /// </summary>
        public void DirectNextJob(string path)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(JobControlKey))
                {
                    if (key == null)
                        throw new InvalidOperationException("No se pudo abrir la clave de registro de Distiller.");

                    key.SetValue(_hostExecutable, path, RegistryValueKind.String);
                    _valueWritten = true;
                    _printManager.PrintToFileName = path;
                }
            }
            catch (Exception ex)
            {
                throw new ExportSetupException(
                    "No se pudo indicarle a Adobe PDF dónde guardar el archivo." + Environment.NewLine +
                    Environment.NewLine +
                    "Adobe lee la ruta de destino de:" + Environment.NewLine +
                    @"  HKEY_CURRENT_USER\" + JobControlKey + Environment.NewLine +
                    Environment.NewLine +
                    "y esa escritura falló: " + ex.Message, ex);
            }
        }

        public bool FinalizeJob(string path, TimeSpan timeout) => FileWait.UntilStable(path, timeout);

        public string DescribeFailure() =>
            "Adobe PDF no generó el archivo. Revisa que en Propiedades de la impresora " +
            "\"Adobe PDF\" no esté activada la opción de preguntar siempre el nombre, o elige otra " +
            "impresora PDF (" + string.Join(", ", PdfPrinterCatalog.RecommendedPrinters) + ").";

        public void Dispose()
        {
            if (!_valueWritten) return;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(JobControlKey, writable: true))
                {
                    key?.DeleteValue(_hostExecutable, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo limpiar la ruta de salida de Adobe PDF en el registro: " + ex.Message);
            }
        }

        private static string ResolveHostExecutable()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
#endif
