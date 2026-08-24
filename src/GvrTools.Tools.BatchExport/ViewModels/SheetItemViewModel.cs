using GvrTools.Revit.Model;
using GvrTools.UI.Mvvm;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>One row of the sheet grid.</summary>
    public sealed class SheetItemViewModel : ObservableObject
    {
        private bool _isSelected = true;

        public SheetItemViewModel(SheetSnapshot sheet)
        {
            Sheet = sheet;
        }

        public SheetSnapshot Sheet { get; }

        public string Number => Sheet.Number;

        public string Name => Sheet.Name;

        public string RevisionNumber => Sheet.RevisionNumber;

        public string RevisionDescription => Sheet.RevisionDescription;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        /// <summary>True when the row matches the current search term.</summary>
        public bool Matches(string term) =>
            Number.IndexOf(term, System.StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            Name.IndexOf(term, System.StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
