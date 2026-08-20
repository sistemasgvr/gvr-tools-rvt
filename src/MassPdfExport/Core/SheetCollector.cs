using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>Reads sheets and saved sheet sets from the active document. Read-only, no transaction needed.</summary>
    public static class SheetCollector
    {
        public static List<ViewSheet> GetAllSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate && !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, NaturalSortComparer.Instance)
                .ToList();
        }

        /// <summary>Saved "Sheet Issue/Revision" print sets, keyed by set name, so the UI can filter by them.</summary>
        public static Dictionary<string, HashSet<ElementId>> GetSheetSets(Document doc)
        {
            var result = new Dictionary<string, HashSet<ElementId>>();

            var sheetSets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>();

            foreach (ViewSheetSet set in sheetSets)
            {
                var ids = new HashSet<ElementId>();
                foreach (View view in set.Views)
                {
                    if (view is ViewSheet)
                        ids.Add(view.Id);
                }

                if (ids.Count > 0)
                    result[set.Name] = ids;
            }

            return result;
        }

        public static SheetExportInfo ToExportInfo(ViewSheet sheet)
        {
            return new SheetExportInfo(
                sheet.Id,
                sheet.SheetNumber,
                sheet.Name,
                GetParamString(sheet, BuiltInParameter.SHEET_CURRENT_REVISION),
                GetParamString(sheet, BuiltInParameter.SHEET_CURRENT_REVISION_DESCRIPTION));
        }

        private static string GetParamString(Element element, BuiltInParameter bip)
        {
            try
            {
                Parameter p = element.get_Parameter(bip);
                return p != null && p.HasValue ? p.AsString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
