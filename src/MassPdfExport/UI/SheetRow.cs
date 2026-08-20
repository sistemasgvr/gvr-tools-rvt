using System.ComponentModel;
using Autodesk.Revit.DB;
using GvrTools.MassPdfExport.Core;

namespace GvrTools.MassPdfExport.UI
{
    public sealed class SheetRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public ViewSheet Sheet { get; }
        public SheetExportInfo Info { get; }

        public string SheetNumber => Info.SheetNumber;
        public string SheetName => Info.SheetName;
        public string RevisionNumber => Info.RevisionNumber;
        public string RevisionDescription => Info.RevisionDescription;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public SheetRow(ViewSheet sheet, SheetExportInfo info)
        {
            Sheet = sheet;
            Info = info;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
