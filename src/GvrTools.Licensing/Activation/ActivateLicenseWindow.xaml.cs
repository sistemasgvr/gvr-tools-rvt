using System.Windows;

namespace GvrTools.Licensing.Activation
{
    public partial class ActivateLicenseWindow : Window
    {
        public ActivateLicenseWindow(ActivateLicenseViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += accepted =>
            {
                DialogResult = accepted;
                Close();
            };
        }
    }
}
