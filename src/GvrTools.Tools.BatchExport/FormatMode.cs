namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// What the user picks in the format combo. <see cref="PdfAndDwg"/> is an orchestration concept
    /// that chains two engine-level exports; the engine layer only knows <see cref="GvrTools.Revit.Export.ExportFormat"/>.
    /// </summary>
    public enum FormatMode
    {
        Pdf,
        Dwg,
        PdfAndDwg
    }
}
