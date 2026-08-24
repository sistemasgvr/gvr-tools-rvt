using System.Collections.Generic;
using System.Linq;

namespace GvrTools.Revit.Export
{
    /// <summary>One placeholder the user can put in the file-name pattern.</summary>
    public sealed class NamingToken
    {
        public NamingToken(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }

        public string Description { get; }

        /// <summary>The token as typed in a pattern, e.g. <c>{SheetNumber}</c>.</summary>
        public string Placeholder => "{" + Name + "}";
    }

    /// <summary>
    /// The tokens the sheet exporters understand, in the order they are offered in the UI.
    ///
    /// Single source of truth: <see cref="ExportFileNamer"/> resolves exactly these, and the window
    /// builds its help text from the same list, so a new token can never be documented but
    /// unimplemented (or the other way round).
    /// </summary>
    public static class NamingTokens
    {
        public const string DefaultPattern = "{SheetNumber} - {SheetName}";

        public static readonly IReadOnlyList<NamingToken> All = new[]
        {
            new NamingToken("SheetNumber", "Número de lámina"),
            new NamingToken("SheetName", "Nombre de la lámina"),
            new NamingToken("RevisionNumber", "Número de la revisión actual"),
            new NamingToken("RevisionDescription", "Descripción de la revisión actual"),
            new NamingToken("RevisionDate", "Fecha de la revisión actual"),
            new NamingToken("SheetIssueDate", "Fecha de emisión de la lámina"),
            new NamingToken("ProjectTitle", "Nombre del archivo de Revit"),
            new NamingToken("ProjectNumber", "Número de proyecto"),
            new NamingToken("ProjectName", "Nombre del proyecto"),
            new NamingToken("ClientName", "Nombre del cliente"),
            new NamingToken("Date", "Fecha de hoy (AAAA-MM-DD)")
        };

        /// <summary>One-line hint listing every token, for the tooltip under the pattern box.</summary>
        public static string HelpText =>
            string.Join("  ", All.Select(token => token.Placeholder).ToArray());
    }
}
