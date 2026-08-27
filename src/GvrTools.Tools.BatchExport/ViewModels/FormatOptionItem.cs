using GvrTools.UI.Mvvm;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// Opción de formato en el paso 2: incluida o bloqueada con candado
    /// (UI_FREEMIUM_PLAN.md §3.1 — clic en bloqueada abre Cambiar plan).
    /// </summary>
    public sealed class FormatOptionItem : ObservableObject
    {
        public FormatOptionItem(FormatMode mode, string label, bool isLocked)
        {
            Mode = mode;
            Label = label;
            IsLocked = isLocked;
        }

        public FormatMode Mode { get; }

        public string Label { get; }

        public bool IsLocked { get; }

        /// <summary>Plain format name; lock/premium glyphs are drawn in XAML (no ★ / emoji in strings).</summary>
        public string DisplayLabel => Label;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
