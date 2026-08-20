using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>Plain snapshot of the sheet data the exporter and UI need, taken while the Revit API is safe to touch.</summary>
    public sealed class SheetExportInfo
    {
        public ElementId SheetId { get; }
        public string SheetNumber { get; }
        public string SheetName { get; }
        public string RevisionNumber { get; }
        public string RevisionDescription { get; }

        public SheetExportInfo(ElementId sheetId, string sheetNumber, string sheetName, string revisionNumber, string revisionDescription)
        {
            SheetId = sheetId;
            SheetNumber = sheetNumber;
            SheetName = sheetName;
            RevisionNumber = revisionNumber;
            RevisionDescription = revisionDescription;
        }
    }

    public sealed class ExportProgress
    {
        public int Current { get; }
        public int Total { get; }
        public SheetExportInfo Sheet { get; }

        public ExportProgress(int current, int total, SheetExportInfo sheet)
        {
            Current = current;
            Total = total;
            Sheet = sheet;
        }
    }

    public sealed class SheetExportResult
    {
        public SheetExportInfo Sheet { get; }
        public bool Success { get; }
        public string OutputPath { get; }
        public string ErrorMessage { get; }

        private SheetExportResult(SheetExportInfo sheet, bool success, string outputPath, string errorMessage)
        {
            Sheet = sheet;
            Success = success;
            OutputPath = outputPath;
            ErrorMessage = errorMessage;
        }

        public static SheetExportResult Ok(SheetExportInfo sheet, string outputPath) =>
            new SheetExportResult(sheet, true, outputPath, null);

        public static SheetExportResult Fail(SheetExportInfo sheet, string errorMessage) =>
            new SheetExportResult(sheet, false, null, errorMessage);
    }

    public sealed class ExportSummary
    {
        public IReadOnlyList<SheetExportResult> Results { get; }
        public bool WasCancelled { get; }
        public string DestinationFolder { get; }

        public int SuccessCount => Results.Count(r => r.Success);
        public int FailureCount => Results.Count(r => !r.Success);

        public ExportSummary(IReadOnlyList<SheetExportResult> results, bool wasCancelled, string destinationFolder)
        {
            Results = results;
            WasCancelled = wasCancelled;
            DestinationFolder = destinationFolder;
        }
    }
}
