using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using GvrTools.Core.Text;
using GvrTools.Revit.Model;

namespace GvrTools.Revit.Sheets
{
    /// <summary>Read-only access to the sheets and saved sheet sets of a document.</summary>
    public static class SheetRepository
    {
        /// <summary>All printable sheets, in natural sheet-number order.</summary>
        public static IReadOnlyList<SheetSnapshot> GetSheets(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(sheet => !sheet.IsTemplate && !sheet.IsPlaceholder)
                .OrderBy(sheet => sheet.SheetNumber, NaturalSortComparer.Instance)
                .Select(Snapshot)
                .ToList();
        }

        /// <summary>Saved sheet sets (Revit's print/issue sets) that contain at least one sheet.</summary>
        public static IReadOnlyList<SheetSetSnapshot> GetSheetSets(Document document)
        {
            var sets = new List<SheetSetSnapshot>();

            IEnumerable<ViewSheetSet> sheetSets = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>();

            foreach (ViewSheetSet set in sheetSets)
            {
                var ids = new HashSet<ElementId>();

                try
                {
                    foreach (View view in set.Views)
                    {
                        if (view is ViewSheet) ids.Add(view.Id);
                    }
                }
                catch (Exception)
                {
                    // A set can reference views that no longer resolve; skip it rather than fail.
                    continue;
                }

                if (ids.Count > 0)
                    sets.Add(new SheetSetSnapshot(set.Name, ids));
            }

            return sets
                .OrderBy(set => set.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Re-resolves a snapshot to the live sheet. Returns null when the sheet no longer exists,
        /// which the exporters report as a per-sheet failure.
        /// </summary>
        public static ViewSheet Resolve(Document document, SheetSnapshot snapshot)
        {
            try
            {
                return document.GetElement(snapshot.Id) as ViewSheet;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static SheetSnapshot Snapshot(ViewSheet sheet) => new SheetSnapshot(
            sheet.Id,
            sheet.SheetNumber,
            sheet.Name,
            ReadParameter(sheet, BuiltInParameter.SHEET_CURRENT_REVISION),
            ReadParameter(sheet, BuiltInParameter.SHEET_CURRENT_REVISION_DESCRIPTION),
            ReadParameter(sheet, BuiltInParameter.SHEET_CURRENT_REVISION_DATE),
            ReadParameter(sheet, BuiltInParameter.SHEET_ISSUE_DATE));

        private static string ReadParameter(Element element, BuiltInParameter parameter)
        {
            try
            {
                Parameter found = element.get_Parameter(parameter);
                return found != null && found.HasValue ? found.AsString() ?? string.Empty : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
