using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using GvrTools.App.Account;
using GvrTools.App.Ribbon;
using GvrTools.Core.Diagnostics;
using GvrTools.Licensing;
using GvrTools.Licensing.Activation;
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
                RevitRestart.Bind(application);
                LicenseUi.RequestApplicationClose = RevitRestart.RequestCloseAndRestart;
                var uiContext = SynchronizationContext.Current;

                void ShowRevokedUi(string reason)
                {
                    try
                    {
                        TaskDialog.Show(
                            "GVR Tools · Licencia",
                            reason + "\n\nLas herramientas dejan de estar disponibles hasta que actives de nuevo.");
                        LicenseUi.ShowActivate(LicenseRuntime.Client, default, reason);
                    }
                    catch (Exception ex)
                    {
                        log.Warn("No se pudo mostrar reactivación tras kick: " + ex.Message);
                    }
                }

                // Validar cache local contra el servidor antes de armar la cinta (evita licencia fantasma).
                try
                {
                    Task.Run(async () => await LicenseRuntime.WarmupAsync().ConfigureAwait(false))
                        .Wait(TimeSpan.FromSeconds(9));
                }
                catch (Exception ex)
                {
                    log.Warn("Warmup de licencia no completado: " + ex.Message);
                }

                if (LicenseRuntime.NeedsReactivation)
                {
                    var reason = LicenseRuntime.ReactivationReason
                        ?? "Sesión de licencia expirada. Vuelve a activar con tu clave de licencia.";
                    try
                    {
                        LicenseUi.ShowActivate(LicenseRuntime.Client, default, reason);
                    }
                    catch (Exception ex)
                    {
                        log.Warn("No se pudo mostrar reactivación al arranque: " + ex.Message);
                    }
                }

                // Watch periódico + aviso de updates en background (warmup ya corrió arriba).
                Task.Run(async () =>
                {
                    void StartWatch()
                    {
                        LicenseRuntime.StartSessionWatch(uiContext, ShowRevokedUi);
                    }

                    if (uiContext != null)
                        uiContext.Post(_ => StartWatch(), null);
                    else
                        StartWatch();

                    var current = AddInVersion.Current;
                    var update = await LicenseRuntime.TryCheckForUpdateAsync(
                        current,
                        RevitVersionInfo.CompiledFor.ToString()).ConfigureAwait(false);

                    if (update == null || !update.UpdateAvailable)
                        return;

                    void ShowUpdate()
                    {
                        try
                        {
                            LicenseUi.ShowUpdateAvailable(update, current, LicenseRuntime.Client);
                        }
                        catch (Exception ex)
                        {
                            log.Warn("No se pudo mostrar el aviso de actualización: " + ex.Message);
                        }
                    }

                    if (uiContext != null)
                        uiContext.Post(_ => ShowUpdate(), null);
                    else
                        ShowUpdate();
                });

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

        public Result OnShutdown(UIControlledApplication application)
        {
            LicenseRuntime.StopSessionWatch();
            return Result.Succeeded;
        }

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
