using Autodesk.Revit.DB;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Matches a sheet's measured size to one of the printer driver's paper sizes.
    /// Revit's own PaperSize only exposes a Name, no dimensions, so the real widths/heights are
    /// read from System.Drawing.Printing.PrinterSettings (same underlying Windows driver, so the
    /// paper names line up) and then the winning name is looked up back in Revit's PaperSizeSet.
    /// </summary>
    public static class PaperSizeMatcher
    {
        private const double MaxTotalDeviationInches = 1.0;

        public static PaperSize FindBestMatch(PaperSizeSet revitPaperSizes, string printerName, double widthIn, double heightIn)
        {
            if (string.IsNullOrEmpty(printerName) || widthIn <= 0 || heightIn <= 0)
                return null;

            var printerSettings = new System.Drawing.Printing.PrinterSettings { PrinterName = printerName };
            if (!printerSettings.IsValid) return null;

            string bestName = null;
            double bestScore = double.MaxValue;

            foreach (System.Drawing.Printing.PaperSize candidate in printerSettings.PaperSizes)
            {
                double w = candidate.Width / 100.0;
                double h = candidate.Height / 100.0;

                double score = System.Math.Min(
                    System.Math.Abs(w - widthIn) + System.Math.Abs(h - heightIn),
                    System.Math.Abs(w - heightIn) + System.Math.Abs(h - widthIn));

                if (score < bestScore)
                {
                    bestScore = score;
                    bestName = candidate.PaperName;
                }
            }

            if (bestName == null || bestScore > MaxTotalDeviationInches) return null;

            foreach (PaperSize revitSize in revitPaperSizes)
            {
                if (string.Equals(revitSize.Name, bestName, System.StringComparison.OrdinalIgnoreCase))
                    return revitSize;
            }

            return null;
        }
    }
}
