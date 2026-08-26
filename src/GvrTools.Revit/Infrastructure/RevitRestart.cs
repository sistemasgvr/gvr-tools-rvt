using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using GvrTools.Core.Diagnostics;

namespace GvrTools.Revit.Infrastructure
{
    /// <summary>
    /// Cierra Revit y lo vuelve a abrir tras activar licencia.
    /// Preferencia: PostableCommand.ExitRevit en el siguiente Idling (salida ordenada de Revit).
    /// Fallback: PostMessage(WM_CLOSE) solo cuando ya no hay diálogos modales.
    /// Nunca SendMessage ni CloseMainWindow desde un ShowDialog WPF (error irrecuperable).
    /// </summary>
    public static class RevitRestart
    {
        private const uint WmClose = 0x0010;

        private static UIControlledApplication _controlledApp;
        private static bool _exitSubscribed;

        /// <summary>Proyecto .rvt a reabrir tras reiniciar (opcional; solo si está guardado en disco).</summary>
        public static string PendingDocumentPath { get; set; }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Registrar en OnStartup para poder usar Idling + ExitRevit.</summary>
        public static void Bind(UIControlledApplication application)
        {
            _controlledApp = application;
        }

        /// <summary>
        /// Programa el relanzado y pide el cierre cuando Revit esté idle
        /// (después de que todos los ShowDialog / MessageBox / ExternalCommand hayan terminado).
        ///
        /// Se llama siempre en diferido (Dispatcher.BeginInvoke desde LicenseUi), fuera de la pila
        /// del comando/diálogo que originó la activación -- ningún try/catch de más arriba puede
        /// atraparla ya. Sin este try/catch propio, un fallo de E/S al escribir el script de
        /// reinicio, o al lanzar powershell.exe, tumbaría Revit entero en vez de solo avisar que
        /// el reinicio automático no se pudo hacer.
        /// </summary>
        public static void RequestCloseAndRestart()
        {
            try
            {
                string revitExe = ResolveRevitExecutable();
                string documentPath = PendingDocumentPath;
                PendingDocumentPath = null;

                if (!string.IsNullOrEmpty(revitExe))
                {
                    ScheduleRestart(revitExe, documentPath);
                }
                else
                {
                    TaskDialog.Show(
                        "GVR Tools · Reiniciar Revit",
                        "La licencia se activó, pero no se pudo localizar Revit.exe para reabrirlo automáticamente "
                        + "(instalación fuera de la ruta habitual).\n\n"
                        + "Revit se cerrará. Ábrelo de nuevo manualmente desde el acceso que uses en este PC.");
                }

                ScheduleExit();
            }
            catch (Exception ex)
            {
                LogFailure("No se pudo programar el reinicio de Revit tras activar la licencia.", ex);
            }
        }

        private static void LogFailure(string message, Exception ex)
        {
            try
            {
                new RollingFileLog("App").Error(message, ex);
            }
            catch
            {
                // el logging nunca debe ser la razón de un segundo fallo.
            }
        }

        private static void ScheduleExit()
        {
            if (_controlledApp != null)
            {
                if (_exitSubscribed)
                    return;

                _exitSubscribed = true;
                _controlledApp.Idling += OnIdlingExitOnce;
                return;
            }

            var dispatcher = Dispatcher.CurrentDispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                PostCloseToMainWindow();
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(PostCloseToMainWindow));
        }

        /// <summary>
        /// Manejador del evento Idling de Revit: si algo de aquí lanza sin atrapar, Revit no lo
        /// absorbe como haría con un IExternalCommand.Execute -- una excepción sin manejar dentro
        /// de un handler de un evento de la API es justo el tipo de cosa que produce el "error
        /// irrecuperable" de Revit, así que este método nunca debe dejar escapar una excepción.
        /// </summary>
        private static void OnIdlingExitOnce(object sender, IdlingEventArgs e)
        {
            try
            {
                if (_controlledApp != null)
                    _controlledApp.Idling -= OnIdlingExitOnce;
                _exitSubscribed = false;

                var uiApp = sender as UIApplication;
                if (TryPostExitRevit(uiApp))
                    return;

                PostCloseToMainWindow();
            }
            catch (Exception ex)
            {
                LogFailure("El cierre programado de Revit falló dentro del evento Idling.", ex);
            }
        }

        private static bool TryPostExitRevit(UIApplication uiApp)
        {
            if (uiApp == null)
                return false;

            try
            {
                RevitCommandId commandId = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                if (commandId == null || !uiApp.CanPostCommand(commandId))
                    return false;

                uiApp.PostCommand(commandId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PostCloseToMainWindow()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero)
                    return;

                PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LogFailure("No se pudo cerrar la ventana principal de Revit.", ex);
            }
        }

        private static string ResolveRevitExecutable()
        {
            try
            {
                string host = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(host)
                    && host.EndsWith("Revit.exe", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(host))
                {
                    return host;
                }
            }
            catch
            {
                // MainModule puede fallar por permisos; caemos al path por defecto.
            }

            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Autodesk",
                "Revit " + RevitVersionInfo.CompiledFor,
                "Revit.exe");

            return File.Exists(path) ? path : null;
        }

        private static void ScheduleRestart(string revitExe, string documentPath)
        {
            int pid = Process.GetCurrentProcess().Id;
            string scriptPath = Path.Combine(Path.GetTempPath(), "gvr-restart-revit-" + Guid.NewGuid().ToString("N") + ".ps1");

            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            script.AppendLine("$pidToWait = " + pid);
            script.AppendLine("$revitExe = " + ToPowerShellLiteral(revitExe));

            if (!string.IsNullOrWhiteSpace(documentPath) && File.Exists(documentPath))
                script.AppendLine("$documentPath = " + ToPowerShellLiteral(documentPath));
            else
                script.AppendLine("$documentPath = $null");

            script.AppendLine("$deadline = (Get-Date).AddMinutes(10)");
            script.AppendLine("while ((Get-Date) -lt $deadline -and (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 400 }");
            script.AppendLine("if (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {");
            script.AppendLine("  Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force");
            script.AppendLine("  exit 0");
            script.AppendLine("}");
            script.AppendLine("Start-Sleep -Milliseconds 800");
            script.AppendLine("if ($documentPath) {");
            script.AppendLine("  Start-Process -FilePath $revitExe -ArgumentList @($documentPath)");
            script.AppendLine("} else {");
            script.AppendLine("  Start-Process -FilePath $revitExe");
            script.AppendLine("}");
            script.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            Process.Start(psi);
        }

        private static string ToPowerShellLiteral(string value)
        {
            if (value == null) return "$null";
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
