#if !REVIT2022_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using GvrTools.Core.Diagnostics;
using Microsoft.Win32;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// For the "PDF24" printer, whose default profile opens the interactive "PDF24 Assistant" after
    /// every job and waits there for the user to pick an action - unusable for a batch of any size.
    ///
    /// PDF24 also has a silent "autoSave" handler that skips the Assistant entirely. This was not
    /// found documented anywhere; it was read directly from a real PDF24 install's own registry
    /// entries, where DiRoots ProSheets' bundled PDF24-based printer already uses exactly this
    /// mechanism (which is also why that printer cannot be used here instead - see
    /// <see cref="PdfPrinterCatalog"/> - it always writes to one fixed folder of its own):
    ///
    ///   HKEY_LOCAL_MACHINE\Software\PDF24\Services\&lt;subkey&gt;
    /// (the subkey is NOT necessarily the printer's display name - see <see cref="ResolveServiceKeyPath"/>)
    ///     Handler                 "default" (shows the Assistant) or "autoSave" (writes and exits)
    ///     AutoSaveDir             folder the file is written into
    ///     AutoSaveFilename        filename template (irrelevant here - see below)
    ///     AutoSaveOpenDir         open the folder afterwards (0/1)
    ///     AutoSaveOverwriteFile   overwrite an existing file instead of renaming (0/1)
    ///     AutoSaveShowProgress    show a progress window while saving (0/1)
    ///     AutoSaveUseFileChooser  ask for the file name instead of using AutoSaveFilename (0/1)
    ///     AutoSaveUseFileCmd      run AutoSaveFileCmd instead of saving directly (0/1)
    ///
    /// This switches the PDF24 printer's own entry to that handler for the life of the run, and
    /// before every job points AutoSaveDir at a fresh, empty, per-sheet temp folder - so whatever
    /// single file appears there can only be this job's output regardless of what PDF24 names it,
    /// sidestepping AutoSaveFilename's templating entirely. The file is then moved into its real
    /// destination, the same land-it-anywhere-then-move approach the DWG/PDF file namers already use
    /// elsewhere. The printer's original settings are restored on <see cref="Dispose"/>, so manual,
    /// interactive use of "PDF24" from any other application is unaffected outside of this run.
    ///
    /// Both HKEY_CURRENT_USER and HKEY_LOCAL_MACHINE are written where possible. HKCU is the one that
    /// matters in practice - it needs no elevation, and PDF24 evidently treats a per-user entry as an
    /// override, since the ProSheets entry above is mirrored identically under both hives - but HKLM
    /// is also attempted, best-effort, in case a given install only honours it; a failure to write
    /// HKLM (most commonly a permissions error, since a standard Revit session is not elevated) is
    /// logged and otherwise ignored rather than treated as a setup failure.
    /// </summary>
    internal sealed class Pdf24AutoSaveOutput : IPdfOutputController
    {
        private const string ServicesKey = @"Software\PDF24\Services";

        private static readonly string[] BackedUpValueNames =
        {
            "Handler", "AutoSaveDir", "AutoSaveFilename", "AutoSaveOpenDir", "AutoSaveOverwriteFile",
            "AutoSaveShowProgress", "AutoSaveUseFileChooser", "AutoSaveUseFileCmd"
        };

        private readonly ILog _log;
        private readonly string _serviceKeyPath;
        private readonly string _tempRoot;
        private readonly HiveBackup _userBackup;
        private readonly HiveBackup _machineBackup;
        private string _currentSheetTempDir;
        private bool _disposed;

        public Pdf24AutoSaveOutput(ILog log, PrintManager printManager, string printerName, string printerPort)
        {
            _log = log ?? NullLog.Instance;
            _serviceKeyPath = ResolveServiceKeyPath(printerName, printerPort);
            _tempRoot = Path.Combine(Path.GetTempPath(), "GvrTools_Pdf24_" + Guid.NewGuid().ToString("N"));

            _userBackup = BackupAndSwitch(Registry.CurrentUser, required: true);
            _machineBackup = BackupAndSwitch(Registry.LocalMachine, required: false);

            // PDF24 needs PrintToFile=true for a virtual printer regardless of which handler is
            // active; PrintToFileName itself is ignored by the autoSave handler.
            printManager.PrintToFile = true;
        }

        public string Description => "perfil \"autoSave\" de PDF24 (evita el Asistente interactivo)";

        public void DirectNextJob(string path)
        {
            _currentSheetTempDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_currentSheetTempDir);

            SetAutoSaveDir(Registry.CurrentUser, _currentSheetTempDir, required: true);
            SetAutoSaveDir(Registry.LocalMachine, _currentSheetTempDir, required: false);
        }

        public bool FinalizeJob(string path, TimeSpan timeout)
        {
            string produced = FileWait.ForSingleFileIn(_currentSheetTempDir, timeout);
            bool success = false;

            if (produced != null)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(produced, path);
                    success = true;
                }
                catch (Exception ex)
                {
                    _log.Warn($"No se pudo mover el PDF generado por PDF24 a su destino final: {ex.Message}");
                }
            }

            TryDeleteDirectory(_currentSheetTempDir);
            return success;
        }

        public string DescribeFailure() =>
            "PDF24 no generó el archivo. Abre PDF24 una vez de forma manual (menú Inicio) por si está " +
            "pidiendo aceptar un cambio de configuración, o elige otra impresora PDF (" +
            string.Join(", ", PdfPrinterCatalog.RecommendedPrinters) + ").";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Restore(Registry.CurrentUser, _userBackup);
            Restore(Registry.LocalMachine, _machineBackup);
            TryDeleteDirectory(_tempRoot);
        }

        /// <summary>Snapshot of one hive's entry before this controller touched it, for <see cref="Restore"/>.</summary>
        private sealed class HiveBackup
        {
            public bool Attempted;
            public bool KeyExistedBefore;
            public Dictionary<string, object> Values;
        }

        private HiveBackup BackupAndSwitch(RegistryKey root, bool required)
        {
            var backup = new HiveBackup();

            try
            {
                using (RegistryKey existing = root.OpenSubKey(_serviceKeyPath))
                {
                    backup.KeyExistedBefore = existing != null;
                    backup.Values = new Dictionary<string, object>();

                    if (existing != null)
                    {
                        foreach (string name in BackedUpValueNames)
                            backup.Values[name] = existing.GetValue(name);
                    }
                }

                using (RegistryKey key = root.CreateSubKey(_serviceKeyPath))
                {
                    key.SetValue("Handler", "autoSave", RegistryValueKind.String);
                    key.SetValue("AutoSaveOpenDir", 0, RegistryValueKind.DWord);
                    key.SetValue("AutoSaveOverwriteFile", 1, RegistryValueKind.DWord);
                    key.SetValue("AutoSaveShowProgress", 0, RegistryValueKind.DWord);
                    key.SetValue("AutoSaveUseFileChooser", 0, RegistryValueKind.DWord);
                    key.SetValue("AutoSaveUseFileCmd", 0, RegistryValueKind.DWord);
                }

                backup.Attempted = true;
            }
            catch (Exception ex)
            {
                if (required)
                {
                    throw new ExportSetupException(
                        "No se pudo configurar PDF24 en modo silencioso." + Environment.NewLine + Environment.NewLine +
                        @"Esto necesita escribir en HKEY_CURRENT_USER\" + _serviceKeyPath + Environment.NewLine +
                        Environment.NewLine + "y esa escritura falló: " + ex.Message, ex);
                }

                _log.Warn($"No se pudo preparar PDF24 en {root.Name} (se sigue solo con HKEY_CURRENT_USER): {ex.Message}");
            }

            return backup;
        }

        private void SetAutoSaveDir(RegistryKey root, string directory, bool required)
        {
            try
            {
                using (RegistryKey key = root.CreateSubKey(_serviceKeyPath))
                {
                    key.SetValue("AutoSaveDir", directory, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                if (required)
                {
                    throw new ExportSetupException(
                        "No se pudo indicarle a PDF24 dónde guardar esta lámina (registro): " + ex.Message, ex);
                }

                _log.Warn($"No se pudo actualizar la carpeta de PDF24 en {root.Name}: {ex.Message}");
            }
        }

        private void Restore(RegistryKey root, HiveBackup backup)
        {
            if (backup == null || !backup.Attempted) return;

            try
            {
                if (!backup.KeyExistedBefore)
                {
                    root.DeleteSubKeyTree(_serviceKeyPath, throwOnMissingSubKey: false);
                    return;
                }

                using (RegistryKey key = root.CreateSubKey(_serviceKeyPath))
                {
                    foreach (KeyValuePair<string, object> entry in backup.Values)
                    {
                        if (entry.Value == null)
                            key.DeleteValue(entry.Key, throwOnMissingValue: false);
                        else
                            key.SetValue(entry.Key, entry.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"No se pudo restaurar la configuración original de PDF24 en {root.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the <c>Software\PDF24\Services\&lt;subkey&gt;</c> entry that actually belongs to the
        /// selected printer. Critical: the subkey name is NOT the printer's display name — PDF24's
        /// own printer shows up in Windows as "PDF24" but its config sits under a subkey called just
        /// "PDF" (observed on a real install; ProSheets' own "diroots.prosheets" printer follows the
        /// same convention). The reliable link between printer and service entry is the spooler port
        /// (e.g. <c>\\.\pipe\PDFPrint</c>), which PDF24 writes into the service entry's <c>Port</c>
        /// value. Match on that; fall back to the printer name so an unusual install is not left
        /// worse off than before.
        /// </summary>
        private string ResolveServiceKeyPath(string printerName, string printerPort)
        {
            string match = FindByPort(Registry.LocalMachine, printerPort)
                        ?? FindByPort(Registry.CurrentUser, printerPort);

            if (match != null) return ServicesKey + "\\" + match;

            _log.Warn($"No se encontró la subclave de servicio de PDF24 para el puerto '{printerPort}'; " +
                      $"se usará el nombre de la impresora ('{printerName}') como respaldo.");
            return ServicesKey + "\\" + printerName;
        }

        private static string FindByPort(RegistryKey root, string port)
        {
            if (string.IsNullOrEmpty(port)) return null;

            try
            {
                using (RegistryKey services = root.OpenSubKey(ServicesKey))
                {
                    if (services == null) return null;

                    foreach (string subName in services.GetSubKeyNames())
                    {
                        using (RegistryKey sub = services.OpenSubKey(subName))
                        {
                            string subPort = sub?.GetValue("Port") as string;
                            if (string.Equals(subPort, port, StringComparison.OrdinalIgnoreCase))
                                return subName;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort lookup; caller falls back to printer name.
            }

            return null;
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception)
            {
                // Best-effort temp cleanup; never fatal.
            }
        }
    }
}
#endif
