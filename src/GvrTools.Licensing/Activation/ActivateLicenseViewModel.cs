using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GvrTools.UI.Mvvm;

namespace GvrTools.Licensing.Activation
{
    public sealed class ActivateLicenseViewModel : ObservableObject
    {
        private readonly LicenseClient _client;
        private CancellationTokenSource _cts;

        public ActivateLicenseViewModel(LicenseClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            ActivateCommand = new RelayCommand(async () => await ActivateAsync(), () => !IsBusy);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public event Action<bool> RequestClose;

        private string _licenseKey;
        public string LicenseKey
        {
            get => _licenseKey;
            set
            {
                if (Set(ref _licenseKey, value))
                    ActivateCommand.RaiseCanExecuteChanged();
            }
        }

        private string _fullName;
        public string FullName
        {
            get => _fullName;
            set => Set(ref _fullName, value);
        }

        private string _email;
        public string Email
        {
            get => _email;
            set => Set(ref _email, value);
        }

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
                    ActivateCommand.RaiseCanExecuteChanged();
            }
        }

        public string SupportHint =>
            "Si no tienes una clave, contacta a " + (_client.SupportEmailHint ?? "soporte") + ".";

        public RelayCommand ActivateCommand { get; }
        public RelayCommand CancelCommand { get; }

        private async Task ActivateAsync()
        {
            StatusMessage = null;

            if (string.IsNullOrWhiteSpace(LicenseKey) ||
                string.IsNullOrWhiteSpace(FullName) ||
                string.IsNullOrWhiteSpace(Email))
            {
                StatusMessage = "Completa la clave, tu nombre y tu correo.";
                return;
            }

            IsBusy = true;
            _cts = new CancellationTokenSource();
            try
            {
                await _client.ActivateAsync(LicenseKey, FullName, Email, _cts.Token).ConfigureAwait(true);
                StatusMessage = "Licencia activada.";
                RequestClose?.Invoke(true);
            }
            catch (Http.LicenseApiClientException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo activar: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                _cts.Dispose();
                _cts = null;
            }
        }
    }
}
