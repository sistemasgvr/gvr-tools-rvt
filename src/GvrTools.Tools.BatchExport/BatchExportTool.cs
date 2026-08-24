using System;
using System.Windows;
using System.Windows.Media;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Ribbon;
using GvrTools.UI.Icons;

namespace GvrTools.Tools.BatchExport
{
    /// <summary>
    /// Ribbon registration for the batch exporter. This class is all the host application needs to
    /// know about the tool: it is discovered by assembly scan and wired to the command below.
    /// </summary>
    public sealed class BatchExportTool : RevitToolBase
    {
        public override string Id => "GvrBatchExport";

        public override string Title => "Exportar" + Environment.NewLine + "láminas";

        public override string PanelName => "Exportación";

        public override int SortOrder => 10;

        public override Type CommandType => typeof(BatchExportCommand);

        public override string Tooltip =>
            "Exporta láminas a PDF o DWG de forma masiva, una carpeta por proyecto y un archivo por lámina.";

        public override string LongDescription => RevitVersionInfo.HasNativePdfExport
            ? "Usa el exportador de PDF nativo de Revit: no abre ventanas, no ocupa el teclado y el equipo " +
              "queda libre mientras se exporta."
            : "Revit " + RevitVersionInfo.CompiledFor + " no incluye API de PDF, así que el PDF se plotea con una " +
              "impresora PDF de Windows que escriba el archivo sin preguntar. DWG siempre usa la API nativa.";

        /// <summary>
        /// A document with a red PDF band and a blue export arrow, drawn with <see cref="VectorIcon"/>
        /// rather than the shared <see cref="BrandIcons"/> shield: that pack URI is read for the very
        /// first time here, during ribbon construction in OnStartup, before any window (or its
        /// InitializeComponent) has run anywhere in the process - too early for WPF's "pack:" URI
        /// scheme to be registered yet, so it silently failed and left the button blank. The window's
        /// own header icon works fine because it is set after the window's XAML has already loaded a
        /// pack URI once. Pure code has no such ordering dependency.
        /// </summary>
        public override ImageSource CreateIcon() => VectorIcon.Compose(
            VectorIcon.FilledRectangle(new Rect(6, 3, 20, 26), Colors.White, Color.FromRgb(0x45, 0x5A, 0x64), 1.5, 2),
            VectorIcon.Polygon(Color.FromRgb(0xCF, 0xD8, 0xDC), new Point(18, 3), new Point(26, 3), new Point(26, 11)),
            VectorIcon.Rectangle(new Rect(6, 20, 20, 9), Color.FromRgb(0xD3, 0x2F, 0x2F)),
            VectorIcon.Rectangle(new Rect(14, 6, 4, 7), Color.FromRgb(0x15, 0x65, 0xC0)),
            VectorIcon.Polygon(Color.FromRgb(0x15, 0x65, 0xC0), new Point(9, 13), new Point(23, 13), new Point(16, 19)));
    }
}
