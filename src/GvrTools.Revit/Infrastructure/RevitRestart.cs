using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GvrTools.Revit.Infrastructure
{
    /// <summary>
    /// Cierra Revit y lo vuelve a abrir tras activar licencia. Autodesk no expone salida/reinicio en
    /// la API; el cierre usa la ventana principal (respeta guardar) y el relanzado va en un script
    /// auxiliar que espera a que termine el proceso actual.
    /// </summary>
    public static class RevitRestart
    {
        private const uint WmClose = 0x0010;

        /// <summary>Proyecto .rvt a reabrir tras reiniciar (opcional; solo si está guardado en disco).</summary>
        public static string PendingDocumentPath { get; set; }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static void RequestCloseAndRestart()
        {
            string revitExe = ResolveRevitExecutable();
            string documentPath = PendingDocumentPath;
            PendingDocumentPath = null;

            if (!string.IsNullOrEmpty(revitExe))
                ScheduleRestart(revitExe, documentPath);

            RequestClose();
        }

        private static void RequestClose()
        {
            Process process = Process.GetCurrentProcess();
            IntPtr handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
                return;

            if (!process.CloseMainWindow())
                SendMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        private static string ResolveRevitExecutable()
        {
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

            script.AppendLine("while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }");
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
