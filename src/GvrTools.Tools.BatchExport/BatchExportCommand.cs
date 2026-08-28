using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.History;
using GvrTools.Core.Selections;
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
            if (LicenseRuntime.NeedsReactivation
                || !LicenseRuntime.Entitlements.CanUse(FeatureCodes.ToolBatchExport))
            {
                var hwnd = commandData.Application.MainWindowHandle;
                TaskDialog.Show(DialogTitle,
                    "No hay una licencia válida para Exportar láminas. Activa tu clave en Cuenta / Licencia.");
                RevitRestart.PendingDocumentPath = uiDocument.Document.PathName;
                try
                {
                    LicenseUi.ShowChangePlan(LicenseRuntime.Client, hwnd);
                }
                catch (Exception ex)
                {
                    // Igual que en GvrApplication.cs: un fallo mostrando la ventana de licencia
                    // no debe dejar rastro solo como una excepción genérica de Revit -- se registra
                    // y se sigue el mismo camino de "no entitled" de abajo.
                    new RollingFileLog("BatchExport").Error("No se pudo mostrar Cambiar plan desde el comando de exportación.", ex);
                }

                if (!LicenseRuntime.Entitlements.CanUse(FeatureCodes.ToolBatchExport))
                    return Result.Cancelled;

                // Ya está entitled y no lo estaba antes de abrir el diálogo: solo pudo pasar
                // activando (o cayendo a free), y eso ya programó el reinicio de Revit -- no
                // tiene sentido abrir la ventana de exportación justo antes de que Revit se cierre.
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
                    new SheetExportHistoryStore(),
                    log,
                    new SavedSelectionStore());

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
