using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace GvrTools.Revit.Model
{
    /// <summary>Whether a <see cref="SheetSnapshot"/> stands for a sheet or a standalone view.</summary>
    public enum ExportItemKind
    {
        Sheet,
        View
    }

    /// <summary>
    /// Plain, immutable copy of the sheet (or, since the views-export feature, standalone view) data
    /// the UI and the exporters need, read once while the Revit API is safe to touch.
    ///
    /// Only the <see cref="Id"/> is kept, never the live element: element objects can go stale (undo,
    /// reload, worksharing sync) between opening the window and pressing Export, and re-resolving
    /// from the id at export time turns that into a clean per-item error instead of an exception from
    /// deep inside the API.
    ///
    /// Views reuse this same type instead of a parallel class: every consumer downstream (the naming
    /// engine, the export history store, the 3 export engines, the selection grid) only ever needs a
    /// name, an id and a token dictionary, and duplicating all of that for a second type would be far
    /// more code and risk than one optional <see cref="Kind"/> discriminator. For a view, only
    /// <see cref="Name"/> and <see cref="ViewTypeLabel"/> are populated; the sheet-only fields
    /// (<see cref="Number"/>, revision, issue date) stay empty.
    /// </summary>
    public sealed class SheetSnapshot
    {
        public SheetSnapshot(
            ElementId id,
            string uniqueId,
            string number,
            string name,
            string revisionNumber,
            string revisionDescription,
            string revisionDate,
            string issueDate,
            ExportItemKind kind = ExportItemKind.Sheet,
            string viewTypeLabel = null)
        {
            Id = id;
            UniqueId = uniqueId ?? string.Empty;
            Number = number ?? string.Empty;
            Name = name ?? string.Empty;
            RevisionNumber = revisionNumber ?? string.Empty;
            RevisionDescription = revisionDescription ?? string.Empty;
            RevisionDate = revisionDate ?? string.Empty;
            IssueDate = issueDate ?? string.Empty;
            Kind = kind;
            ViewTypeLabel = viewTypeLabel ?? string.Empty;
        }

        public ElementId Id { get; }

        /// <summary>
        /// Revit's <c>Element.UniqueId</c> (a GUID persisted in the file), stable across sessions
        /// unlike <see cref="Id"/> -- this is the key the export-history store uses to remember
        /// "was this sheet exported before" from one Revit session to the next.
        /// </summary>
        public string UniqueId { get; }

        public string Number { get; }

        public string Name { get; }

        public string RevisionNumber { get; }

        public string RevisionDescription { get; }

        public string RevisionDate { get; }

        public string IssueDate { get; }

        /// <summary>Sheet by default; <see cref="ExportItemKind.View"/> for a standalone view.</summary>
        public ExportItemKind Kind { get; }

        /// <summary>Revit's <c>ViewType</c> as text ("FloorPlan", "Section", ...). Empty for sheets.</summary>
        public string ViewTypeLabel { get; }

        /// <summary>"A-101 - Planta primer nivel" for a sheet, or just the view name for a view.</summary>
        public string Label => Kind == ExportItemKind.View
            ? Name
            : (string.IsNullOrEmpty(Name) ? Number : Number + " - " + Name);

        /// <summary>Values for the sheet- and view-scoped file-name tokens.</summary>
        public IReadOnlyDictionary<string, string> ToTokens() => new Dictionary<string, string>
        {
            ["SheetNumber"] = Number,
            ["SheetName"] = Name,
            ["RevisionNumber"] = RevisionNumber,
            ["RevisionDescription"] = RevisionDescription,
            ["RevisionDate"] = RevisionDate,
            ["SheetIssueDate"] = IssueDate,
            ["ViewName"] = Kind == ExportItemKind.View ? Name : string.Empty,
            ["ViewType"] = ViewTypeLabel
        };
    }
}
