using System.Windows;

namespace GvrTools.Licensing.Activation
{
    public partial class AccountLicenseWindow : Window
    {
        private readonly AccountLicenseViewModel _viewModel;

        public AccountLicenseWindow(AccountLicenseViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.RequestClose += Close;
            viewModel.RequestActivate += OpenActivate;
        }

        private void OpenActivate()
        {
            var reason = LicenseRuntime.Client.NeedsReactivation
                ? LicenseRuntime.Client.ReactivationReason
                : null;
            var activateVm = new ActivateLicenseViewModel(LicenseRuntime.Client, reason);
            var dialog = new ActivateLicenseWindow(activateVm) { Owner = this };
            if (dialog.ShowDialog() == true)
                _viewModel.Refresh();
        }
    }
}
