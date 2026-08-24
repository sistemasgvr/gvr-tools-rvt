using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using GvrTools.Core.Diagnostics;
using GvrTools.Revit.Ribbon;

namespace GvrTools.App.Ribbon
{
    /// <summary>
    /// Finds the tools to put on the ribbon.
    ///
    /// Discovery is by convention: every assembly named <c>GvrTools.Tools.*.dll</c> sitting next to
    /// the add-in is scanned for public, concrete <see cref="IRevitTool"/> implementations with a
    /// parameterless constructor. That is what makes the suite scale — a new tool is a new project,
    /// not an edit to a registration list that everybody has to remember to update.
    ///
    /// One misbehaving assembly must never cost the whole ribbon, so every step is isolated and
    /// logged rather than allowed to escape into Revit's start-up.
    /// </summary>
    public static class ToolCatalog
    {
        private const string ToolAssemblyPattern = "GvrTools.Tools.*.dll";

        public static IReadOnlyList<IRevitTool> Discover(UIControlledApplication application, ILog log)
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var tools = new List<IRevitTool>();

            foreach (Assembly assembly in LoadToolAssemblies(directory, log))
                tools.AddRange(InstantiateTools(assembly, application, log));

            return tools
                .OrderBy(tool => tool.PanelName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(tool => tool.SortOrder)
                .ThenBy(tool => tool.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static IEnumerable<Assembly> LoadToolAssemblies(string directory, ILog log)
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(directory, ToolAssemblyPattern);
            }
            catch (Exception ex)
            {
                log.Error($"No se pudo listar las herramientas en '{directory}'.", ex);
                yield break;
            }

            foreach (string file in files)
            {
                Assembly assembly = null;

                try
                {
                    assembly = Assembly.LoadFrom(file);
                }
                catch (Exception ex)
                {
                    log.Error($"No se pudo cargar la herramienta '{Path.GetFileName(file)}'.", ex);
                }

                if (assembly != null) yield return assembly;
            }
        }

        private static IEnumerable<IRevitTool> InstantiateTools(Assembly assembly, UIControlledApplication application, ILog log)
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // A partially loadable assembly still contributes the types that did resolve.
                log.Warn($"'{assembly.GetName().Name}' se cargó parcialmente: {ex.Message}");
                types = ex.Types.Where(type => type != null).ToArray();
            }
            catch (Exception ex)
            {
                log.Error($"No se pudieron leer los tipos de '{assembly.GetName().Name}'.", ex);
                yield break;
            }

            foreach (Type type in types.Where(IsInstantiableTool))
            {
                IRevitTool tool = null;

                try
                {
                    tool = (IRevitTool)Activator.CreateInstance(type);

                    if (!tool.IsSupported(application))
                    {
                        log.Info($"La herramienta '{tool.Id}' no es compatible con esta versión de Revit; se omite.");
                        tool = null;
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"No se pudo crear la herramienta '{type.FullName}'.", ex);
                }

                if (tool != null) yield return tool;
            }
        }

        private static bool IsInstantiableTool(Type type) =>
            typeof(IRevitTool).IsAssignableFrom(type) &&
            !type.IsAbstract &&
            !type.IsInterface &&
            type.IsPublic &&
            type.GetConstructor(Type.EmptyTypes) != null;
    }
}
