using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.Naming;
using GvrTools.Revit.Model;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Dwg
{
    /// <summary>
    /// Exports sheets to DWG with <c>Document.Export</c> and <see cref="DWGExportOptions"/>.
    ///
    /// Unlike PDF, DWG has had a real, silent export API in every Revit release this add-in
    /// supports, so this engine is identical on 2021 and on 2025: no printer, no dialogs, no
    /// version guards.
    /// </summary>
    public sealed class DwgExportEngine : IExportEngine
    {
        public ExportFormat Format => ExportFormat.Dwg;

        public string StrategyDescription => "API nativa de DWG de Revit: silenciosa en todas las versiones.";

        public IExportSession BeginSession(ExportRequest request) =>
            new Session(request, request.SettingsAs<DwgExportSettings>());

        private sealed class Session : IExportSession
        {
            private readonly Document _document;
            private readonly ExportFileNamer _namer;
            private readonly DWGExportOptions _options;
            private readonly string _folder;
            private readonly bool _exportImage;
            private readonly SheetImageExporter _imageExporter;

            internal Session(ExportRequest request, DwgExportSettings settings)
            {
                _document = request.UIDocument.Document;
                _folder = request.DestinationFolder;
                _namer = new ExportFileNamer(
                    request.DestinationFolder,
                    request.NamingPattern,
                    ExportFormatInfo.Extension(ExportFormat.Dwg),
                    request.Project.ToTokens());

                _options = BuildOptions(settings);
                _exportImage = settings.AlsoExportImage;
                _imageExporter = _exportImage ? new SheetImageExporter(request.Log) : null;
            }

            public BatchItemResult Export(SheetSnapshot sheet)
            {
                ViewSheet viewSheet = SheetRepository.Resolve(_document, sheet);
                if (viewSheet == null)
                    return BatchItemResult.Failure(sheet.Label, "La lámina ya no existe en el proyecto.");

                IReadOnlyList<string> viewSuffixes = _options.MergedViews
                    ? null
                    : CollectViewSuffixes(viewSheet);

                // Document.Export appends the extension itself, so it needs a base name.
                string baseName = viewSuffixes == null
                    ? _namer.ReserveBaseName(sheet)
                    : _namer.ReserveDwgBaseName(sheet, viewSuffixes);

                string expectedPath = Path.Combine(_folder, baseName + ".dwg");
                string builtName = _namer.UnreservedBaseName(sheet);
                string detailSuffix = string.Equals(baseName, builtName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $" (como {baseName}.dwg porque ya existía uno anterior)";

                MoveAsideLegacyRevitDwgs(viewSheet);

                bool exported = _document.Export(_folder, baseName, new List<ElementId> { sheet.Id }, _options);

                if (!exported)
                    return BatchItemResult.Failure(
                        sheet.Label,
                        "Revit rechazó la exportación DWG de esta lámina." + (detailSuffix ?? string.Empty));

                if (!File.Exists(expectedPath))
                    return BatchItemResult.Failure(sheet.Label, "Revit no generó el archivo DWG esperado.");

                // Image export is a companion, not a gate: if it fails, the DWG is still a success.
                if (_exportImage)
                    _imageExporter.ExportAlongside(_document, viewSheet, expectedPath);

                return BatchItemResult.Success(
                    sheet.Label,
                    expectedPath + (detailSuffix ?? string.Empty));
            }

            private IReadOnlyList<string> CollectViewSuffixes(ViewSheet viewSheet)
            {
                var suffixes = new List<string>();
                foreach (ElementId viewId in viewSheet.GetAllPlacedViews())
                {
                    if (!(_document.GetElement(viewId) is View view))
                        continue;

                    string suffix = PathSanitizer.SanitizeFileName(view.Name);
                    if (!string.IsNullOrWhiteSpace(suffix))
                        suffixes.Add(suffix);
                }

                return suffixes;
            }

            /// <summary>
            /// Revit a veces deja DWG viejos con nombre {proyecto}-{vista}.dwg que bloquean re-exportes.
            /// </summary>
            private void MoveAsideLegacyRevitDwgs(ViewSheet viewSheet)
            {
                string docPrefix = PathSanitizer.SanitizeFileName(_document.Title);
                if (string.IsNullOrWhiteSpace(docPrefix))
                    return;

                var asideResolver = new UniqueNameResolver(_folder);
                foreach (ElementId viewId in viewSheet.GetAllPlacedViews())
                {
                    if (!(_document.GetElement(viewId) is View view))
                        continue;

                    string suffix = PathSanitizer.SanitizeFileName(view.Name);
                    if (string.IsNullOrWhiteSpace(suffix))
                        continue;

                    string legacyPath = Path.Combine(_folder, docPrefix + "-" + suffix + ".dwg");
                    if (!File.Exists(legacyPath))
                        continue;

                    try
                    {
                        string asidePath = asideResolver.ReservePath(docPrefix + "-" + suffix + "_anterior", ".dwg");
                        File.Move(legacyPath, asidePath);
                    }
                    catch (Exception)
                    {
                        // Si no se puede mover, Revit puede seguir mostrando su diálogo nativo.
                    }
                }
            }

            public void Dispose()
            {
                // Nothing to undo: this engine changes no document or application state.
            }

            private static DWGExportOptions BuildOptions(DwgExportSettings settings) => new DWGExportOptions
            {
                FileVersion = ToAcadVersion(settings.FileVersion),
                Colors = ExportColorMode.TrueColorPerView,
                TargetUnit = ExportUnit.Default,
                PreserveCoincidentLines = true,
                MergedViews = settings.MergeViews,
                SharedCoords = settings.UseSharedCoordinates,
                HideUnreferenceViewTags = settings.HideHelperGraphics,
                HideReferencePlane = settings.HideHelperGraphics,
                HideScopeBox = settings.HideHelperGraphics
            };

            private static ACADVersion ToAcadVersion(DwgFileVersion version)
            {
                switch (version)
                {
                    case DwgFileVersion.R2007: return ACADVersion.R2007;
                    case DwgFileVersion.R2010: return ACADVersion.R2010;
                    case DwgFileVersion.R2013: return ACADVersion.R2013;
                    case DwgFileVersion.R2018: return ACADVersion.R2018;
                    default: return ACADVersion.Default;
                }
            }
        }
    }
}
