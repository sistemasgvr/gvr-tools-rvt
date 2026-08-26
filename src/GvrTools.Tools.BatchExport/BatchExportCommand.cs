using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.Settings;
using GvrTools.Licensing;
using GvrTools.Licensing.Activation;
using GvrTools.Licensing.Entitlements;
using GvrTools.Revit.Infrastructure;
using GvrTools.Tools.BatchExport.ViewModels;
using GvrTools.Tools.BatchExport.Views;
using GvrTools.UI.Services;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// Entry point of the tool: opens the exporter window and returns immediately.
    ///
    /// The command owns the scheduler because <see cref="ExternalEvent.Create"/> has to be called
    /// from a valid Revit API context, and a command execution is one. Both scheduler and window
    /// live until the user closes the window, which is why the single live instance is tracked in a
    /// static field: a modeless window that nothing references would be collected out from under
    /// the user.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchExportCommand : IExternalCommand
    {
        private const string DialogTitle = "GVR Tools - Exportación masiva";

        private static BatchExportWindow _openWindow;
        private static RevitJobScheduler _scheduler;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;

            if (uiDocument?.Document == null)
            {
                TaskDialog.Show(DialogTitle, "Abre un proyecto de Revit antes de usar esta herramienta.");
                return Result.Cancelled;
            }

            if (uiDocument.Document.IsFamilyDocument)
            {
                TaskDialog.Show(DialogTitle, "Esta herramienta funciona sobre proyectos, no sobre familias.");
                return Result.Cancelled;
            }

            LicenseRuntime.EnsureInitialized();
            if (!LicenseRuntime.Entitlements.CanUse(FeatureCodes.ToolBatchExport))
            {
                var hwnd = commandData.Application.MainWindowHandle;
                TaskDialog.Show(DialogTitle,
                    "No hay una licencia válida para Exportar láminas. Activa tu clave GVR-… en Cuenta / Licencia.");
                RevitRestart.PendingDocumentPath = uiDocument.Document.PathName;
                bool? accepted = LicenseUi.ShowActivate(LicenseRuntime.Client, hwnd);
                if (!LicenseRuntime.Entitlements.CanUse(FeatureCodes.ToolBatchExport))
                    return Result.Cancelled;

                // Activación ok → reinicio de Revit ya programado; no abrir la ventana ahora.
                if (accepted == true)
                    return Result.Succeeded;
            }

            if (_openWindow != null)
            {
                _openWindow.Activate();
                return Result.Succeeded;
            }

            var log = new RollingFileLog("BatchExport");

            try
            {
                _scheduler = new RevitJobScheduler(log);

                var viewModel = new BatchExportViewModel(
                    uiDocument,
                    _scheduler,
                    new WindowsUserDialogs(),
                    new FlatFileSettingsStore(),
                    log);

                _openWindow = new BatchExportWindow(viewModel, ReleaseWindow);
                new WindowInteropHelper(_openWindow).Owner = commandData.Application.MainWindowHandle;
                _openWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                log.Error("No se pudo abrir la ventana de exportación masiva.", ex);
                ReleaseWindow();

                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ReleaseWindow()
        {
            _openWindow = null;

            _scheduler?.Dispose();
            _scheduler = null;
        }
    }
}
