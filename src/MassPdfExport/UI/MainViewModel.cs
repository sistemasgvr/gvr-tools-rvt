using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using GvrTools.MassPdfExport.Core;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace GvrTools.MassPdfExport.UI
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        public const string AllSheetSetsLabel = "(Todas las láminas)";

        private readonly Document _document;
        private readonly PdfExportService _exportService = new PdfExportService();
        private readonly Dictionary<string, HashSet<ElementId>> _sheetSets;
        private readonly Dispatcher _dispatcher;

        private bool _cancelRequested;

        public ObservableCollection<SheetRow> Sheets { get; } = new ObservableCollection<SheetRow>();
        public ICollectionView SheetsView { get; }
        public List<string> SheetSetNames { get; }
        public string DocumentTitle { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (Set(ref _searchText, value)) SheetsView.Refresh(); }
        }

        private string _selectedSheetSet;
        public string SelectedSheetSet
        {
            get => _selectedSheetSet;
            set { if (Set(ref _selectedSheetSet, value)) SheetsView.Refresh(); }
        }

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set { if (Set(ref _outputFolder, value)) OnPropertyChanged(nameof(PreviewFolder)); }
        }

        public string PreviewFolder =>
            string.IsNullOrWhiteSpace(OutputFolder) ? string.Empty : Path.Combine(OutputFolder, FileNaming.Sanitize(DocumentTitle));

        private string _namingPattern = FileNaming.DefaultPattern;
        public string NamingPattern
        {
            get => _namingPattern;
            set => Set(ref _namingPattern, value);
        }

        private bool _openFolderWhenDone = true;
        public bool OpenFolderWhenDone
        {
            get => _openFolderWhenDone;
            set => Set(ref _openFolderWhenDone, value);
        }

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set => Set(ref _isExporting, value);
        }

        private int _progressCurrent;
        public int ProgressCurrent
        {
            get => _progressCurrent;
            set => Set(ref _progressCurrent, value);
        }

        private int _progressTotal = 1;
        public int ProgressTotal
        {
            get => _progressTotal;
            set => Set(ref _progressTotal, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand InvertSelectionCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CancelCommand { get; }

        public MainViewModel(Document document, IList<ViewSheet> sheets, Dictionary<string, HashSet<ElementId>> sheetSets)
        {
            _document = document;
            _sheetSets = sheetSets ?? new Dictionary<string, HashSet<ElementId>>();
            _dispatcher = Dispatcher.CurrentDispatcher;

            DocumentTitle = string.IsNullOrWhiteSpace(document.Title) ? "Proyecto" : document.Title;

            foreach (ViewSheet sheet in sheets)
                Sheets.Add(new SheetRow(sheet, SheetCollector.ToExportInfo(sheet)));

            SheetsView = CollectionViewSource.GetDefaultView(Sheets);
            SheetsView.Filter = FilterSheet;

            SheetSetNames = new List<string> { AllSheetSetsLabel };
            SheetSetNames.AddRange(_sheetSets.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase));
            _selectedSheetSet = AllSheetSetsLabel;

            OutputFolder = GetDefaultFolder(document);

            SelectAllCommand = new RelayCommand(_ => SetVisibleSelection(true));
            SelectNoneCommand = new RelayCommand(_ => SetVisibleSelection(false));
            InvertSelectionCommand = new RelayCommand(_ => InvertVisibleSelection());
            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            ExportCommand = new RelayCommand(_ => Export(), _ => CanExport());
            CancelCommand = new RelayCommand(_ => _cancelRequested = true, _ => IsExporting);

            StatusText = $"{Sheets.Count} lámina(s) encontradas en el proyecto.";
        }

        private bool FilterSheet(object obj)
        {
            if (!(obj is SheetRow row)) return false;

            if (!string.Equals(SelectedSheetSet, AllSheetSetsLabel, StringComparison.Ordinal))
            {
                if (!_sheetSets.TryGetValue(SelectedSheetSet ?? string.Empty, out HashSet<ElementId> ids) || !ids.Contains(row.Sheet.Id))
                    return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string term = SearchText.Trim();
            return row.SheetNumber.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.SheetName.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void SetVisibleSelection(bool selected)
        {
            foreach (SheetRow row in SheetsView.Cast<SheetRow>().ToList())
                row.IsSelected = selected;
        }

        private void InvertVisibleSelection()
        {
            foreach (SheetRow row in SheetsView.Cast<SheetRow>().ToList())
                row.IsSelected = !row.IsSelected;
        }

        private static string GetDefaultFolder(Document document)
        {
            try
            {
                string path = document.PathName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return Path.GetDirectoryName(path);
            }
            catch
            {
                // Cloud-hosted (BIM 360 / ACC) models can throw or return a non-local path here.
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Selecciona la carpeta donde se exportarán los PDF",
                ShowNewFolderButton = true
            })
            {
                if (!string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder))
                    dialog.SelectedPath = OutputFolder;

                if (dialog.ShowDialog() == DialogResult.OK)
                    OutputFolder = dialog.SelectedPath;
            }
        }

        private bool CanExport()
        {
            return !IsExporting
                && !string.IsNullOrWhiteSpace(OutputFolder)
                && Sheets.Any(s => s.IsSelected);
        }

        private void Export()
        {
            List<(ViewSheet Sheet, SheetExportInfo Info)> selected = Sheets
                .Where(s => s.IsSelected)
                .Select(s => (s.Sheet, s.Info))
                .ToList();

            if (selected.Count == 0 || string.IsNullOrWhiteSpace(OutputFolder)) return;

            string targetFolder = Path.Combine(OutputFolder, FileNaming.Sanitize(DocumentTitle));

            _cancelRequested = false;
            IsExporting = true;
            ProgressCurrent = 0;
            ProgressTotal = selected.Count;
            StatusText = "Preparando exportación...";
            RelayCommand.RaiseCanExecuteChanged();

            ExportSummary summary;
            try
            {
                summary = _exportService.ExportSheets(
                    _document,
                    selected,
                    targetFolder,
                    NamingPattern,
                    OnProgress,
                    () => _cancelRequested);
            }
            catch (Exception ex)
            {
                StatusText = "La exportación no pudo iniciarse.";
                MessageBox.Show(ex.Message, "GVR Tools - Exportación PDF masiva", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                IsExporting = false;
                RelayCommand.RaiseCanExecuteChanged();
            }

            StatusText = summary.WasCancelled
                ? $"Cancelado. {summary.SuccessCount} de {ProgressTotal} lámina(s) exportadas."
                : $"Listo. {summary.SuccessCount} de {ProgressTotal} lámina(s) exportadas.";

            ShowSummary(summary);

            if (summary.SuccessCount > 0 && OpenFolderWhenDone)
            {
                try { Process.Start("explorer.exe", summary.DestinationFolder); }
                catch { /* opening the output folder is a convenience, never fatal */ }
            }
        }

        private void OnProgress(ExportProgress progress)
        {
            ProgressCurrent = progress.Current;
            ProgressTotal = progress.Total;
            StatusText = $"Exportando {progress.Current} de {progress.Total}: {progress.Sheet.SheetNumber} - {progress.Sheet.SheetName}";
            PumpMessages();
        }

        /// <summary>
        /// Revit API calls are only valid on this (the main) thread, so the export loop runs
        /// synchronously here rather than on a background task. Pumping the dispatcher between
        /// sheets keeps the progress bar and the Cancel button responsive during the loop.
        /// </summary>
        private void PumpMessages()
        {
            _dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
        }

        private static void ShowSummary(ExportSummary summary)
        {
            List<SheetExportResult> failed = summary.Results.Where(r => !r.Success).ToList();

            string message = summary.WasCancelled
                ? $"Exportación cancelada.\n\nLáminas exportadas: {summary.SuccessCount}"
                : $"Exportación finalizada.\n\nLáminas exportadas: {summary.SuccessCount}\nErrores: {summary.FailureCount}\nCarpeta: {summary.DestinationFolder}";

            if (failed.Count > 0)
            {
                message += "\n\nLáminas con error:\n" + string.Join("\n",
                    failed.Take(15).Select(r => $"- {r.Sheet.SheetNumber} {r.Sheet.SheetName}: {r.ErrorMessage}"));
                if (failed.Count > 15)
                    message += $"\n... y {failed.Count - 15} más.";
            }

            MessageBox.Show(
                message,
                "GVR Tools - Exportación PDF masiva",
                MessageBoxButton.OK,
                failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
