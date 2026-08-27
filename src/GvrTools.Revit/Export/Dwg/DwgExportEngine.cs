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
            private readonly ILog _log;
            private bool _imageSkipWarned;

            internal Session(ExportRequest request, DwgExportSettings settings)
            {
                _document = request.UIDocument.Document;
                _folder = request.DestinationFolder;
                _namer = new ExportFileNamer(
                    request.DestinationFolder,
                    request.NamingPattern,
                    ExportFormatInfo.Extension(ExportFormat.Dwg),
                    request.Project.ToTokens());

                _options = BuildOptions(_document, settings);
                _exportImage = settings.AlsoExportImage;
                _log = request.Log;
                _imageExporter = _exportImage ? new SheetImageExporter(request.Log) : null;
            }

            public BatchItemResult Export(SheetSnapshot sheet)
            {
                View view = SheetRepository.ResolveView(_document, sheet);
                if (view == null)
                {
                    string missing = sheet.Kind == ExportItemKind.View
                        ? "La vista ya no existe en el proyecto."
                        : "La lámina ya no existe en el proyecto.";
                    return BatchItemResult.Failure(sheet.Label, missing);
                }

                // The per-view-sibling-file naming and the legacy-DWG cleanup below only make sense
                // for a sheet, which can have several views placed on it; a standalone view is
                // exported as a single file and never produces those siblings.
                ViewSheet viewSheet = view as ViewSheet;

                IReadOnlyList<string> viewSuffixes = _options.MergedViews || viewSheet == null
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

                if (viewSheet != null)
                    MoveAsideLegacyRevitDwgs(viewSheet);

                bool exported = _document.Export(_folder, baseName, new List<ElementId> { sheet.Id }, _options);

                if (!exported)
                {
                    string rejected = sheet.Kind == ExportItemKind.View
                        ? "Revit rechazó la exportación DWG de esta vista."
                        : "Revit rechazó la exportación DWG de esta lámina.";
                    return BatchItemResult.Failure(sheet.Label, rejected + (detailSuffix ?? string.Empty));
                }

                if (!File.Exists(expectedPath))
                    return BatchItemResult.Failure(sheet.Label, "Revit no generó el archivo DWG esperado.");

                // Image export is a companion, not a gate: if it fails, the DWG is still a success.
                // Only wired for sheets today (SheetImageExporter reads sheet-specific geometry) --
                // for a standalone view the checkbox is silently a no-op, so at least one warning
                // goes to the log instead of the image just never appearing with no explanation.
                if (_exportImage && viewSheet != null)
                {
                    _imageExporter.ExportAlongside(_document, viewSheet, expectedPath);
                }
                else if (_exportImage && !_imageSkipWarned)
                {
                    _imageSkipWarned = true;
                    _log.Warn("\"Exportar también imagen\" no aplica en modo Vistas; se omite para todas las vistas de este lote.");
                }

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

            /// <summary>
            /// Si el usuario eligió una configuración DWG ya guardada en el proyecto, se usa tal
            /// cual (trae su propio mapeo de capas/grosores que este panel no expone) -- solo se le
            /// respeta encima el candado de "ocultar gráficos auxiliares", que es nuestro y no algo
            /// que el diálogo nativo de configuración DWG de Revit cubra. Si el nombre guardado ya
            /// no existe (se borró en Revit desde la última vez), cae de vuelta a nuestros controles.
            /// </summary>
            private static DWGExportOptions BuildOptions(Document document, DwgExportSettings settings)
            {
                DWGExportOptions saved = DwgExportSetupCatalog.TryGetOptions(document, settings.SavedSetupName);
                if (saved != null)
                {
                    saved.HideUnreferenceViewTags = settings.HideHelperGraphics;
                    saved.HideReferencePlane = settings.HideHelperGraphics;
                    saved.HideScopeBox = settings.HideHelperGraphics;
                    return saved;
                }

                return new DWGExportOptions
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
            }

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
