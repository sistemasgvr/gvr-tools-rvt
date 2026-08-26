using System.Collections.Generic;

namespace GvrTools.Revit.Export
{
    /// <summary>One selectable starting point for the file-naming pattern box.</summary>
    public sealed class NamingPreset
    {
        public NamingPreset(string label, string pattern)
        {
            Label = label;
            Pattern = pattern;
        }

        public string Label { get; }

        public string Pattern { get; }
    }

    /// <summary>
    /// Common sheet-naming conventions seen in AEC practice, offered as a starting point for the
    /// pattern box -- not a claim of compliance with any specific published standard (ISO 19650's
    /// container-naming scheme needs fields like discipline/originator/volume codes that Revit does
    /// not expose per sheet by default, so a single fixed preset could not honestly be labelled
    /// "ISO 19650"). Picking one just fills the pattern box; it stays fully editable afterwards.
    /// </summary>
    public static class NamingPresets
    {
        public static readonly IReadOnlyList<NamingPreset> All = new[]
        {
            new NamingPreset("Número - Nombre", NamingTokens.DefaultPattern),
            new NamingPreset("Solo el número", "{SheetNumber}"),
            new NamingPreset("Proyecto_Número_Nombre", "{ProjectNumber}_{SheetNumber}_{SheetName}"),
            new NamingPreset("Número_Rev_Nombre", "{SheetNumber}_Rev{RevisionNumber}_{SheetName}"),
            new NamingPreset("Fecha_Número_Nombre", "{Date}_{SheetNumber}_{SheetName}")
        };
    }
}
