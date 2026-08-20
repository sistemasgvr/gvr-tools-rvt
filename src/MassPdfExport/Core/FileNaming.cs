using System.IO;
using System.Linq;
using System.Text;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Builds sanitized, collision-free file names from a sheet using a token pattern,
    /// e.g. "{SheetNumber} - {SheetName}". Format-agnostic: callers add their own extension.
    /// </summary>
    public static class FileNaming
    {
        public const string DefaultPattern = "{SheetNumber} - {SheetName}";

        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        public static string BuildBaseName(string pattern, SheetExportInfo sheet)
        {
            string pat = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern;

            string result = pat
                .Replace("{SheetNumber}", sheet.SheetNumber)
                .Replace("{SheetName}", sheet.SheetName)
                .Replace("{RevisionNumber}", sheet.RevisionNumber ?? string.Empty)
                .Replace("{RevisionDescription}", sheet.RevisionDescription ?? string.Empty);

            result = Sanitize(result);

            return string.IsNullOrWhiteSpace(result) ? Sanitize(sheet.SheetNumber) : result;
        }

        public static string BuildFileName(string pattern, SheetExportInfo sheet, string extension) =>
            BuildBaseName(pattern, sheet) + extension;

        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(InvalidChars.Contains(c) ? '_' : c);

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Returns a path inside <paramref name="folder"/> for <paramref name="fileName"/>,
        /// appending " (2)", " (3)", ... if a file with that name already exists.
        /// </summary>
        public static string GetUniquePath(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);
            if (!File.Exists(candidate)) return candidate;

            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);

            int i = 2;
            string next;
            do
            {
                next = Path.Combine(folder, $"{nameOnly} ({i}){ext}");
                i++;
            } while (File.Exists(next));

            return next;
        }

        /// <summary>
        /// Like <see cref="GetUniquePath"/>, but for APIs (like Document.Export) that take a base
        /// name without extension and append it themselves. Returns just the base name to use.
        /// </summary>
        public static string GetUniqueBaseName(string folder, string baseName, string extension)
        {
            if (!File.Exists(Path.Combine(folder, baseName + extension))) return baseName;

            int i = 2;
            string next;
            do
            {
                next = $"{baseName} ({i})";
                i++;
            } while (File.Exists(Path.Combine(folder, next + extension)));

            return next;
        }
    }
}
