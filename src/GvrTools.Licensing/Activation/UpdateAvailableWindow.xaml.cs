using System.Windows;
using GvrTools.UI.Icons;

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

            Icon = BrandIcons.Escudo;
            HeaderIcon.Source = BrandIcons.Escudo;
        }
    }
}
