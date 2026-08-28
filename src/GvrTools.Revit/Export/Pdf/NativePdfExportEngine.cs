#if REVIT2022_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Revit.Model;
using GvrTools.Revit.Sheets;

namespace GvrTools.Revit.Export.Pdf
{
    /// <summary>
    /// Exports PDFs with Revit's own PDF exporter (<c>Document.Export</c> +
    /// <see cref="PDFExportOptions"/>, available from Revit 2022).
    ///
    /// This is the path that makes a batch export unattended in the real sense of the word: Revit
    /// writes the files itself, so there is no Windows printer involved, no "Save print output as"
    /// dialog, no window taking focus and no keyboard automation. The machine stays usable while a
    /// hundred sheets are being written, which is exactly what tools like ProSheets do on these
    /// releases.
    ///
    /// One call per sheet rather than one call for the whole set: it costs nothing measurable and it
    /// buys exact control over each file name, per-sheet progress, per-sheet error reporting and a
    /// cancel that takes effect immediately.
    /// </summary>
    public sealed class NativePdfExportEngine : IExportEngine
    {
        public ExportFormat Format => ExportFormat.Pdf;

        public string StrategyDescription =>
            "API nativa de PDF de Revit: sin impresora, sin ventanas y sin bloquear el equipo.";

        public IExportSession BeginSession(ExportRequest request)
        {
            PdfExportSettings settings = request.SettingsAs<PdfExportSettings>();

            return new Session(request, settings);
        }

        private sealed class Session : IExportSession
        {
            private readonly Document _document;
            private readonly ExportFileNamer _namer;
            private readonly PdfExportSettings _settings;
            private readonly string _folder;
            private readonly ILog _log;

            internal Session(ExportRequest request, PdfExportSettings settings)
            {
                _document = request.UIDocument.Document;
                _settings = settings;
                _folder = request.DestinationFolder;
                _log = request.Log;
                _namer = new ExportFileNamer(
                    request.DestinationFolder,
                    request.NamingPattern,
                    ExportFormatInfo.Extension(ExportFormat.Pdf),
                    request.Project.ToTokens());
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

                string baseName = _namer.ReserveBaseName(sheet);
                string expectedPath = Path.Combine(_folder, baseName + ".pdf");

                using (PDFExportOptions options = PdfExportOptionsFactory.Build(_settings, baseName, ResolveOrientation(view)))
                {
                    bool exported = _document.Export(_folder, new List<ElementId> { sheet.Id }, options);

                    if (!exported)
                    {
                        string rejected = sheet.Kind == ExportItemKind.View
                            ? "Revit rechazó la exportación de esta vista."
                            : "Revit rechazó la exportación de esta lámina.";
                        return BatchItemResult.Failure(sheet.Label, rejected);
                    }
                }

                if (!File.Exists(expectedPath))
                {
                    _log.Warn($"Revit informó éxito pero no se encontró '{expectedPath}'.");
                    return BatchItemResult.Failure(sheet.Label, "Revit no generó el archivo PDF esperado.");
                }

                return BatchItemResult.Success(sheet.Label, expectedPath);
            }

            public void Dispose()
            {
                // Nothing to undo: this engine changes no document or application state.
            }

            /// <summary>
            /// Auto is right for sheets whose size Revit can work out on its own; measuring the
            /// title block only pays off when the user asked us to respect each sheet's own size. A
            /// standalone view has no title block to measure, so it always falls back to Auto.
            /// </summary>
            private PageOrientationType ResolveOrientation(View view)
            {
                if (!_settings.MatchSheetSize || !(view is ViewSheet sheet)) return PageOrientationType.Auto;

                SheetSize size = SheetSizeReader.Read(_document, sheet);
                if (!size.IsKnown) return PageOrientationType.Auto;

                return size.IsLandscape ? PageOrientationType.Landscape : PageOrientationType.Portrait;
            }
        }
    }
}
#endif
