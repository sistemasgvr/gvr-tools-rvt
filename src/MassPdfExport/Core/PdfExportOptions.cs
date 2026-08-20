namespace GvrTools.MassPdfExport.Core
{
    /// <summary>User-configurable print/plot options, applied to every sheet in a run.</summary>
    public sealed class PdfExportOptions
    {
        /// <summary>Windows printer name to plot through. Null/empty = auto-detect a PDF-capable printer.</summary>
        public string PrinterName { get; set; }

        /// <summary>true = no margin (content centered edge-to-edge); false = printer's default margin.</summary>
        public bool NoMargin { get; set; } = true;

        /// <summary>true = scale content to fill the page; false = print at true 100% scale.</summary>
        public bool FitToPage { get; set; } = true;

        /// <summary>true = try to match each sheet's own size to a paper size the printer supports.</summary>
        public bool MatchSheetSize { get; set; } = true;
    }
}
