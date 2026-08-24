namespace GvrTools.Revit.Infrastructure
{
    /// <summary>
    /// What the current binary was compiled against. Lets runtime code and user-facing messages
    /// explain *why* a given export path was chosen without duplicating the <c>#if</c> ladder.
    /// </summary>
    public static class RevitVersionInfo
    {
#if REVIT2025
        public const int CompiledFor = 2025;
#elif REVIT2024
        public const int CompiledFor = 2024;
#elif REVIT2023
        public const int CompiledFor = 2023;
#elif REVIT2022
        public const int CompiledFor = 2022;
#else
        public const int CompiledFor = 2021;
#endif

        /// <summary>
        /// True when Revit exposes a real PDF export API (<c>PDFExportOptions</c>, added in 2022).
        /// When false, PDF has to be plotted through a Windows printer driver instead.
        /// </summary>
#if REVIT2022_OR_GREATER
        public const bool HasNativePdfExport = true;
#else
        public const bool HasNativePdfExport = false;
#endif
    }
}
