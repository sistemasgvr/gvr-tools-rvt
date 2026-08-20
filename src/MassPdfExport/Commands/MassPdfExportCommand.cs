using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GvrTools.MassPdfExport.Core;
using GvrTools.MassPdfExport.UI;

namespace GvrTools.MassPdfExport.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MassPdfExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No hay ningún documento de Revit activo.";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;

            List<ViewSheet> sheets = SheetCollector.GetAllSheets(doc);
            if (sheets.Count == 0)
            {
                TaskDialog.Show("GVR Tools", "El proyecto activo no tiene láminas para exportar.");
                return Result.Cancelled;
            }

            Dictionary<string, HashSet<ElementId>> sheetSets = SheetCollector.GetSheetSets(doc);

            try
            {
                var viewModel = new MainViewModel(uiDoc, sheets, sheetSets);
                var window = new MainWindow(viewModel);
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
