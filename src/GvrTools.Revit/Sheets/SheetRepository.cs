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
        /// Printable standalone views (plans, sections, elevations, 3D, drafting...), excluding
        /// sheets, templates and schedules. Schedules are left out for now: they are tabular, not
        /// geometry, and neither Revit's PDF exporter nor <c>DWGExportOptions</c> treats them like a
        /// normal view -- a later pass can add them as their own kind if there is demand.
        /// </summary>
        public static IReadOnlyList<SheetSnapshot> GetViews(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(view => !view.IsTemplate
                    && !(view is ViewSheet)
                    && !(view is ViewSchedule)
                    && IsPrintable(view))
                .OrderBy(view => view.Name, NaturalSortComparer.Instance)
                .Select(ViewSnapshot)
                .ToList();
        }

        /// <summary>
        /// Re-resolves a snapshot (sheet or view) to the live <see cref="View"/>. Returns null when
        /// the element no longer exists or is no longer a view, reported as a per-item failure.
        /// </summary>
        public static View ResolveView(Document document, SheetSnapshot snapshot)
        {
            try
            {
                return document.GetElement(snapshot.Id) as View;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// <c>View.CanBePrinted</c> throws for a handful of view kinds instead of returning false
        /// (e.g. some legend/internal views on older API versions), so this is defensive rather than
        /// a direct property read.
        /// </summary>
        private static bool IsPrintable(View view)
        {
            try
            {
                return view.CanBePrinted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static SheetSnapshot ViewSnapshot(View view) => new SheetSnapshot(
            view.Id,
            view.UniqueId,
            string.Empty,
            view.Name,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            ExportItemKind.View,
            view.ViewType.ToString());

        private static SheetSnapshot Snapshot(ViewSheet sheet) => new SheetSnapshot(
            sheet.Id,
            sheet.UniqueId,
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
