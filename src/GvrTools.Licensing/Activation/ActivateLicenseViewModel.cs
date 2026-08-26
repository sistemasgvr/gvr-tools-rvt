using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GvrTools.Licensing.Storage;
using GvrTools.Licensing.Validation;
using GvrTools.UI.Mvvm;

namespace GvrTools.Licensing.Activation
{
    public sealed class ActivateLicenseViewModel : ObservableObject
    {
        private readonly LicenseClient _client;
        private readonly FileActivationProfileStore _profileStore;
        private CancellationTokenSource _cts;

        public ActivateLicenseViewModel(LicenseClient client, string initialMessage = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _profileStore = new FileActivationProfileStore();
            if (!string.IsNullOrWhiteSpace(initialMessage))
                _statusMessage = initialMessage.Trim();

            var profile = _profileStore.Load();
            _fullName = profile.FullName;
            _email = profile.Email;

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

            if (string.IsNullOrWhiteSpace(LicenseKey))
            {
                StatusMessage = "Completa la clave de licencia.";
                return;
            }

            if (!PersonNameValidator.TryNormalize(FullName, out string fullName, out string nameError))
            {
                StatusMessage = nameError;
                return;
            }

            if (!EmailValidator.TryNormalize(Email, out string email, out string emailError))
            {
                StatusMessage = emailError;
                return;
            }

            IsBusy = true;
            _cts = new CancellationTokenSource();
            try
            {
                await _client.ActivateAsync(LicenseKey, fullName, email, _cts.Token).ConfigureAwait(true);
                _profileStore.Save(fullName, email);
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
