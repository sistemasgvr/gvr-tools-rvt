#if !REVIT2022_OR_GREATER
using System;
using Autodesk.Revit.DB;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Matches a measured sheet size to one of the printer driver's paper sizes.
    ///
    /// Needed only on the printer-driver path: Revit's <see cref="PaperSize"/> exposes a name and
    /// nothing else, so the actual dimensions are read from
    /// <see cref="System.Drawing.Printing.PrinterSettings"/> — the same Windows driver Revit talks
    /// to, so the names line up — and the winning name is then looked back up in Revit's set.
    /// </summary>
    internal static class PaperSizeMatcher
    {
        /// <summary>
        /// Combined width+height slack, in inches, still considered the same paper. Generous enough
        /// to absorb title-block borders, tight enough not to confuse A1 with A0.
        /// </summary>
        private const double ToleranceInches = 1.0;

        public static PaperSize FindBestMatch(PaperSizeSet revitPaperSizes, string printerName, SheetSize sheet)
        {
            if (string.IsNullOrEmpty(printerName) || !sheet.IsKnown) return null;

            string bestName = FindBestDriverPaperName(printerName, sheet);
            if (bestName == null) return null;

            foreach (PaperSize candidate in revitPaperSizes)
            {
                if (string.Equals(candidate.Name, bestName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }

        private static string FindBestDriverPaperName(string printerName, SheetSize sheet)
        {
            var settings = new System.Drawing.Printing.PrinterSettings { PrinterName = printerName };
            if (!settings.IsValid) return null;

            string bestName = null;
            double bestScore = double.MaxValue;

            foreach (System.Drawing.Printing.PaperSize candidate in settings.PaperSizes)
            {
                // Driver dimensions are in hundredths of an inch.
                double width = candidate.Width / 100.0;
                double height = candidate.Height / 100.0;

                // Compared both ways round, because orientation is applied separately.
                double score = Math.Min(
                    Math.Abs(width - sheet.WidthInches) + Math.Abs(height - sheet.HeightInches),
                    Math.Abs(width - sheet.HeightInches) + Math.Abs(height - sheet.WidthInches));

                if (score >= bestScore) continue;

                bestScore = score;
                bestName = candidate.PaperName;
            }

            return bestScore > ToleranceInches ? null : bestName;
        }
    }
}
#endif
