using System;
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

        private DateTime? _lastExportedUtc;

        /// <summary>
        /// When this sheet last exported successfully (any format), from the per-project export
        /// history -- null means "never exported" (at least not from this PC/user). Set once when
        /// the window opens and again live after each successful export in the current run.
        /// </summary>
        public DateTime? LastExportedUtc
        {
            get => _lastExportedUtc;
            set
            {
                if (Set(ref _lastExportedUtc, value))
                    Raise(nameof(WasExported), nameof(ExportStatusLabel));
            }
        }

        public bool WasExported => _lastExportedUtc.HasValue;

        /// <summary>Short, human-relative status for the grid column ("Nunca exportado", "Exportado hace 3 días"...).</summary>
        public string ExportStatusLabel
        {
            get
            {
                if (_lastExportedUtc == null) return "Nunca exportado";

                TimeSpan elapsed = DateTime.UtcNow - _lastExportedUtc.Value;
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

                if (elapsed < TimeSpan.FromMinutes(1)) return "Exportado hace un momento";
                if (elapsed < TimeSpan.FromHours(1)) return $"Exportado hace {(int)elapsed.TotalMinutes} min";
                if (elapsed < TimeSpan.FromHours(24)) return $"Exportado hace {(int)elapsed.TotalHours} h";
                if (elapsed < TimeSpan.FromDays(7)) return $"Exportado hace {(int)elapsed.TotalDays} día(s)";

                return "Exportado el " + _lastExportedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy");
            }
        }

        private string _runProgressLabel = string.Empty;

        /// <summary>Estado del lote en curso en el paso Crear (vacío / Exportando… / OK / Error).</summary>
        public string RunProgressLabel
        {
            get => _runProgressLabel;
            set => Set(ref _runProgressLabel, value ?? string.Empty);
        }

        private bool _runSucceeded;
        public bool RunSucceeded
        {
            get => _runSucceeded;
            set => Set(ref _runSucceeded, value);
        }

        public void ResetRunProgress()
        {
            RunProgressLabel = string.Empty;
            RunSucceeded = false;
        }

        /// <summary>True when the row matches the current search term.</summary>
        public bool Matches(string term) =>
            Number.IndexOf(term, System.StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            Name.IndexOf(term, System.StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
