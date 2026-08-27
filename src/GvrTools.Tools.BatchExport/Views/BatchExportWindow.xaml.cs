using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using GvrTools.Licensing.Activation;
using GvrTools.Tools.BatchExport.ViewModels;
using GvrTools.UI.Icons;

namespace GvrTools.Tools.BatchExport.Views
{
    /// <summary>
    /// The exporter window.
    ///
    /// Shown modeless (Show, not ShowDialog) on purpose: a modal dialog freezes Revit for as long as
    /// it is open, and the whole point of this tool is that a long export leaves both Revit and the
    /// rest of the machine usable.
    /// </summary>
    public partial class BatchExportWindow : Window
    {
        private readonly BatchExportViewModel _viewModel;
        private readonly Action _onClosed;

        public BatchExportWindow(BatchExportViewModel viewModel, Action onClosed = null)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _onClosed = onClosed;
            DataContext = viewModel;

            // Set from code-behind rather than from XAML: the brand assets live in another
            // assembly (GvrTools.UI), and referencing them via {x:Static} would drag the whole
            // XAML type-resolution chain across the assembly boundary — cheaper and clearer to
            // pull the frozen image from its lazy holder here.
            Icon = BrandIcons.Escudo;
            HeaderIcon.Source = BrandIcons.Escudo;

            viewModel.RequestChangePlan += OnRequestChangePlan;
            viewModel.ExportSucceeded += OnExportSucceeded;
        }

        /// <summary>"Cambiar plan": mismo flujo que Cuenta/Licencia en la cinta (LicenseUi.ShowAccount), sin duplicar esa lógica.</summary>
        private void OnRequestChangePlan()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            LicenseUi.ShowChangePlan(ownerHwnd: hwnd);
        }

        private void OnExportSucceeded(ExportSummary summary)
        {
            var successVm = new ExportSuccessViewModel(summary, _viewModel.QuotaFooterText, _viewModel.Dialogs);
            successVm.RequestChangePlan += OnRequestChangePlan;
            var window = new ExportSuccessWindow(successVm) { Owner = this };
            window.Show();
        }

        /// <summary>
        /// Closing mid-run would leave the scheduler writing into a dead window, so the first
        /// attempt cancels the run instead and the window closes once the current sheet finishes.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_viewModel.IsExporting)
            {
                e.Cancel = true;
                _viewModel.CancelCommand.Execute(null);
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.Dispose();
            _onClosed?.Invoke();

            base.OnClosed(e);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
