using System.Windows;
using GvrTools.Tools.BatchExport.ViewModels;
using GvrTools.UI.Icons;

namespace GvrTools.Tools.BatchExport.Views
{
    /// <summary>Shown non-modal (Show), same as BatchExportWindow -- a summary popup should never block Revit.</summary>
    public partial class ExportSuccessWindow : Window
    {
        public ExportSuccessWindow(ExportSuccessViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
            viewModel.RequestClose += Close;

            Icon = BrandIcons.Escudo;
            HeaderIcon.Source = BrandIcons.Escudo;
        }
    }
}
