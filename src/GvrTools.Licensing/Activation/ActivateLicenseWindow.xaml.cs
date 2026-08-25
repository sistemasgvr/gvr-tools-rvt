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

                if (accepted)
                {
                    MessageBox.Show(
                        "Licencia activada correctamente.\n\n" +
                        "Al pulsar Aceptar, Revit se cerrará y se volverá a abrir solo para cargar todas las herramientas de tu plan.\n\n" +
                        "Si tenías un proyecto guardado abierto, se reabrirá automáticamente.",
                        "GVR Tools · Reiniciar Revit",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    LicenseUi.RequestApplicationClose?.Invoke();
                }
            };
        }
    }
}
