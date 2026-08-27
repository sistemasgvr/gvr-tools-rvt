using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace GvrTools.Revit.Export.Dwg
{
    /// <summary>
    /// Muchos proyectos ya traen sus propias configuraciones de exportación DWG (Administrar →
    /// Configuraciones de exportación → DWG/DXF), con mapeo de capas, grosores de línea, etc. que
    /// nuestro propio panel de opciones no cubre. En vez de obligar a redefinir todo eso a mano,
    /// se puede elegir una de esas configuraciones ya guardadas y usarla tal cual.
    /// </summary>
    public static class DwgExportSetupCatalog
    {
        /// <summary>Nombres de las configuraciones DWG guardadas en el documento, vacío si no hay ninguna.</summary>
        public static IReadOnlyList<string> ListNames(Document document)
        {
            if (document == null) return new List<string>();

            try
            {
                return (IReadOnlyList<string>)ExportDWGSettings.ListNames(document) ?? new List<string>();
            }
            catch
            {
                // Documentos muy antiguos o sin ninguna configuración creada: no romper el arranque de la tool por esto.
                return new List<string>();
            }
        }

        /// <summary>La configuración marcada como predeterminada en el proyecto (Administrar → Configuraciones DWG), o null si no hay ninguna.</summary>
        public static string TryGetActiveName(Document document)
        {
            if (document == null) return null;

            try
            {
                return ExportDWGSettings.GetActivePredefinedSettings(document)?.Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Las opciones reales de esa configuración guardada, o null si el nombre ya no existe (se borró en Revit).</summary>
        public static DWGExportOptions TryGetOptions(Document document, string name)
        {
            if (document == null || string.IsNullOrWhiteSpace(name)) return null;

            try
            {
                return ExportDWGSettings.FindByName(document, name)?.GetDWGExportOptions();
            }
            catch
            {
                return null;
            }
        }
    }
}
