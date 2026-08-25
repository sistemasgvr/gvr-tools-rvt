using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using GvrTools.App.Account;
using GvrTools.App.Ribbon;
using GvrTools.Core.Diagnostics;
using GvrTools.Licensing;
using GvrTools.Licensing.Entitlements;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Ribbon;

namespace GvrTools.App
{
    /// <summary>
    /// The add-in Revit loads.
    ///
    /// It contains no tool logic at all: it creates the GVR Tools tab, asks
    /// <see cref="ToolCatalog"/> what tools exist and hands them to <see cref="RibbonBuilder"/>.
    /// Adding, removing or reordering tools therefore never touches this file for product tools;
    /// the Account button is registered here because it lives in the host assembly.
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

                LicenseRuntime.EnsureInitialized();
                // Heartbeat en background: no bloquear la cinta más de ~2.5s (LicenseRuntime.WarmupAsync).
                Task.Run(() => LicenseRuntime.WarmupAsync());

                IReadOnlyList<IRevitTool> discovered = ToolCatalog.Discover(application, log);
                var tools = new List<IRevitTool> { new AccountTool() };
                tools.AddRange(FilterByEntitlement(discovered, log));

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

                log.Info($"Cinta lista: {added} de {tools.Count} herramienta(s) agregadas. " +
                         $"Licencia: {(LicenseRuntime.IsLicensed ? LicenseRuntime.Client.PlanCode : "sin licencia válida")}.");
            }
            catch (Exception ex)
            {
                log.Error("Error al construir la cinta de GVR Tools.", ex);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static List<IRevitTool> FilterByEntitlement(IReadOnlyList<IRevitTool> tools, ILog log)
        {
            var result = new List<IRevitTool>(tools.Count);
            IEntitlementService entitlements = LicenseRuntime.Entitlements;

            foreach (IRevitTool tool in tools)
            {
                string feature = tool.RequiredFeature;
                if (string.IsNullOrWhiteSpace(feature) || entitlements.CanUse(feature))
                {
                    result.Add(tool);
                    continue;
                }

                log.Info($"Herramienta '{tool.Id}' oculta: falta feature '{feature}'.");
            }

            return result;
        }
    }
}
