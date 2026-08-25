using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GvrTools.Licensing.Entitlements;
using GvrTools.UI.Mvvm;

namespace GvrTools.Licensing.Activation
{
    public sealed class AccountLicenseViewModel : ObservableObject
    {
        private readonly LicenseClient _client;

        public AccountLicenseViewModel(LicenseClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Refresh();
            ActivateCommand = new RelayCommand(() => RequestActivate?.Invoke());
            DeactivateCommand = new RelayCommand(async () => await DeactivateAsync(), () => IsLicensed && !IsBusy);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        public event Action RequestActivate;
        public event Action RequestClose;

        public void Refresh()
        {
            Raise(
                nameof(IsLicensed),
                nameof(PlanSummary),
                nameof(GraceSummary),
                nameof(QuotaSummary),
                nameof(StatusHeadline),
                nameof(SupportHint));
            DeactivateCommand.RaiseCanExecuteChanged();
        }

        public bool IsLicensed => _client.IsLicensed;

        public string StatusHeadline =>
            IsLicensed
                ? "Licencia activa"
                : (_client.NeedsReactivation
                    ? "Sesión expirada — reactiva"
                    : "Sin licencia válida");

        public string PlanSummary =>
            IsLicensed
                ? ("Plan: " + (_client.PlanCode ?? "—"))
                : (!string.IsNullOrWhiteSpace(_client.ReactivationReason)
                    ? _client.ReactivationReason
                    : "Activa una clave GVR-… para usar las herramientas.");

        public string GraceSummary
        {
            get
            {
                if (!IsLicensed || _client.OfflineUntilUtc == null) return string.Empty;
                return "Válida sin conexión hasta: " +
                       _client.OfflineUntilUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            }
        }

        public string QuotaSummary
        {
            get
            {
                if (!IsLicensed) return string.Empty;
                var remaining = _client.Entitlements.Remaining(FeatureCodes.QuotaSheetsPerMonth);
                if (remaining < 0) return "Cuota de láminas este mes: ilimitada";
                return "Láminas restantes este mes: " + remaining.ToString(CultureInfo.CurrentCulture);
            }
        }

        public string SupportHint =>
            "Soporte: " + (_client.SupportEmailHint ?? "contacta a tu administrador GVR");

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (Set(ref _isBusy, value))
                    DeactivateCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand ActivateCommand { get; }
        public RelayCommand DeactivateCommand { get; }
        public RelayCommand CloseCommand { get; }

        private async Task DeactivateAsync()
        {
            var confirm = MessageBox.Show(
                "¿Liberar este PC? Dejarás de poder usar GVR Tools aquí hasta volver a activar.",
                "Desactivar este PC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = null;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    await _client.DeactivateAsync(cts.Token).ConfigureAwait(true);
                }

                StatusMessage = "PC liberado.";
                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo desactivar: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
