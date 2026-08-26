using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.History;
using GvrTools.Core.IO;
using GvrTools.Core.Naming;
using GvrTools.Core.Settings;
using GvrTools.Revit.Export;
using GvrTools.Revit.Export.Dwg;
using GvrTools.Revit.Export.Pdf;
using GvrTools.Revit.Infrastructure;
using GvrTools.Revit.Model;
using GvrTools.Revit.Sheets;
using GvrTools.UI.Mvvm;
using GvrTools.UI.Services;
using GvrTools.Licensing;
using GvrTools.Licensing.Entitlements;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// Window logic for the batch sheet exporter.
    ///
    /// The export itself is delegated to a <see cref="BatchExportJob"/> driven by a
    /// <see cref="RevitJobScheduler"/>, which is what keeps the window (and Revit) usable while the
    /// batch runs. All the callbacks below arrive on Revit's own thread, which is also this window's
    /// dispatcher thread, so they can update bound properties directly without marshalling.
    /// </summary>
    public sealed class BatchExportViewModel : ObservableObject, IDisposable
    {
        public const string AllSheetsLabel = "(Todas las láminas)";
        private const string DialogTitle = "GVR Tools - Exportación masiva";

        /// <summary>
        /// The only PDF printer this tool is allowed to use on Revit 2021 (matched by name/driver,
        /// not an exact string, since the exact installed name can vary by PDF24 version). Fixed by
        /// policy rather than left as a user choice: with several printers behaving differently,
        /// letting anyone be picked reintroduces the "why did this batch stop on a dialog" problem
        /// PdfPrinterCatalog exists to avoid. If PDF24 is ever swapped out, only this constant and
        /// the messages below need to change.
        /// </summary>
        private const string RequiredPdfPrinterHint = "PDF24";
        private const string RequiredPdfPrinterDisplayName = "PDF24 Toolbox";

        private readonly UIDocument _uiDocument;
        private readonly RevitJobScheduler _scheduler;
        private readonly IUserDialogs _dialogs;
        private readonly ISettingsStore _settingsStore;
        private readonly ISheetExportHistoryStore _historyStore;
        private readonly ILog _log;
        private readonly ExportEngineCatalog _engines;
        private readonly ProjectSnapshot _project;
        private readonly IReadOnlyList<SheetSetSnapshot> _sheetSets;
        private readonly BatchExportPreferences _preferences;
        private readonly Dictionary<string, DateTime> _exportHistory;

        public BatchExportViewModel(
            UIDocument uiDocument,
            RevitJobScheduler scheduler,
            IUserDialogs dialogs,
            ISettingsStore settingsStore,
            ISheetExportHistoryStore historyStore,
            ILog log)
        {
            _uiDocument = uiDocument;
            _scheduler = scheduler;
            _dialogs = dialogs;
            _settingsStore = settingsStore;
            _historyStore = historyStore ?? new SheetExportHistoryStore();
            _log = log ?? NullLog.Instance;
            _engines = ExportEngineCatalog.CreateDefault();

            _project = ProjectSnapshot.Read(uiDocument.Document);
            _sheetSets = SheetRepository.GetSheetSets(uiDocument.Document);

            // Dictionary<,>(IReadOnlyDictionary<,>) is not available on net48 (Revit 2021-2024) --
            // copy explicitly instead so this builds the same way on every target framework.
            _exportHistory = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, DateTime> entry in _historyStore.Load(_project.ProjectKey))
                _exportHistory[entry.Key] = entry.Value;

            foreach (SheetSnapshot sheet in SheetRepository.GetSheets(uiDocument.Document))
            {
                var item = new SheetItemViewModel(sheet);
                if (!string.IsNullOrEmpty(sheet.UniqueId) && _exportHistory.TryGetValue(sheet.UniqueId, out DateTime lastExported))
                    item.LastExportedUtc = lastExported;

                item.PropertyChanged += OnSheetItemChanged;
                Sheets.Add(item);
            }

            SheetSetNames = new[] { AllSheetsLabel }
                .Concat(_sheetSets.Select(set => set.Name))
                .ToList();

            // The filter must not run before the set selector has a value: assigning Filter
            // evaluates it once immediately, and a null selection would hide every row.
            _selectedSheetSet = AllSheetsLabel;
            SheetsView = CollectionViewSource.GetDefaultView(Sheets);
            SheetsView.Filter = PassesFilter;

            _preferences = _settingsStore.Load<BatchExportPreferences>(BatchExportPreferences.StorageKey);
            ApplyPreferences(_preferences);
            FormatChoices = BuildFormatChoices();
            EnsureSelectedFormatAllowed();

            SelectAllCommand = new RelayCommand(() => SetVisibleSelection(true));
            SelectNoneCommand = new RelayCommand(() => SetVisibleSelection(false));
            InvertSelectionCommand = new RelayCommand(InvertVisibleSelection);
            SelectPendingCommand = new RelayCommand(SelectVisiblePending);
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(RevealTarget));
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);

            StatusText = Sheets.Count == 0
                ? "El proyecto activo no tiene láminas para exportar."
                : $"{Sheets.Count} lámina(s) en el proyecto.";
        }

        // ---------------------------------------------------------------- sheet list

        public ObservableCollection<SheetItemViewModel> Sheets { get; } = new ObservableCollection<SheetItemViewModel>();

        public ICollectionView SheetsView { get; }

        public IReadOnlyList<string> SheetSetNames { get; }

        /// <summary>
        /// The saved-sheet-set filter is only useful when the project actually has any. On projects
        /// with none, the combo would only offer "(Todas las láminas)", which is UI noise and made
        /// people wonder what it was for; the window binds its visibility to this.
        /// </summary>
        public bool HasSheetSets => _sheetSets.Count > 0;

        public ObservableCollection<ExportResultViewModel> Results { get; } = new ObservableCollection<ExportResultViewModel>();

        public string DocumentTitle => _project.Title;

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

        public int SelectedCount => Sheets.Count(sheet => sheet.IsSelected);

        public string SelectionSummary => $"{SelectedCount} de {Sheets.Count} lámina(s) seleccionadas";

        // ---------------------------------------------------------------- destination and naming

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (!Set(ref _outputFolder, value)) return;

                Raise(nameof(DestinationFolder), nameof(CanExport));
                ExportCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Each run writes into a subfolder named after the Revit file, prefixed by format
        /// ("PDF_"/"DWG_") so a combined PDF+DWG run keeps the two kinds of files apart instead of
        /// mixing them into one folder.
        /// </summary>
        public string DestinationFolder
        {
            get
            {
                if (string.IsNullOrWhiteSpace(OutputFolder)) return string.Empty;

                if (_selectedFormatMode == FormatMode.PdfAndDwg)
                    return $"{GetDestinationFolder(ExportFormat.Pdf)}  y  {GetDestinationFolder(ExportFormat.Dwg)}";

                return GetDestinationFolder(_selectedFormatMode == FormatMode.Dwg ? ExportFormat.Dwg : ExportFormat.Pdf);
            }
        }

        private string GetDestinationFolder(ExportFormat format)
        {
            if (string.IsNullOrWhiteSpace(OutputFolder)) return string.Empty;

            string prefix = format == ExportFormat.Pdf ? "PDF_" : "DWG_";
            return Path.Combine(OutputFolder, prefix + PathSanitizer.SanitizeFolderName(_project.Title));
        }

        /// <summary>
        /// Where "Abrir carpeta" and the post-export auto-reveal should point: the one format
        /// subfolder normally, or the shared parent when a combined run produced two of them.
        /// </summary>
        private string RevealTarget => _selectedFormatMode == FormatMode.PdfAndDwg ? OutputFolder : DestinationFolder;

        // ---------------------------------------------------------------- naming pattern

        /// <summary>Every token the pattern box understands, for the help text under it.</summary>
        public string NamingHelpText => NamingTokens.HelpText;

        /// <summary>Presets offered as a starting point; picking one just fills the pattern box.</summary>
        public IReadOnlyList<NamingPreset> NamingPresetChoices { get; } = NamingPresets.All;

        private string _namingPattern = NamingTokens.DefaultPattern;
        public string NamingPattern
        {
            get => _namingPattern;
            set
            {
                if (!Set(ref _namingPattern, value)) return;
                Raise(nameof(NamingPreview));
            }
        }

        /// <summary>
        /// Live "this is what the file will be called" example, built from the first sheet in the
        /// project (selection/filter do not matter for a naming preview) using the exact same
        /// <see cref="ExportFileNamer"/> the real export uses, so the preview can never drift from
        /// what actually gets written to disk.
        /// </summary>
        public string NamingPreview
        {
            get
            {
                SheetSnapshot sample = Sheets.Count > 0 ? Sheets[0].Sheet : null;
                if (sample == null) return string.IsNullOrWhiteSpace(NamingPattern) ? string.Empty : "(sin láminas para previsualizar)";

                var namer = new ExportFileNamer(string.Empty, NamingPattern, string.Empty, _project.ToTokens());
                return namer.Preview(sample);
            }
        }

        /// <summary>
        /// Write-only selector: picking a preset copies its pattern into <see cref="NamingPattern"/>
        /// and the box goes back to plain text editing. There is deliberately no "which preset is
        /// this" state to keep in sync afterwards -- once the user edits the text it no longer
        /// matches any preset anyway, so tracking a selected item would just go stale.
        /// </summary>
        public NamingPreset SelectedNamingPreset
        {
            get => null;
            set
            {
                if (value != null) NamingPattern = value.Pattern;
            }
        }

        // ---------------------------------------------------------------- format and options

        public IReadOnlyList<ChoiceItem<FormatMode>> FormatChoices { get; private set; }

        private static IReadOnlyList<ChoiceItem<FormatMode>> BuildFormatChoices()
        {
            LicenseRuntime.EnsureInitialized();
            var entitlements = LicenseRuntime.Entitlements;
            var list = new List<ChoiceItem<FormatMode>>();

            if (entitlements.CanUse(FeatureCodes.FormatPdf))
                list.Add(ChoiceItem.Of(FormatMode.Pdf, "PDF"));
            if (entitlements.CanUse(FeatureCodes.FormatDwg))
                list.Add(ChoiceItem.Of(FormatMode.Dwg, "DWG"));
            if (entitlements.CanUse(FeatureCodes.FormatPdfDwg) ||
                (entitlements.CanUse(FeatureCodes.FormatPdf) && entitlements.CanUse(FeatureCodes.FormatDwg)))
                list.Add(ChoiceItem.Of(FormatMode.PdfAndDwg, "PDF + DWG"));

            return list;
        }

        private void EnsureSelectedFormatAllowed()
        {
            if (FormatChoices.Count == 0)
                return;
            if (FormatChoices.Any(c => Equals(c.Value, _selectedFormatMode))) return;
            _selectedFormatMode = FormatChoices[0].Value;
        }

        private FormatMode _selectedFormatMode = FormatMode.Pdf;
        public FormatMode SelectedFormatMode
        {
            get => _selectedFormatMode;
            set
            {
                if (!Set(ref _selectedFormatMode, value)) return;

                Raise(nameof(ShowPdfOptions), nameof(ShowDwgOptions), nameof(ExportButtonLabel),
                      nameof(StrategyDescription), nameof(ShowPrinterSelector), nameof(IsPdfPrinterMissing),
                      nameof(CanExport), nameof(DestinationFolder));
                ExportCommand.RaiseCanExecuteChanged();
            }
        }

        public bool ShowPdfOptions => _selectedFormatMode != FormatMode.Dwg;

        public bool ShowDwgOptions => _selectedFormatMode != FormatMode.Pdf;

        public string ExportButtonLabel
        {
            get
            {
                if (_selectedFormatMode == FormatMode.PdfAndDwg) return "Exportar PDF + DWG";
                if (_selectedFormatMode == FormatMode.Dwg) return "Exportar DWG";
                return "Exportar PDF";
            }
        }

        /// <summary>Tells the user how the files will be produced, which differs by Revit version.</summary>
        public string StrategyDescription
        {
            get
            {
                if (_selectedFormatMode == FormatMode.PdfAndDwg)
                {
                    string pdf = GetEngineDescription(ExportFormat.Pdf);
                    return $"PDF: {pdf} · DWG: exportación nativa de Revit.";
                }

                ExportFormat format = _selectedFormatMode == FormatMode.Pdf ? ExportFormat.Pdf : ExportFormat.Dwg;
                return GetEngineDescription(format);
            }
        }

        private string GetEngineDescription(ExportFormat format)
        {
            try { return _engines.Resolve(format).StrategyDescription; }
            catch (ExportSetupException ex) { return ex.Message; }
        }

        public IReadOnlyList<ChoiceItem<PdfColorMode>> PdfColorModes { get; } = ChoiceItem.List(
            ChoiceItem.Of(PdfColorMode.Color, "Color"),
            ChoiceItem.Of(PdfColorMode.GrayScale, "Escala de grises"),
            ChoiceItem.Of(PdfColorMode.BlackAndWhite, "Blanco y negro"));

        public IReadOnlyList<ChoiceItem<PdfRasterQuality>> PdfRasterQualities { get; } = ChoiceItem.List(
            ChoiceItem.Of(PdfRasterQuality.Low, "Baja"),
            ChoiceItem.Of(PdfRasterQuality.Medium, "Media"),
            ChoiceItem.Of(PdfRasterQuality.High, "Alta"),
            ChoiceItem.Of(PdfRasterQuality.Presentation, "Presentación"));

        public IReadOnlyList<ChoiceItem<DwgFileVersion>> DwgFileVersions { get; } = ChoiceItem.List(
            ChoiceItem.Of(DwgFileVersion.Default, "Predeterminada"),
            ChoiceItem.Of(DwgFileVersion.R2018, "AutoCAD 2018"),
            ChoiceItem.Of(DwgFileVersion.R2013, "AutoCAD 2013"),
            ChoiceItem.Of(DwgFileVersion.R2010, "AutoCAD 2010"),
            ChoiceItem.Of(DwgFileVersion.R2007, "AutoCAD 2007"));

        /// <summary>Only the Revit 2021 build plots through a printer, so only it shows the printer status line.</summary>
        public bool ShowPrinterSelector => ShowPdfOptions && PdfPrinterOptions.IsPrinterRequired;

        /// <summary>
        /// Resolved fresh from what is actually installed (never from a stored preference): a
        /// printer that existed last session might not exist this one, and a stale name would either
        /// silently fall back to some other driver or produce a misleading error.
        /// </summary>
        private string _selectedPrinter;
        public string SelectedPrinter
        {
            get => _selectedPrinter;
            private set { if (Set(ref _selectedPrinter, value)) Raise(nameof(PrinterHint), nameof(IsPdfPrinterMissing)); }
        }

        /// <summary>True when Revit 2021 needs a printer and PDF24 specifically was not found.</summary>
        public bool IsPdfPrinterMissing => ShowPrinterSelector && string.IsNullOrEmpty(SelectedPrinter);

        /// <summary>
        /// One line about the fixed PDF24 requirement: confirms it is present, or explains why
        /// exporting to PDF is blocked. There is no picker to explain alternatives for any more - the
        /// tool only ever uses PDF24, so silently falling back to a different installed printer is
        /// exactly the "which driver actually ran" surprise PdfPrinterCatalog was built to prevent.
        /// </summary>
        public string PrinterHint => IsPdfPrinterMissing
            ? $"{RequiredPdfPrinterDisplayName} no está instalado. Instálalo (gratis, pdf24.org) para exportar a PDF en Revit 2021, o exporta a DWG mientras tanto."
            : $"Se exportará con {RequiredPdfPrinterDisplayName}.";

        private bool _pdfMatchSheetSize = true;
        public bool PdfMatchSheetSize
        {
            get => _pdfMatchSheetSize;
            set => Set(ref _pdfMatchSheetSize, value);
        }

        private bool _pdfFitToPage = true;
        public bool PdfFitToPage
        {
            get => _pdfFitToPage;
            set => Set(ref _pdfFitToPage, value);
        }

        private bool _pdfNoMargin = true;
        public bool PdfNoMargin
        {
            get => _pdfNoMargin;
            set => Set(ref _pdfNoMargin, value);
        }

        private PdfColorMode _pdfColorMode = PdfColorMode.Color;
        public PdfColorMode PdfColorMode
        {
            get => _pdfColorMode;
            set => Set(ref _pdfColorMode, value);
        }

        private PdfRasterQuality _pdfRasterQuality = PdfRasterQuality.High;
        public PdfRasterQuality PdfRasterQuality
        {
            get => _pdfRasterQuality;
            set => Set(ref _pdfRasterQuality, value);
        }

        private DwgFileVersion _dwgFileVersion = DwgFileVersion.Default;
        public DwgFileVersion DwgFileVersion
        {
            get => _dwgFileVersion;
            set => Set(ref _dwgFileVersion, value);
        }

        private bool _dwgMergeViews = true;
        public bool DwgMergeViews
        {
            get => _dwgMergeViews;
            set => Set(ref _dwgMergeViews, value);
        }

        private bool _dwgSharedCoordinates;
        public bool DwgSharedCoordinates
        {
            get => _dwgSharedCoordinates;
            set => Set(ref _dwgSharedCoordinates, value);
        }

        private bool _dwgAlsoExportImage;
        public bool DwgAlsoExportImage
        {
            get => _dwgAlsoExportImage;
            set => Set(ref _dwgAlsoExportImage, value);
        }

        private bool _openFolderWhenDone = true;
        public bool OpenFolderWhenDone
        {
            get => _openFolderWhenDone;
            set => Set(ref _openFolderWhenDone, value);
        }

        // ---------------------------------------------------------------- run state

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                if (!Set(ref _isExporting, value)) return;

                Raise(nameof(CanEditOptions));
                RefreshCommands();
            }
        }

        /// <summary>Options are locked while a run is in flight, so a job never changes shape mid-flight.</summary>
        public bool CanEditOptions => !IsExporting;

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set => Set(ref _progressValue, value);
        }

        private int _progressMaximum = 1;
        public int ProgressMaximum
        {
            get => _progressMaximum;
            set => Set(ref _progressMaximum, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        private bool _showResults;
        public bool ShowResults
        {
            get => _showResults;
            set => Set(ref _showResults, value);
        }

        private bool _lastRunHadFailures;
        public bool LastRunHadFailures
        {
            get => _lastRunHadFailures;
            set => Set(ref _lastRunHadFailures, value);
        }

        public bool CanExport =>
            !IsExporting &&
            FormatChoices.Count > 0 &&
            SelectedCount > 0 &&
            !string.IsNullOrWhiteSpace(OutputFolder) &&
            !(ShowPdfOptions && IsPdfPrinterMissing);

        // ---------------------------------------------------------------- commands

        public RelayCommand SelectAllCommand { get; }

        public RelayCommand SelectNoneCommand { get; }

        public RelayCommand InvertSelectionCommand { get; }

        /// <summary>Marks only the visible sheets that have never exported successfully -- the "what's left" shortcut.</summary>
        public RelayCommand SelectPendingCommand { get; }

        public RelayCommand BrowseFolderCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        // ---------------------------------------------------------------- multi-format state

        private bool _isMultiFormat;
        private int _multiFormatPhase;
        private int _progressOffset;
        private int _totalSteps;
        private ExportFormat _currentExportFormat;
        private List<SheetSnapshot> _pendingSheets;
        private BatchResult _firstPhaseResult;

        /// <summary>
        /// How many <see cref="OnItemCompleted"/> callbacks have fired since <see cref="LaunchFormat"/>
        /// started the current phase -- <see cref="BatchItemResult"/> does not carry the sheet
        /// identity, but the scheduler guarantees one step per sheet in <c>_pendingSheets</c> order
        /// with no skipping or parallelism, so this index reliably points at the sheet each result
        /// belongs to (see <see cref="BatchExportJob.ExecuteStep"/>).
        /// </summary>
        private int _itemsCompletedInPhase;
        private Dictionary<SheetSnapshot, SheetItemViewModel> _itemsBySheet;

        // ---------------------------------------------------------------- behaviour

        private bool PassesFilter(object candidate)
        {
            if (!(candidate is SheetItemViewModel item)) return false;

            if (!string.Equals(SelectedSheetSet, AllSheetsLabel, StringComparison.Ordinal))
            {
                SheetSetSnapshot set = _sheetSets.FirstOrDefault(s =>
                    string.Equals(s.Name, SelectedSheetSet, StringComparison.Ordinal));

                if (set == null || !set.SheetIds.Contains(item.Sheet.Id)) return false;
            }

            string term = SearchText?.Trim();
            return string.IsNullOrEmpty(term) || item.Matches(term);
        }

        private void OnSheetItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SheetItemViewModel.IsSelected)) return;

            Raise(nameof(SelectedCount), nameof(SelectionSummary), nameof(CanExport));
            ExportCommand.RaiseCanExecuteChanged();
        }

        private void SetVisibleSelection(bool selected)
        {
            foreach (SheetItemViewModel item in SheetsView.Cast<SheetItemViewModel>().ToList())
                item.IsSelected = selected;
        }

        private void InvertVisibleSelection()
        {
            foreach (SheetItemViewModel item in SheetsView.Cast<SheetItemViewModel>().ToList())
                item.IsSelected = !item.IsSelected;
        }

        private void SelectVisiblePending()
        {
            foreach (SheetItemViewModel item in SheetsView.Cast<SheetItemViewModel>().ToList())
                item.IsSelected = !item.WasExported;
        }

        private void BrowseFolder()
        {
            string picked = _dialogs.PickFolder(
                "Selecciona la carpeta donde se exportarán las láminas",
                string.IsNullOrWhiteSpace(OutputFolder) ? _project.LocalFolder : OutputFolder);

            if (picked == null) return;

            if (!ExportPathHelper.TryEnsureWritable(picked, out string pathError))
            {
                _dialogs.ShowError(DialogTitle, pathError);
                return;
            }

            OutputFolder = picked;
            Raise(nameof(CanExport));
            ExportCommand.RaiseCanExecuteChanged();
            SavePreferences();
        }

        private void StartExport()
        {
            List<SheetItemViewModel> selectedItems = Sheets.Where(item => item.IsSelected).ToList();
            List<SheetSnapshot> selected = selectedItems.Select(item => item.Sheet).ToList();

            if (selected.Count == 0) return;

            _itemsBySheet = selectedItems.ToDictionary(item => item.Sheet);

            if (!TryValidateLicenseQuota(selected.Count, out string licenseError))
            {
                _dialogs.ShowError(DialogTitle, licenseError);
                return;
            }

            if (!TryValidateOutputPaths(out string pathError))
            {
                _dialogs.ShowError(DialogTitle, pathError);
                return;
            }

            SavePreferences();
            Results.Clear();
            ShowResults = false;
            LastRunHadFailures = false;
            ProgressValue = 0;
            StatusText = "Preparando exportación...";
            IsExporting = true;

            _isMultiFormat = _selectedFormatMode == FormatMode.PdfAndDwg;
            _multiFormatPhase = 0;
            _progressOffset = 0;
            _firstPhaseResult = null;
            _pendingSheets = selected;
            _totalSteps = _isMultiFormat ? selected.Count * 2 : selected.Count;
            ProgressMaximum = _totalSteps;

            ExportFormat firstFormat = _selectedFormatMode == FormatMode.Dwg
                ? ExportFormat.Dwg
                : ExportFormat.Pdf;

            LaunchFormat(firstFormat, selected);
        }

        private bool TryValidateOutputPaths(out string error)
        {
            error = null;

            if (!ExportPathHelper.TryEnsureWritable(OutputFolder, out error))
                return false;

            if (_selectedFormatMode == FormatMode.PdfAndDwg)
            {
                if (!ExportPathHelper.TryEnsureWritable(GetDestinationFolder(ExportFormat.Pdf), out error))
                    return false;

                if (!ExportPathHelper.TryEnsureWritable(GetDestinationFolder(ExportFormat.Dwg), out error))
                    return false;
            }
            else
            {
                ExportFormat format = _selectedFormatMode == FormatMode.Dwg ? ExportFormat.Dwg : ExportFormat.Pdf;
                if (!ExportPathHelper.TryEnsureWritable(GetDestinationFolder(format), out error))
                    return false;
            }

            return true;
        }

        private bool TryValidateLicenseQuota(int selectedCount, out string error)
        {
            error = null;
            LicenseRuntime.EnsureInitialized();
            var entitlements = LicenseRuntime.Entitlements;

            if (!entitlements.CanUse(FeatureCodes.ToolBatchExport))
            {
                error = "No hay una licencia válida. Abre Cuenta / Licencia y activa tu clave.";
                return false;
            }

            if (!IsFormatEntitled(_selectedFormatMode, entitlements))
            {
                error = "Tu plan no incluye el formato seleccionado. Revisa Cuenta / Licencia o elige otro formato.";
                return false;
            }

            int batchLimit = entitlements.Remaining(FeatureCodes.LimitSheetsPerBatch);
            // Remaining() sobre un feature no-quota: el valor del plan es el tope (no remanente).
            // Si no existe, Remaining devuelve 0 — tratar 0 sin feature como “sin tope” no aplica;
            // limit.* siempre es un entero del plan. Si es 0, bloquear todo.
            if (batchLimit > 0 && selectedCount > batchLimit)
            {
                error = $"Tu plan permite como máximo {batchLimit} lámina(s) por lote. Seleccionaste {selectedCount}.";
                return false;
            }

            int unitsPerSheet = _selectedFormatMode == FormatMode.PdfAndDwg ? 2 : 1;
            int needed = selectedCount * unitsPerSheet;
            int remaining = entitlements.Remaining(FeatureCodes.QuotaSheetsPerMonth);
            if (remaining != -1 && needed > remaining)
            {
                error = remaining <= 0
                    ? "Se agotó la cuota de láminas de este mes."
                    : $"Te quedan {remaining} unidad(es) este mes y este lote necesita {needed} (PDF+DWG cuenta doble).";
                return false;
            }

            return true;
        }

        private static bool IsFormatEntitled(FormatMode mode, IEntitlementService entitlements)
        {
            switch (mode)
            {
                case FormatMode.Pdf:
                    return entitlements.CanUse(FeatureCodes.FormatPdf);
                case FormatMode.Dwg:
                    return entitlements.CanUse(FeatureCodes.FormatDwg);
                case FormatMode.PdfAndDwg:
                    return entitlements.CanUse(FeatureCodes.FormatPdfDwg)
                        || (entitlements.CanUse(FeatureCodes.FormatPdf) && entitlements.CanUse(FeatureCodes.FormatDwg));
                default:
                    return false;
            }
        }

        private void LaunchFormat(ExportFormat format, List<SheetSnapshot> sheets)
        {
            _currentExportFormat = format;
            _itemsCompletedInPhase = 0;

            var request = new ExportRequest(
                _uiDocument,
                GetDestinationFolder(format),
                NamingPattern,
                BuildFormatSettings(format),
                _project,
                _log);

            try
            {
                IExportEngine engine = _engines.Resolve(format);
                var job = new BatchExportJob(engine, request, sheets, OnProgress, OnItemCompleted, OnFinished);
                _scheduler.Start(job);
            }
            catch (Exception ex)
            {
                if (_isMultiFormat && _multiFormatPhase == 0)
                {
                    _log.Error($"No se pudo iniciar la exportación {ExportFormatInfo.Label(format)}.", ex);
                    _multiFormatPhase = 1;
                    _progressOffset = sheets.Count;
                    StatusText = $"{ExportFormatInfo.Label(format)}: {ex.Message} — Continuando con DWG...";
                    LaunchFormat(ExportFormat.Dwg, sheets);
                    return;
                }

                IsExporting = false;
                StatusText = "La exportación no pudo iniciarse.";
                _log.Error("No se pudo iniciar la exportación.", ex);
                _dialogs.ShowError(DialogTitle, ex.Message);
            }
        }

        private void RequestCancel()
        {
            _scheduler.RequestCancel();
            StatusText = "Cancelando al terminar la lámina actual...";
        }

        private IExportFormatSettings BuildFormatSettings(ExportFormat format)
        {
            if (format == ExportFormat.Dwg)
            {
                return new DwgExportSettings
                {
                    FileVersion = DwgFileVersion,
                    MergeViews = DwgMergeViews,
                    UseSharedCoordinates = DwgSharedCoordinates,
                    AlsoExportImage = DwgAlsoExportImage,
                    HideHelperGraphics = true
                };
            }

            return new PdfExportSettings
            {
                PrinterName = SelectedPrinter ?? string.Empty,
                MatchSheetSize = PdfMatchSheetSize,
                FitToPage = PdfFitToPage,
                NoMargin = PdfNoMargin,
                ColorMode = PdfColorMode,
                RasterQuality = PdfRasterQuality,
                HideHelperGraphics = true
            };
        }

        private void OnProgress(BatchProgress progress)
        {
            ProgressValue = _progressOffset + progress.Completed;
            ProgressMaximum = _totalSteps;

            string formatPrefix = _isMultiFormat
                ? ExportFormatInfo.Label(_currentExportFormat) + " "
                : "";

            if (progress.Completed >= progress.Total)
            {
                StatusText = _isMultiFormat && _multiFormatPhase == 0
                    ? "Pasando a DWG..."
                    : "Finalizando...";
            }
            else
            {
                StatusText = $"Exportando {formatPrefix}{progress.Completed + 1} de {progress.Total}: {progress.CurrentLabel}";
            }
        }

        private void OnItemCompleted(BatchItemResult result)
        {
            string formatTag = _isMultiFormat ? ExportFormatInfo.Label(_currentExportFormat) : null;
            Results.Add(new ExportResultViewModel(result, formatTag));

            SheetSnapshot sheet = _itemsCompletedInPhase < _pendingSheets.Count
                ? _pendingSheets[_itemsCompletedInPhase]
                : null;
            _itemsCompletedInPhase++;

            if (result.Succeeded)
            {
                if (sheet != null) RecordSheetExported(sheet);
                RecordSuccessfulSheetUsage();
                return;
            }

            LastRunHadFailures = true;
            ShowResults = true;
        }

        /// <summary>
        /// Updates both the in-memory history (persisted once at the end of the run, not per sheet,
        /// to avoid a disk write per item in a large batch) and the live grid row, so "Pendientes"
        /// reflects sheets exported earlier in the very same run.
        /// </summary>
        private void RecordSheetExported(SheetSnapshot sheet)
        {
            if (string.IsNullOrEmpty(sheet.UniqueId)) return;

            DateTime now = DateTime.UtcNow;
            _exportHistory[sheet.UniqueId] = now;

            if (_itemsBySheet != null && _itemsBySheet.TryGetValue(sheet, out SheetItemViewModel item))
                item.LastExportedUtc = now;
        }

        private void RecordSuccessfulSheetUsage()
        {
            try
            {
                LicenseRuntime.EnsureInitialized();
                if (!LicenseRuntime.Entitlements.TryConsume(FeatureCodes.QuotaSheetsPerMonth, 1))
                {
                    _log.Warn("No se pudo descontar cuota local tras una lámina exitosa (remaining insuficiente).");
                    return;
                }

                // Reconciliar con el servidor en background; fallos de red quedan en la cola.
                System.Threading.Tasks.Task.Run(() => LicenseRuntime.Client.FlushUsageQueueAsync(default));
            }
            catch (Exception ex)
            {
                _log.Warn("Error al registrar uso de licencia: " + ex.Message);
            }
        }

        private void OnFinished(BatchResult result)
        {
            if (_isMultiFormat && _multiFormatPhase == 0)
            {
                _firstPhaseResult = result;

                if (result.WasCancelled)
                {
                    FinishExport(result);
                    return;
                }

                _multiFormatPhase = 1;
                _progressOffset = _pendingSheets.Count;

                if (result.HasSetupError)
                    _log.Warn("La exportación PDF no pudo iniciarse: " + result.SetupError);

                try
                {
                    LaunchFormat(ExportFormat.Dwg, _pendingSheets);
                }
                catch (Exception ex)
                {
                    _log.Error("No se pudo iniciar la exportación DWG.", ex);
                    FinishExport(result);
                }

                return;
            }

            FinishExport(result);
        }

        private void FinishExport(BatchResult lastResult)
        {
            IsExporting = false;
            _historyStore.Save(_project.ProjectKey, _exportHistory);

            int totalSucceeded = lastResult.SucceededCount;
            int totalFailed = lastResult.FailedCount;
            bool wasCancelled = lastResult.WasCancelled;
            TimeSpan elapsed = lastResult.Elapsed;

            if (_firstPhaseResult != null)
            {
                totalSucceeded += _firstPhaseResult.SucceededCount;
                totalFailed += _firstPhaseResult.FailedCount;
                wasCancelled = wasCancelled || _firstPhaseResult.WasCancelled;
                elapsed = elapsed + _firstPhaseResult.Elapsed;

                if (_firstPhaseResult.HasSetupError && lastResult.HasSetupError)
                {
                    StatusText = "No se exportó ninguna lámina.";
                    _dialogs.ShowError(DialogTitle,
                        $"PDF: {_firstPhaseResult.SetupError}\nDWG: {lastResult.SetupError}");
                    return;
                }
            }
            else if (lastResult.HasSetupError)
            {
                StatusText = "No se exportó ninguna lámina.";
                _dialogs.ShowError(DialogTitle, lastResult.SetupError);
                return;
            }

            string verb = wasCancelled ? "Cancelado" : "Listo";
            string folderText = _firstPhaseResult != null
                ? $"{GetDestinationFolder(ExportFormat.Pdf)} y {GetDestinationFolder(ExportFormat.Dwg)}"
                : lastResult.DestinationFolder;
            string status = $"{verb}: {totalSucceeded} exportada(s), {totalFailed} con error " +
                            $"({elapsed.TotalSeconds:0.0} s). Carpeta: {folderText}";

            if (_firstPhaseResult != null)
            {
                if (_firstPhaseResult.HasSetupError)
                    status += $" (PDF no se pudo iniciar: {_firstPhaseResult.SetupError})";
                else if (lastResult.HasSetupError)
                    status += $" (DWG no se pudo iniciar: {lastResult.SetupError})";
            }

            StatusText = status;

            if (totalFailed > 0)
                ShowResults = true;

            if (totalSucceeded > 0 && OpenFolderWhenDone)
                _dialogs.Reveal(RevealTarget);
        }

        private void RefreshCommands()
        {
            Raise(nameof(CanExport));
            ExportCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }

        // ---------------------------------------------------------------- preferences

        private void ApplyPreferences(BatchExportPreferences preferences)
        {
            _outputFolder = ExportPathHelper.ResolveWritableFolder(
                preferences.OutputFolder,
                _project.LocalFolder,
                ExportPathHelper.DefaultExportRoot);

            _namingPattern = string.IsNullOrWhiteSpace(preferences.NamingPattern)
                ? NamingTokens.DefaultPattern
                : preferences.NamingPattern;

            _selectedFormatMode = preferences.Format;
            _openFolderWhenDone = preferences.OpenFolderWhenDone;

            _pdfMatchSheetSize = preferences.PdfMatchSheetSize;
            _pdfFitToPage = preferences.PdfFitToPage;
            _pdfNoMargin = preferences.PdfNoMargin;
            _pdfColorMode = preferences.PdfColorMode;
            _pdfRasterQuality = preferences.PdfRasterQuality;

            _dwgFileVersion = preferences.DwgFileVersion;
            _dwgMergeViews = preferences.DwgMergeViews;
            _dwgSharedCoordinates = preferences.DwgSharedCoordinates;
            _dwgAlsoExportImage = preferences.DwgAlsoExportImage;

            _selectedPrinter = PdfPrinterOptions.FindByNameContains(RequiredPdfPrinterHint);
        }

        private void SavePreferences()
        {
            _preferences.OutputFolder = OutputFolder;
            _preferences.NamingPattern = NamingPattern;
            _preferences.Format = SelectedFormatMode;
            _preferences.OpenFolderWhenDone = OpenFolderWhenDone;
            _preferences.PdfMatchSheetSize = PdfMatchSheetSize;
            _preferences.PdfFitToPage = PdfFitToPage;
            _preferences.PdfNoMargin = PdfNoMargin;
            _preferences.PdfColorMode = PdfColorMode;
            _preferences.PdfRasterQuality = PdfRasterQuality;
            _preferences.DwgFileVersion = DwgFileVersion;
            _preferences.DwgMergeViews = DwgMergeViews;
            _preferences.DwgSharedCoordinates = DwgSharedCoordinates;
            _preferences.DwgAlsoExportImage = DwgAlsoExportImage;

            _settingsStore.Save(BatchExportPreferences.StorageKey, _preferences);
        }

        public void Dispose()
        {
            foreach (SheetItemViewModel item in Sheets)
                item.PropertyChanged -= OnSheetItemChanged;

            SavePreferences();
        }
    }
}
