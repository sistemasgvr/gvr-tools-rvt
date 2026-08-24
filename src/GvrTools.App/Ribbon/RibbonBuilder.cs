using System;
using System.Collections.Generic;
using System.Windows.Media;
using Autodesk.Revit.UI;
using GvrTools.Core.Diagnostics;
using GvrTools.Revit.Ribbon;

namespace GvrTools.App.Ribbon
{
    /// <summary>
    /// Turns a list of tools into ribbon buttons: creates the tab once, creates each panel on
    /// demand, and adds one button per tool.
    /// </summary>
    public sealed class RibbonBuilder
    {
        private readonly UIControlledApplication _application;
        private readonly string _tabName;
        private readonly ILog _log;
        private readonly Dictionary<string, RibbonPanel> _panels = new Dictionary<string, RibbonPanel>(StringComparer.OrdinalIgnoreCase);

        public RibbonBuilder(UIControlledApplication application, string tabName, ILog log)
        {
            _application = application;
            _tabName = tabName;
            _log = log;

            CreateTab();
        }

        /// <summary>Adds one button. Returns false when Revit rejected it, which is logged but not fatal.</summary>
        public bool Add(IRevitTool tool)
        {
            try
            {
                RibbonPanel panel = ResolvePanel(tool.PanelName);

                // Revit resolves the class name inside the assembly it is given, and a tool's
                // command lives in the tool's own assembly, not in the host's.
                string commandAssembly = tool.CommandType.Assembly.Location;

                var data = new PushButtonData(tool.Id, tool.Title, commandAssembly, tool.CommandType.FullName)
                {
                    ToolTip = tool.Tooltip
                };

                if (!string.IsNullOrWhiteSpace(tool.LongDescription))
                    data.LongDescription = tool.LongDescription;

                ImageSource icon = SafeIcon(tool);
                if (icon != null)
                {
                    data.LargeImage = icon;
                    data.Image = icon;
                }

                panel.AddItem(data);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"No se pudo agregar el botón de la herramienta '{tool.Id}'.", ex);
                return false;
            }
        }

        private void CreateTab()
        {
            try
            {
                _application.CreateRibbonTab(_tabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The tab already exists, which is the expected case when another GVR add-in
                // (or a previous load of this one) created it first.
            }
        }

        private RibbonPanel ResolvePanel(string panelName)
        {
            string name = string.IsNullOrWhiteSpace(panelName) ? "General" : panelName;

            if (_panels.TryGetValue(name, out RibbonPanel cached)) return cached;

            foreach (RibbonPanel existing in _application.GetRibbonPanels(_tabName))
            {
                if (!string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

                _panels[name] = existing;
                return existing;
            }

            RibbonPanel created = _application.CreateRibbonPanel(_tabName, name);
            _panels[name] = created;
            return created;
        }

        /// <summary>A tool that cannot draw its icon still gets its button.</summary>
        private ImageSource SafeIcon(IRevitTool tool)
        {
            try
            {
                return tool.CreateIcon();
            }
            catch (Exception ex)
            {
                _log.Warn($"El ícono de '{tool.Id}' no se pudo generar: {ex.Message}");
                return null;
            }
        }
    }
}
