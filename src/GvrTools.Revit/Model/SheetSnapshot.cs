using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace GvrTools.Revit.Model
{
    /// <summary>
    /// Plain, immutable copy of the sheet data the UI and the exporters need, read once while the
    /// Revit API is safe to touch.
    ///
    /// Only the <see cref="Id"/> is kept, never the live <see cref="ViewSheet"/>: element objects
    /// can go stale (undo, reload, worksharing sync) between opening the window and pressing
    /// Export, and re-resolving from the id at export time turns that into a clean per-sheet error
    /// instead of an exception from deep inside the API.
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
            string issueDate)
        {
            Id = id;
            UniqueId = uniqueId ?? string.Empty;
            Number = number ?? string.Empty;
            Name = name ?? string.Empty;
            RevisionNumber = revisionNumber ?? string.Empty;
            RevisionDescription = revisionDescription ?? string.Empty;
            RevisionDate = revisionDate ?? string.Empty;
            IssueDate = issueDate ?? string.Empty;
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

        /// <summary>"A-101 - Planta primer nivel", used in progress text and error reports.</summary>
        public string Label => string.IsNullOrEmpty(Name) ? Number : Number + " - " + Name;

        /// <summary>Values for the sheet-scoped file-name tokens.</summary>
        public IReadOnlyDictionary<string, string> ToTokens() => new Dictionary<string, string>
        {
            ["SheetNumber"] = Number,
            ["SheetName"] = Name,
            ["RevisionNumber"] = RevisionNumber,
            ["RevisionDescription"] = RevisionDescription,
            ["RevisionDate"] = RevisionDate,
            ["SheetIssueDate"] = IssueDate
        };
    }
}
