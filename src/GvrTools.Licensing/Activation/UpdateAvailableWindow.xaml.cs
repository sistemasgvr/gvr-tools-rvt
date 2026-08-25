using System.Windows;

namespace GvrTools.Licensing.Activation
{
    public partial class UpdateAvailableWindow : Window
    {
        public UpdateAvailableWindow(UpdateAvailableViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += () =>
            {
                try { Close(); }
                catch { /* already closing */ }
            };
        }
    }
}
