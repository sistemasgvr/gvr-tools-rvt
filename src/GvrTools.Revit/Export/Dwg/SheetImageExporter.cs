using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GvrTools.Core.Diagnostics;

namespace GvrTools.Revit.Export.Dwg
{
    /// <summary>
    /// Renders one sheet as a PNG using Revit's <see cref="ImageExportOptions"/>.
    ///
    /// This is a companion of the DWG exporter, not a first-class format on its own: it exists so
    /// the "Exportar imágenes" checkbox can drop a preview alongside each .dwg without turning the
    /// engine matrix into a two-dimensional table.
    ///
    /// Revit's image API always saves at least the sheet passed in, and picks the actual file name
    /// itself from the view name and a "Sheet - " prefix. Two behaviours matter here:
    ///
    ///  - The caller only gets to influence the file name via <see cref="ImageExportOptions.FilePath"/>
    ///    (a directory + prefix), not the exact name. So the produced file is discovered by looking
    ///    for the newest .png in the destination folder after the export call.
    ///  - Revit refuses to overwrite an existing image, so a pre-existing file with the same name
    ///    is renamed out of the way first and put back if the export fails.
    /// </summary>
    public sealed class SheetImageExporter
    {
        private readonly ILog _log;

        public SheetImageExporter(ILog log)
        {
            _log = log ?? NullLog.Instance;
        }

        /// <summary>
        /// Exports <paramref name="sheet"/> as a PNG into the same folder as <paramref name="dwgPath"/>,
        /// with the same base name. Returns null on failure — the caller logs it as a warning but
        /// does not fail the sheet: the .dwg is already in place.
        /// </summary>
        public string ExportAlongside(Document document, ViewSheet sheet, string dwgPath)
        {
            string folder = Path.GetDirectoryName(dwgPath);
            string baseName = Path.GetFileNameWithoutExtension(dwgPath);
            string finalPath = Path.Combine(folder, baseName + ".png");

            HashSet<string> existingPngs = SnapshotPngs(folder);

            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 2048,
                ImageResolution = ImageResolution.DPI_300,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ShouldCreateWebSite = false,
                FilePath = Path.Combine(folder, baseName)
            };

            options.SetViewsAndSheets(new List<ElementId> { sheet.Id });

            try
            {
                document.ExportImage(options);
            }
            catch (Exception ex)
            {
                _log.Warn($"No se pudo exportar la imagen de {sheet.SheetNumber}: {ex.Message}");
                return null;
            }

            string produced = FindNewPng(folder, existingPngs);
            if (produced == null)
            {
                _log.Warn($"Revit no generó ningún PNG para {sheet.SheetNumber}.");
                return null;
            }

            // The name Revit chose almost never matches; rename to match the .dwg for a stable pair.
            if (!string.Equals(produced, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(produced, finalPath);
                }
                catch (Exception ex)
                {
                    _log.Warn($"No se pudo renombrar la imagen exportada '{produced}': {ex.Message}");
                    return produced;
                }
            }

            return finalPath;
        }

        private static HashSet<string> SnapshotPngs(string folder)
        {
            try
            {
                return new HashSet<string>(
                    Directory.GetFiles(folder, "*.png"),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string FindNewPng(string folder, HashSet<string> before)
        {
            try
            {
                return Directory.GetFiles(folder, "*.png")
                    .FirstOrDefault(path => !before.Contains(path));
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
