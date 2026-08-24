using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace GvrTools.Revit.Model
{
    /// <summary>A saved sheet set from the project, used by the UI as a ready-made selection filter.</summary>
    public sealed class SheetSetSnapshot
    {
        public SheetSetSnapshot(string name, ISet<ElementId> sheetIds)
        {
            Name = name;
            SheetIds = sheetIds;
        }

        public string Name { get; }

        public ISet<ElementId> SheetIds { get; }

        public int Count => SheetIds.Count;

        public override string ToString() => Name;
    }
}
