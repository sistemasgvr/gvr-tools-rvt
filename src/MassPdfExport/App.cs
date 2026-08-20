using System;
using System.Reflection;
using Autodesk.Revit.UI;
using GvrTools.MassPdfExport.Resources;

namespace GvrTools.MassPdfExport
{
    public class App : IExternalApplication
    {
        private const string TabName = "GVR Tools";
        private const string PanelName = "Exportación";

        public Result OnStartup(UIControlledApplication application)
        {
            TryCreateRibbonTab(application, TabName);
            RibbonPanel panel = FindOrCreatePanel(application, TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var buttonData = new PushButtonData(
                "cmdGvrMassPdfExport",
                "Exportar PDF\nMasivo",
                assemblyPath,
                "GvrTools.MassPdfExport.Commands.MassPdfExportCommand")
            {
                ToolTip = "Exporta láminas a PDF de forma masiva, en una carpeta con el nombre del proyecto.",
                LargeImage = RibbonIconFactory.CreateExportIcon(),
                Image = RibbonIconFactory.CreateExportIcon()
            };

            panel.AddItem(buttonData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void TryCreateRibbonTab(UIControlledApplication application, string tabName)
        {
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tab already exists — another GVR add-in created it first, which is fine.
            }
        }

        private static RibbonPanel FindOrCreatePanel(UIControlledApplication application, string tabName, string panelName)
        {
            foreach (RibbonPanel existing in application.GetRibbonPanels(tabName))
            {
                if (string.Equals(existing.Name, panelName, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            return application.CreateRibbonPanel(tabName, panelName);
        }
    }
}
