using System;
using System.Linq;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Revit 2021 has no built-in PDF export API (that was added in Revit 2022), so plotting to
    /// PDF goes through Document.PrintManager and an installed PDF-capable printer driver. This
    /// finds one without hardcoding an English name, since the tool must work on any Revit/Windows
    /// language.
    /// </summary>
    public static class PdfPrinterLocator
    {
        public static string[] GetInstalledPrinters()
        {
            return System.Drawing.Printing.PrinterSettings.InstalledPrinters
                .Cast<string>()
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public static string FindPdfPrinterName()
        {
            string[] installed = GetInstalledPrinters();

            string builtIn = installed.FirstOrDefault(n =>
                string.Equals(n, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
            if (builtIn != null) return builtIn;

            return installed.FirstOrDefault(n => n.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
