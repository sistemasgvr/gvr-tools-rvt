using System.Collections.Generic;
using GvrTools.Core.Naming;
using GvrTools.Revit.Model;

namespace GvrTools.Revit.Export
{
    /// <summary>
    /// Turns a sheet plus a user pattern into a unique file name inside the destination folder.
    ///
    /// Owned by the export session rather than by each engine, so every format names its files the
    /// same way and collision handling only exists once.
    /// </summary>
    public sealed class ExportFileNamer
    {
        private readonly UniqueNameResolver _resolver;
        private readonly IReadOnlyDictionary<string, string> _projectTokens;
        private readonly string _pattern;
        private readonly string _extension;

        public ExportFileNamer(
            string destinationFolder,
            string pattern,
            string extension,
            IReadOnlyDictionary<string, string> projectTokens)
        {
            _resolver = new UniqueNameResolver(destinationFolder);
            _pattern = string.IsNullOrWhiteSpace(pattern) ? NamingTokens.DefaultPattern : pattern;
            _extension = extension;
            _projectTokens = projectTokens ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Reserves and returns the base name (no extension) for <paramref name="sheet"/>. For use
        /// with export APIs that append the extension themselves.
        /// </summary>
        public string ReserveBaseName(SheetSnapshot sheet) =>
            _resolver.ReserveBaseName(BuildName(sheet), _extension);

        /// <summary>Reserves and returns the full output path for <paramref name="sheet"/>.</summary>
        public string ReservePath(SheetSnapshot sheet) =>
            _resolver.ReservePath(BuildName(sheet), _extension);

        /// <summary>Expands the pattern without reserving anything, for the preview in the UI.</summary>
        public string Preview(SheetSnapshot sheet) => BuildName(sheet) + _extension;

        private string BuildName(SheetSnapshot sheet)
        {
            var tokens = new Dictionary<string, string>(_projectTokens.Count + 8);

            foreach (KeyValuePair<string, string> token in _projectTokens)
                tokens[token.Key] = token.Value;

            foreach (KeyValuePair<string, string> token in sheet.ToTokens())
                tokens[token.Key] = token.Value;

            return FileNameBuilder.Build(_pattern, tokens, sheet.Number);
        }
    }
}
