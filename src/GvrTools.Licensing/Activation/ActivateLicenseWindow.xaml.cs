using System.Windows;

namespace GvrTools.Licensing.Activation
{
    public partial class ActivateLicenseWindow : Window
    {
        public ActivateLicenseWindow(ActivateLicenseViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            // Solo cerrar el diálogo. El aviso + reinicio de Revit lo hace LicenseUi
            // DESPUÉS de que ShowDialog retorne (evita error irrecuperable por cerrar
            // Revit desde dentro del stack modal).
            viewModel.RequestClose += accepted =>
            {
                DialogResult = accepted;
                Close();
            };
        }
    }
}
