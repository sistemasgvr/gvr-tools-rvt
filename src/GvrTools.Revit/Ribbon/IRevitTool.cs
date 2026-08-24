using System;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace GvrTools.Revit.Ribbon
{
    /// <summary>
    /// One button on the GVR Tools ribbon.
    ///
    /// This is the whole extension contract of the suite: a new tool implements this interface in
    /// its own assembly, and the host application discovers it, places it on the right panel and
    /// wires the button to its command. No file in GvrTools.App has to change to add a tool.
    /// </summary>
    public interface IRevitTool
    {
        /// <summary>
        /// Stable, unique identifier. Revit uses it as the internal button name, so it must not
        /// change once shipped (keyboard shortcuts and ribbon customisations are keyed on it).
        /// </summary>
        string Id { get; }

        /// <summary>Ribbon label. A line break splits it across two lines on the button.</summary>
        string Title { get; }

        /// <summary>Panel the button belongs to; panels are created on demand and shared.</summary>
        string PanelName { get; }

        /// <summary>Order within the panel, lowest first. Ties fall back to the title.</summary>
        int SortOrder { get; }

        string Tooltip { get; }

        /// <summary>Extended help shown in Revit's expanded tooltip. Optional.</summary>
        string LongDescription { get; }

        /// <summary>The <see cref="IExternalCommand"/> the button runs.</summary>
        Type CommandType { get; }

        /// <summary>Button icon, or null to fall back to the suite's generic icon.</summary>
        ImageSource CreateIcon();

        /// <summary>
        /// Lets a tool opt out on hosts it cannot support (a Revit release that lacks an API it
        /// needs, a missing external dependency...). Returning false simply omits the button.
        /// </summary>
        bool IsSupported(UIControlledApplication application);
    }
}
