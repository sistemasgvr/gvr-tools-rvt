using System;
using GvrTools.UI.Mvvm;
using GvrTools.UI.Services;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// ExportSuccessWindow's logic. Pure presentation over an already-finished <see cref="ExportSummary"/>
    /// -- no export/quota logic of its own, so it can never disagree with what BatchExportViewModel
    /// actually did.
    /// </summary>
    public sealed class ExportSuccessViewModel : ObservableObject
    {
        private readonly IUserDialogs _dialogs;
        private readonly ExportSummary _summary;

        public ExportSuccessViewModel(ExportSummary summary, string quotaText, IUserDialogs dialogs)
        {
            _summary = summary ?? throw new ArgumentNullException(nameof(summary));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            QuotaText = quotaText ?? string.Empty;

            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(_summary.RevealTarget));
            ChangePlanCommand = new RelayCommand(() => RequestChangePlan?.Invoke());
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public event Action RequestChangePlan;
        public event Action RequestClose;

        public string SummaryText => _summary.FailedCount > 0
            ? $"{_summary.SucceededCount} lámina(s) exportada(s), {_summary.FailedCount} con error."
            : $"{_summary.SucceededCount} lámina(s) exportada(s) correctamente.";

        public string FolderText => _summary.FolderText;

        public string QuotaText { get; }

        public bool HasQuotaText => !string.IsNullOrWhiteSpace(QuotaText);

        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand ChangePlanCommand { get; }
        public RelayCommand CloseCommand { get; }
    }
}
