#if !REVIT2022_OR_GREATER
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using Microsoft.Win32;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>How a PDF printer can be made to write a file without asking the user.</summary>
    public enum PdfPrinterKind
    {
        /// <summary>Honours the output path Revit hands it (<see cref="Autodesk.Revit.DB.PrintManager.PrintToFileName"/>).</summary>
        WritesToGivenPath,

        /// <summary>
        /// Adobe PDF / Acrobat Distiller. Ignores Revit's output path and prompts, unless the target
        /// file is handed over beforehand through Distiller's own registry channel.
        /// </summary>
        AdobeDistiller,

        /// <summary>
        /// PDF24. Its default profile ignores Revit's output path and opens the interactive "PDF24
        /// Assistant" instead, unless its own registry-based "autoSave" handler is switched on first
        /// (see <see cref="Pdf24AutoSaveOutput"/>) - confirmed by inspecting a real PDF24 install,
        /// where DiRoots ProSheets' own PDF24-based printer already uses exactly that handler.
        /// </summary>
        Pdf24,

        /// <summary>Always opens a Save dialog and blocks on it. Unusable for a batch.</summary>
        AlwaysPrompts,

        /// <summary>Not recognised. Probably writes where it is told, but it may prompt.</summary>
        Unknown
    }

    /// <summary>A Windows printer, classified by how it can be driven unattended.</summary>
    public sealed class PdfPrinter
    {
        /// <summary>
        /// Ports that make the spooler ask the user where to save. "Microsoft Print to PDF" and the
        /// XPS writer both sit on PORTPROMPT:, which is why they cannot be used for an unattended
        /// batch: every sheet stops and waits for a native Save dialog.
        /// </summary>
        private static readonly HashSet<string> PromptingPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PORTPROMPT:",
            "FILE:"
        };

        /// <summary>
        /// Printers known to write to the path they are given. Matched on the printer or driver name.
        /// A deliberate allow-list: guessing that an unknown driver behaves is what makes a batch
        /// stop on a dialog half way through.
        /// </summary>
        private static readonly string[] KnownSilentDrivers =
        {
            "Bullzip",
            "CutePDF",
            "PDFCreator",
            "doPDF",
            "novaPDF",
            "PDF-XChange",
            "Foxit"
        };

        public PdfPrinter(string name, string port, string driver)
        {
            Name = name;
            Port = port ?? string.Empty;
            Driver = driver ?? string.Empty;
            Kind = Classify(Name, Port, Driver);
        }

        public string Name { get; }

        /// <summary>Spooler port, e.g. <c>PORTPROMPT:</c>, <c>Documents\*.pdf</c>, <c>nul:</c>.</summary>
        public string Port { get; }

        /// <summary>Driver name, e.g. <c>Adobe PDF Converter</c>.</summary>
        public string Driver { get; }

        public PdfPrinterKind Kind { get; }

        public bool LooksLikePdf =>
            Name.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Driver.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Can be used for a batch that must not interrupt the user.</summary>
        public bool CanExportUnattended => LooksLikePdf && Kind != PdfPrinterKind.AlwaysPrompts;

        public override string ToString() => Name;

        private static PdfPrinterKind Classify(string name, string port, string driver)
        {
            if (PromptingPorts.Contains(port.Trim())) return PdfPrinterKind.AlwaysPrompts;

            // Adobe's printer is bound to a pseudo-port ("Documents\*.pdf") and asks for the file
            // name itself, so it needs its own hand-off mechanism rather than Revit's.
            if (Contains(driver, "Adobe PDF") || Contains(name, "Adobe PDF") || Contains(port, "Documents\\*.pdf"))
                return PdfPrinterKind.AdobeDistiller;

            // Driver, not name: this also catches third-party printers built on top of the PDF24
            // driver (e.g. DiRoots ProSheets' "diroots.prosheets"), which need the same registry
            // hand-off - they are not plain WritesToGivenPath printers either.
            if (Contains(driver, "PDF24")) return PdfPrinterKind.Pdf24;

            foreach (string known in KnownSilentDrivers)
            {
                if (Contains(name, known) || Contains(driver, known)) return PdfPrinterKind.WritesToGivenPath;
            }

            return PdfPrinterKind.Unknown;
        }

        private static bool Contains(string haystack, string needle) =>
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Lists the installed printers and works out how each one can be made to write a PDF without
    /// interrupting the user.
    ///
    /// Only the Revit 2021 build needs this: from Revit 2022 on, PDFs come from the Revit API itself
    /// and no printer is involved.
    ///
    /// Names come from <see cref="PrinterSettings.InstalledPrinters"/> (the same driver list Revit
    /// queries) and the port and driver come from the spooler's registry key. The registry is read
    /// rather than queried through WMI because it needs no extra assembly, no elevation, and returns
    /// instantly instead of taking about a second per call.
    /// </summary>
    public static class PdfPrinterCatalog
    {
        private const string PrintersKey = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

        /// <summary>PDF printers known to write straight to a given path, to suggest to the user.</summary>
        public static readonly string[] RecommendedPrinters =
        {
            "PDF24 Creator",
            "Bullzip PDF Printer",
            "CutePDF Writer",
            "PDFCreator",
            "doPDF"
        };

        public static IReadOnlyList<PdfPrinter> GetAll()
        {
            var printers = new List<PdfPrinter>();

            foreach (string name in InstalledPrinterNames())
            {
                (string port, string driver) = ReadPortAndDriver(name);
                printers.Add(new PdfPrinter(name, port, driver));
            }

            return printers;
        }

        public static PdfPrinter Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            return GetAll().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds an installed, silently-writable printer whose name contains <paramref name="hint"/>
        /// (e.g. "PDF24") - for a tool that must pin itself to one specific product rather than
        /// accept any driver from the allow-list.
        ///
        /// Matched by name only, deliberately not by driver: PDF24's own driver is also reused by
        /// third-party tools that install their own printer on top of it (observed in the wild -
        /// DiRoots ProSheets registers a printer named "diroots.prosheets" with driver "PDF24", but
        /// routes the job through its own named pipe instead of writing to the given path). Matching
        /// the driver would pick whichever of the two sorts first alphabetically.
        /// </summary>
        public static PdfPrinter FindByNameContains(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint)) return null;

            return GetAll().FirstOrDefault(p =>
                (p.Kind == PdfPrinterKind.WritesToGivenPath || p.Kind == PdfPrinterKind.Pdf24) &&
                p.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Best candidate for an unattended run, or null when the machine only has printers that
        /// would pop a Save dialog. Preference order matches how reliable each mechanism is.
        /// </summary>
        public static PdfPrinter FindBestUnattended()
        {
            IReadOnlyList<PdfPrinter> printers = GetAll();

            foreach (string recommended in RecommendedPrinters)
            {
                PdfPrinter match = printers.FirstOrDefault(p =>
                    (p.Kind == PdfPrinterKind.WritesToGivenPath || p.Kind == PdfPrinterKind.Pdf24) &&
                    p.Name.IndexOf(recommended, StringComparison.OrdinalIgnoreCase) >= 0);

                if (match != null) return match;
            }

            return printers.FirstOrDefault(p => p.CanExportUnattended && p.Kind == PdfPrinterKind.WritesToGivenPath)
                ?? printers.FirstOrDefault(p => p.CanExportUnattended && p.Kind == PdfPrinterKind.Pdf24)
                ?? printers.FirstOrDefault(p => p.CanExportUnattended && p.Kind == PdfPrinterKind.AdobeDistiller)
                ?? printers.FirstOrDefault(p => p.CanExportUnattended);
        }

        private static IEnumerable<string> InstalledPrinterNames()
        {
            var names = new List<string>();

            try
            {
                names.AddRange(PrinterSettings.InstalledPrinters.Cast<string>());
            }
            catch (Exception)
            {
                // No print spooler running: the caller reports it as a setup problem.
                return names;
            }

            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        private static (string Port, string Driver) ReadPortAndDriver(string printerName)
        {
            try
            {
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey printers = machine.OpenSubKey(PrintersKey))
                using (RegistryKey printer = printers?.OpenSubKey(printerName))
                {
                    if (printer == null) return (string.Empty, string.Empty);

                    return (
                        printer.GetValue("Port") as string ?? string.Empty,
                        printer.GetValue("Printer Driver") as string ?? string.Empty);
                }
            }
            catch (Exception)
            {
                // Network printers and locked-down machines can hide the key. An unknown port means
                // the printer is classified as Unknown, which is warned about rather than trusted.
                return (string.Empty, string.Empty);
            }
        }
    }
}
#endif
