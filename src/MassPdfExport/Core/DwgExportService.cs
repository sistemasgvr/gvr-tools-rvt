using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Exports sheets to DWG one at a time via Document.Export(DWGExportOptions) - a real, native,
    /// silent Revit API for this format (unlike PDF on Revit 2021), so no printer driver or dialog
    /// automation is involved at all.
    /// </summary>
    public sealed class DwgExportService
    {
        public ExportSummary ExportSheets(
            UIDocument uiDoc,
            IList<(ViewSheet Sheet, SheetExportInfo Info)> sheets,
            string destinationFolder,
            string namingPattern,
            Action<ExportProgress> onProgress,
            Func<bool> isCancellationRequested)
        {
            Document doc = uiDoc.Document;
            Directory.CreateDirectory(destinationFolder);

            var results = new List<SheetExportResult>();
            bool cancelled = false;

            for (int i = 0; i < sheets.Count; i++)
            {
                if (isCancellationRequested())
                {
                    cancelled = true;
                    break;
                }

                var (sheet, info) = sheets[i];
                onProgress?.Invoke(new ExportProgress(i + 1, sheets.Count, info));

                results.Add(ExportOne(doc, sheet, info, destinationFolder, namingPattern));
            }

            return new ExportSummary(results, cancelled, destinationFolder);
        }

        private static SheetExportResult ExportOne(Document doc, ViewSheet sheet, SheetExportInfo info, string destinationFolder, string namingPattern)
        {
            try
            {
                string baseName = FileNaming.BuildBaseName(namingPattern, info);
                string uniqueBaseName = FileNaming.GetUniqueBaseName(destinationFolder, baseName, ".dwg");

                var options = new DWGExportOptions
                {
                    FileVersion = ACADVersion.Default,
                    Colors = ExportColorMode.TrueColor,
                    TargetUnit = ExportUnit.Default,
                    PreserveCoincidentLines = true,
                    HideUnreferenceViewTags = true,
                    HideReferencePlane = true,
                    HideScopeBox = true,
                    MergedViews = true,
                    SharedCoords = false
                };

                var viewIds = new List<ElementId> { sheet.Id };
                bool success = doc.Export(destinationFolder, uniqueBaseName, viewIds, options);

                string destPath = Path.Combine(destinationFolder, uniqueBaseName + ".dwg");

                return success && File.Exists(destPath)
                    ? SheetExportResult.Ok(info, destPath)
                    : SheetExportResult.Fail(info, "Revit no generó el archivo DWG para esta lámina.");
            }
            catch (Exception ex)
            {
                return SheetExportResult.Fail(info, $"Error al exportar la lámina a DWG: {ex.Message}");
            }
        }
    }
}
