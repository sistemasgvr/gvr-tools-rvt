using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Revit.Model;

namespace GvrTools.Revit.Export
{
    /// <summary>Format-specific options. Each engine declares the concrete type it expects.</summary>
    public interface IExportFormatSettings
    {
        ExportFormat Format { get; }
    }

    /// <summary>Everything one export run needs to know.</summary>
    public sealed class ExportRequest
    {
        public ExportRequest(
            UIDocument uiDocument,
            string destinationFolder,
            string namingPattern,
            IExportFormatSettings settings,
            ProjectSnapshot project,
            ILog log = null)
        {
            UIDocument = uiDocument;
            DestinationFolder = destinationFolder;
            NamingPattern = namingPattern;
            Settings = settings;
            Project = project;
            Log = log ?? NullLog.Instance;
        }

        public UIDocument UIDocument { get; }

        public string DestinationFolder { get; }

        public string NamingPattern { get; }

        public IExportFormatSettings Settings { get; }

        public ProjectSnapshot Project { get; }

        public ILog Log { get; }

        /// <summary>
        /// Casts <see cref="Settings"/> to the type the engine needs, failing with a message aimed
        /// at whoever wired the tool rather than at the user.
        /// </summary>
        public T SettingsAs<T>() where T : class, IExportFormatSettings
        {
            if (Settings is T typed) return typed;

            throw new ExportSetupException(
                $"Las opciones recibidas ({Settings?.GetType().Name ?? "ninguna"}) no corresponden al formato solicitado ({typeof(T).Name}).");
        }
    }

    /// <summary>
    /// Produces files for one format.
    ///
    /// Adding a format means adding one implementation of this interface and registering it in
    /// <see cref="ExportEngineCatalog"/>; no other file changes. The split into engine + session
    /// exists because most formats need per-run setup that must also be undone afterwards (a print
    /// driver selection, a restored active view), and a session with a Dispose is the honest way to
    /// express that.
    /// </summary>
    public interface IExportEngine
    {
        ExportFormat Format { get; }

        /// <summary>
        /// Short description of how this engine writes files, shown in the window so the user knows
        /// whether the run will be silent or will drive a printer driver.
        /// </summary>
        string StrategyDescription { get; }

        /// <summary>
        /// Validates the request and prepares the run.
        /// </summary>
        /// <exception cref="ExportSetupException">Nothing can be exported; message is user-facing.</exception>
        IExportSession BeginSession(ExportRequest request);
    }

    /// <summary>One export run. Not reusable, and always disposed by the caller.</summary>
    public interface IExportSession : IDisposable
    {
        /// <summary>
        /// Exports a single sheet. Must not throw for a per-sheet problem: return a failed
        /// <see cref="BatchItemResult"/> instead so the rest of the batch continues.
        /// </summary>
        BatchItemResult Export(SheetSnapshot sheet);
    }

    /// <summary>The engines available in this build.</summary>
    public sealed class ExportEngineCatalog
    {
        private readonly Dictionary<ExportFormat, IExportEngine> _engines = new Dictionary<ExportFormat, IExportEngine>();

        public ExportEngineCatalog(IEnumerable<IExportEngine> engines)
        {
            foreach (IExportEngine engine in engines)
            {
                if (engine != null) _engines[engine.Format] = engine;
            }
        }

        /// <summary>
        /// The default set for the Revit release this assembly was compiled for. The PDF engine is
        /// chosen at compile time: the native API from 2022 onward, a printer driver on 2021.
        /// </summary>
        public static ExportEngineCatalog CreateDefault() => new ExportEngineCatalog(new IExportEngine[]
        {
#if REVIT2022_OR_GREATER
            new Pdf.NativePdfExportEngine(),
#else
            new Pdf.PrintDriverPdfExportEngine(),
#endif
            new Dwg.DwgExportEngine()
        });

        public IEnumerable<ExportFormat> SupportedFormats => _engines.Keys;

        public IExportEngine Resolve(ExportFormat format)
        {
            if (_engines.TryGetValue(format, out IExportEngine engine)) return engine;

            throw new ExportSetupException(
                $"Esta versión del complemento no puede exportar a {ExportFormatInfo.Label(format)}.");
        }
    }
}
