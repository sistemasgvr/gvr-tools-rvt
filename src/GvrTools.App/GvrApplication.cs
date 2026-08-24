using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using GvrTools.App.Ribbon;
using GvrTools.Core.Diagnostics;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Ribbon;

namespace GvrTools.App
{
    /// <summary>
    /// The add-in Revit loads.
    ///
    /// It contains no tool logic at all: it creates the GVR Tools tab, asks
    /// <see cref="ToolCatalog"/> what tools exist and hands them to <see cref="RibbonBuilder"/>.
    /// Adding, removing or reordering tools therefore never touches this file.
    ///
    /// Start-up never throws. A failure here would surface to the user as Revit complaining about a
    /// broken add-in on every launch, so problems are logged and the load is reported as succeeded
    /// with whatever tools did work.
    /// </summary>
    public class GvrApplication : IExternalApplication
    {
        public const string TabName = "GVR Tools";

        public Result OnStartup(UIControlledApplication application)
        {
            var log = new RollingFileLog("App");

            try
            {
                log.Info($"Iniciando GVR Tools (compilado para Revit {RevitVersionInfo.CompiledFor}, " +
                         $"PDF nativo: {RevitVersionInfo.HasNativePdfExport}).");

                IReadOnlyList<IRevitTool> tools = ToolCatalog.Discover(application, log);

                if (tools.Count == 0)
                {
                    log.Warn("No se encontró ninguna herramienta para agregar a la cinta.");
                    return Result.Succeeded;
                }

                var builder = new RibbonBuilder(application, TabName, log);
                int added = 0;

                foreach (IRevitTool tool in tools)
                {
                    if (builder.Add(tool)) added++;
                }

                log.Info($"Cinta lista: {added} de {tools.Count} herramienta(s) agregadas.");
            }
            catch (Exception ex)
            {
                log.Error("Error al construir la cinta de GVR Tools.", ex);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
