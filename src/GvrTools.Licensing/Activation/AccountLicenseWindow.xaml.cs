using System.Windows;
using GvrTools.UI.Icons;

namespace GvrTools.Licensing.Activation
{
    public partial class AccountLicenseWindow : Window
    {
        private readonly AccountLicenseViewModel _viewModel;
        private bool _activated;

        public AccountLicenseWindow(AccountLicenseViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.RequestClose += OnRequestClose;

            Icon = BrandIcons.Escudo;
            HeaderIcon.Source = BrandIcons.Escudo;
        }

        /// <summary>true si el usuario activó una clave en esta sesión de diálogo.</summary>
        public bool ActivatedSuccessfully => _activated;

        private void OnRequestClose(bool activated)
        {
            _activated = activated;
            DialogResult = activated;
            Close();
        }
    }
}
