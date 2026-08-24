using System.Collections.Generic;
using GvrTools.Revit.Infrastructure;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Version-agnostic facade over PDF printer discovery.
    ///
    /// The printer concept only exists in the Revit 2021 build; from 2022 on, PDFs come straight from
    /// the Revit API. Keeping the <c>#if</c> here means the view models and the window can ask plain
    /// questions ("do I need to offer a printer?", "how will this one behave?") without carrying
    /// conditional compilation into the UI layer.
    /// </summary>
    public static class PdfPrinterOptions
    {
        /// <summary>True when this build needs the user to choose a Windows PDF printer.</summary>
        public static bool IsPrinterRequired => !RevitVersionInfo.HasNativePdfExport;

        /// <summary>
        /// Printers that can write a PDF without interrupting the user. Empty on builds that use the
        /// native exporter.
        /// </summary>
        public static IReadOnlyList<string> ListUsablePrinters()
        {
#if REVIT2022_OR_GREATER
            return new string[0];
#else
            var names = new List<string>();

            foreach (PdfPrinter printer in PdfPrinterCatalog.GetAll())
            {
                if (printer.CanExportUnattended) names.Add(printer.Name);
            }

            return names;
#endif
        }

        /// <summary>Best default printer, or null when none is suitable (or none is needed).</summary>
        public static string SuggestDefault()
        {
#if REVIT2022_OR_GREATER
            return null;
#else
            return PdfPrinterCatalog.FindBestUnattended()?.Name;
#endif
        }

        /// <summary>
        /// The exact installed printer name matching <paramref name="hint"/> (e.g. "PDF24"), or null
        /// if no such printer is installed (or none is needed on this Revit version).
        /// </summary>
        public static string FindByNameContains(string hint)
        {
#if REVIT2022_OR_GREATER
            return null;
#else
            return PdfPrinterCatalog.FindByNameContains(hint)?.Name;
#endif
        }

        /// <summary>
        /// One line telling the user how the chosen printer will produce files. Worth showing: two
        /// printers that both say "PDF" can behave completely differently, and this is where that
        /// becomes visible before a batch is started rather than after.
        /// </summary>
        public static string Describe(string printerName)
        {
#if REVIT2022_OR_GREATER
            return string.Empty;
#else
            if (string.IsNullOrWhiteSpace(printerName)) return string.Empty;

            PdfPrinter printer = PdfPrinterCatalog.Find(printerName);
            if (printer == null) return "Esta impresora ya no está instalada.";

            switch (printer.Kind)
            {
                case PdfPrinterKind.WritesToGivenPath:
                    return "Escribe directamente en la ruta indicada: exportación silenciosa.";

                case PdfPrinterKind.Pdf24:
                    return "Se reconfigura temporalmente en modo silencioso (evita el Asistente de PDF24) " +
                           "antes de cada lámina, y se restaura al cerrar la ventana: exportación silenciosa.";

                case PdfPrinterKind.AdobeDistiller:
                    // "View Adobe PDF results" is a checkbox inside the driver's DEVMODE (binary,
                    // per Acrobat version), not a plain registry value, so it cannot be toggled
                    // reliably by code. The user turns it off once and it stays off.
                    return "Se le indica el destino a Acrobat Distiller antes de cada lámina: " +
                           "exportación silenciosa. Si al terminar cada lámina se abre el PDF en " +
                           "Acrobat: Windows → Impresoras y escáneres → Adobe PDF → " +
                           "Preferencias → desmarca 'Ver los resultados de Adobe PDF' (una sola vez).";

                case PdfPrinterKind.AlwaysPrompts:
                    return "Pregunta el nombre en cada lámina (puerto " + printer.Port + "): " +
                           "no sirve para exportación masiva.";

                default:
                    return "Impresora no reconocida (puerto " + printer.Port + "). Se intentará " +
                           "escribir en la ruta indicada; si abre un cuadro de diálogo, elige otra.";
            }
#endif
        }
    }
}
