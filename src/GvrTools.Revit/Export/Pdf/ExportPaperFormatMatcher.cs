#if REVIT2022_OR_GREATER
using System;
using Autodesk.Revit.DB;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Maps a measured sheet size to a named <see cref="ExportPaperFormat"/>.
    ///
    /// Required whenever zoom or non-center placement must take effect: Autodesk documents that
    /// <c>PDFExportOptions.ZoomType</c> (and non-Center placement) are ignored while
    /// <c>PaperFormat == Default</c> ("use sheet size").
    /// </summary>
    internal static class ExportPaperFormatMatcher
    {
        private const double ToleranceInches = 1.0;

        // Width x height in inches (short side first is fine; score tries both orientations).
        private static readonly (ExportPaperFormat Format, double W, double H)[] Catalog =
        {
            (ExportPaperFormat.ISO_A4, 8.27, 11.69),
            (ExportPaperFormat.ISO_A3, 11.69, 16.54),
            (ExportPaperFormat.ISO_A2, 16.54, 23.39),
            (ExportPaperFormat.ISO_A1, 23.39, 33.11),
            (ExportPaperFormat.ISO_A0, 33.11, 46.81),
            (ExportPaperFormat.ISO_B4, 9.84, 13.90),
            (ExportPaperFormat.ISO_B3, 13.90, 19.69),
            (ExportPaperFormat.ISO_B2, 19.69, 27.83),
            (ExportPaperFormat.ISO_B1, 27.83, 39.37),
            (ExportPaperFormat.ANSI_A, 8.5, 11.0),
            (ExportPaperFormat.ANSI_B, 11.0, 17.0),
            (ExportPaperFormat.ANSI_C, 17.0, 22.0),
            (ExportPaperFormat.ANSI_D, 22.0, 34.0),
            (ExportPaperFormat.ANSI_E, 34.0, 44.0),
            (ExportPaperFormat.ARCH_A, 9.0, 12.0),
            (ExportPaperFormat.ARCH_B, 12.0, 18.0),
            (ExportPaperFormat.ARCH_C, 18.0, 24.0),
            (ExportPaperFormat.ARCH_D, 24.0, 36.0),
            (ExportPaperFormat.ARCH_E, 36.0, 48.0),
            (ExportPaperFormat.ARCH_E1, 30.0, 42.0),
            (ExportPaperFormat.ARCH_E2, 26.0, 38.0),
            (ExportPaperFormat.ARCH_E3, 27.0, 39.0),
        };

        /// <summary>
        /// Best named format for <paramref name="sheet"/>, or null if unknown / no close match.
        /// </summary>
        public static ExportPaperFormat? FindBestMatch(SheetSize sheet)
        {
            if (!sheet.IsKnown) return null;

            ExportPaperFormat? best = null;
            double bestScore = double.MaxValue;

            foreach (var entry in Catalog)
            {
                double score = Math.Min(
                    Math.Abs(entry.W - sheet.WidthInches) + Math.Abs(entry.H - sheet.HeightInches),
                    Math.Abs(entry.W - sheet.HeightInches) + Math.Abs(entry.H - sheet.WidthInches));

                if (score >= bestScore) continue;
                bestScore = score;
                best = entry.Format;
            }

            return bestScore > ToleranceInches ? null : best;
        }

        /// <summary>
        /// Format to use when zoom / corner placement / fixed paper require a named size.
        /// Falls back to ANSI_D (common construction sheet) if the sheet cannot be matched.
        /// </summary>
        public static ExportPaperFormat ResolveRequired(SheetSize sheet) =>
            FindBestMatch(sheet) ?? ExportPaperFormat.ANSI_D;
    }
}
#endif
