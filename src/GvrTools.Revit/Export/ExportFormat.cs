using System;
using System.Collections.Generic;

namespace GvrTools.Revit.Export
{
    /// <summary>Output formats the batch exporter can produce.</summary>
    public enum ExportFormat
    {
        Pdf,
        Dwg
    }

    /// <summary>Display name and file extension for each <see cref="ExportFormat"/>.</summary>
    public static class ExportFormatInfo
    {
        private static readonly Dictionary<ExportFormat, string> Extensions = new Dictionary<ExportFormat, string>
        {
            [ExportFormat.Pdf] = ".pdf",
            [ExportFormat.Dwg] = ".dwg"
        };

        private static readonly Dictionary<ExportFormat, string> Labels = new Dictionary<ExportFormat, string>
        {
            [ExportFormat.Pdf] = "PDF",
            [ExportFormat.Dwg] = "DWG"
        };

        public static string Extension(ExportFormat format) =>
            Extensions.TryGetValue(format, out string extension) ? extension : ".out";

        public static string Label(ExportFormat format) =>
            Labels.TryGetValue(format, out string label) ? label : format.ToString().ToUpperInvariant();

        public static IEnumerable<ExportFormat> All => (ExportFormat[])Enum.GetValues(typeof(ExportFormat));
    }
}
