using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;
using GvrTools.Core.History;
using GvrTools.Core.IO;
using GvrTools.Core.Naming;
using GvrTools.Core.Selections;
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
        private readonly ISavedSelectionStore _savedSelectionStore;
        private readonly ILog _log;
        private readonly ExportEngineCatalog _engines;
        private readonly ProjectSnapshot _project;
        private readonly IReadOnlyList<SheetSetSnapshot> _sheetSets;
        private readonly BatchExportPreferences _preferences;
        private readonly Dictionary<string, DateTime> _exportHistory;

        /// <summary>
        /// Carpetas reales del lote en curso (con sufijo "(n)" si hacía falta). null fuera de un export.
        /// </summary>
        private Dictionary<ExportFormat, string> _runDestinationFolders;

        private ExportItemKind _itemKind = ExportItemKind.Sheet;
        private List<SavedSelection> _savedSelections;

        public BatchExportViewModel(
            UIDocument uiDocument,
            RevitJobScheduler scheduler,
            IUserDialogs dialogs,
            ISettingsStore settingsStore,
            ISheetExportHistoryStore historyStore,
            ILog log,
            ISavedSelectionStore savedSelectionStore = null)
        {
            _uiDocument = uiDocument;
            _scheduler = scheduler;
            _dialogs = dialogs;
            _settingsStore = settingsStore;
            _historyStore = historyStore ?? new SheetExportHistoryStore();
            _savedSelectionStore = savedSelectionStore ?? new SavedSelectionStore();
            _log = log ?? NullLog.Instance;
            _engines = ExportEngineCatalog.CreateDefault();

            _project = ProjectSnapshot.Read(uiDocument.Document);
            _sheetSets = SheetRepository.GetSheetSets(uiDocument.Document);

            // Dictionary<,>(IReadOnlyDictionary<,>) is not available on net48 (Revit 2021-2024) --
            // copy explicitly instead so this builds the same way on every target framework.
            _exportHistory = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, DateTime> entry in _historyStore.Load(_project.ProjectKey))
                _exportHistory[entry.Key] = entry.Value;

            _savedSelections = _savedSelectionStore.Load(_project.ProjectKey).ToList();

            LoadItems(ExportItemKind.Sheet);
            RefreshSavedSelectionNames();

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
            FormatOptions = BuildFormatOptions();
            EnsureSelectedFormatAllowed();
            SyncFormatOptionSelection();
            BlockedFormatLabels = BuildBlockedFormatLabels();

            // Arranca siempre en "(Personalizada)": no cambiar el comportamiento por defecto de la
            // tool solo porque el proyecto tenga una configuración DWG marcada como predeterminada
            // en Revit (esa marca puede existir por otro motivo, para exportar una sola lámina a
            // mano). Cargarla es una elección explícita del usuario, no un default nuevo.
            var savedDwgSetups = DwgExportSetupCatalog.ListNames(uiDocument.Document);
            DwgSavedSetupChoices = new[] { CustomDwgOptionsLabel }.Concat(savedDwgSetups).ToList();
            _dwgSavedSetupName = CustomDwgOptionsLabel;

            string activeDwgSetup = DwgExportSetupCatalog.TryGetActiveName(uiDocument.Document);
            DwgActiveSetupHint = !string.IsNullOrEmpty(activeDwgSetup) && savedDwgSetups.Contains(activeDwgSetup)
                ? $"Tu proyecto marca \"{activeDwgSetup}\" como configuración DWG predeterminada."
                : null;

            SelectAllCommand = new RelayCommand(() => SetVisibleSelection(true));
            SelectNoneCommand = new RelayCommand(() => SetVisibleSelection(false));
            InvertSelectionCommand = new RelayCommand(InvertVisibleSelection);
            SelectPendingCommand = new RelayCommand(SelectVisiblePending);
            SaveSelectionCommand = new RelayCommand(SaveCurrentSelectionAsFilter);
            DeleteSavedSelectionCommand = new RelayCommand(DeleteSelectedSavedSelection, () => IsRealSavedSelectionActive);
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            OpenFolderCommand = new RelayCommand(() => _dialogs.Reveal(RevealTarget));
            ExportCommand = new RelayCommand(StartExport, () => CanExport);
            CancelCommand = new RelayCommand(RequestCancel, () => IsExporting);
            GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);
            GoNextCommand = new RelayCommand(GoNext, () => CanGoNext);
            GoToStepCommand = new RelayCommand(GoToStep, CanGoToStep);
            SelectFormatCommand = new RelayCommand(SelectFormatOption);
            ChangePlanCommand = new RelayCommand(() => RequestChangePlan?.Invoke());

            StatusText = Sheets.Count == 0
                ? $"El proyecto activo no tiene {ItemNounPlural} para exportar."
                : $"{Sheets.Count} {ItemNounPlural} en el proyecto.";

            RefreshIdleProgressScale();
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
        public bool HasSheetSets => _itemKind == ExportItemKind.Sheet && _sheetSets.Count > 0;

        // ---------------------------------------------------------------- sheets / views toggle

        /// <summary>Grid shows sheets when true, standalone views when <see cref="IsViewsMode"/> is true.</summary>
        public bool IsSheetsMode
        {
            get => _itemKind == ExportItemKind.Sheet;
            set { if (value) SetItemKind(ExportItemKind.Sheet); }
        }

        public bool IsViewsMode
        {
            get => _itemKind == ExportItemKind.View;
            set { if (value) SetItemKind(ExportItemKind.View); }
        }

        /// <summary>"lámina(s)"/"vista(s)", for the couple of status labels that name the item kind.</summary>
        private string ItemNounPlural => _itemKind == ExportItemKind.View ? "vista(s)" : "lámina(s)";

        private string ItemNounSingular => _itemKind == ExportItemKind.View ? "vista" : "lámina";

        /// <summary>
        /// Switches what the grid lists. Disabled once a run has started (bound to
        /// <c>!IsExporting</c> in the window) since reloading the grid mid-batch would desync it from
        /// the job actually running; the guard here is the same rule enforced defensively in code.
        /// </summary>
        private void SetItemKind(ExportItemKind kind)
        {
            if (_itemKind == kind || IsExporting) return;

            string previousDefault = _itemKind == ExportItemKind.View ? NamingTokens.DefaultViewPattern : NamingTokens.DefaultPattern;
            _itemKind = kind;

            // Only swap the pattern away from a default the user never touched -- a custom pattern
            // is never overwritten just because the mode changed.
            if (string.Equals(NamingPattern, previousDefault, StringComparison.Ordinal))
                NamingPattern = kind == ExportItemKind.View ? NamingTokens.DefaultViewPattern : NamingTokens.DefaultPattern;

            // Un filtro guardado es un concepto por modo (Láminas o Vistas): nunca debe seguir
            // activo al cruzar al otro modo, ni siquiera cuando existe un filtro con el mismo
            // nombre del otro lado -- limpiarlo ANTES del Refresh de abajo, porque ese Refresh ya
            // consulta _activeSavedFilterIds y ese conjunto es de UniqueIds del modo anterior (no
            // coincidiría con ninguna fila nueva, dejando la grilla vacía por error).
            _activeSavedFilterIds = null;
            Set(ref _selectedSavedSelectionName, null, nameof(SelectedSavedSelectionName));
            Raise(nameof(IsRealSavedSelectionActive));

            LoadItems(kind);
            SelectedSheetSet = AllSheetsLabel;
            SheetsView.Refresh();

            StatusText = Sheets.Count == 0
                ? $"El proyecto activo no tiene {ItemNounPlural} para exportar."
                : $"{Sheets.Count} {ItemNounPlural} en el proyecto.";

            Raise(nameof(IsSheetsMode), nameof(IsViewsMode), nameof(HasSheetSets));
            NotifySelectionChanged();
            Raise(nameof(NamingPreview));
            RefreshIdleProgressScale();
            RefreshSavedSelectionNames();
        }

        /// <summary>
        /// (Re)populates <see cref="Sheets"/> from the repository for the given kind, wiring history
        /// and change notification exactly as the constructor did for the sheet-only original list.
        /// </summary>
        private void LoadItems(ExportItemKind kind)
        {
            foreach (SheetItemViewModel existing in Sheets)
                existing.PropertyChanged -= OnSheetItemChanged;
            Sheets.Clear();

            IReadOnlyList<SheetSnapshot> items = kind == ExportItemKind.View
                ? SheetRepository.GetViews(_uiDocument.Document)
                : SheetRepository.GetSheets(_uiDocument.Document);

            foreach (SheetSnapshot snapshot in items)
            {
                var item = new SheetItemViewModel(snapshot);
                if (!string.IsNullOrEmpty(snapshot.UniqueId) && _exportHistory.TryGetValue(snapshot.UniqueId, out DateTime lastExported))
                    item.LastExportedUtc = lastExported;

                item.PropertyChanged += OnSheetItemChanged;
                Sheets.Add(item);
            }
        }

        // ---------------------------------------------------------------- filtros guardados (checklist)

        /// <summary>"Sheet"/"View", usado como etiqueta al guardar/leer para que un filtro guardado
        /// en modo Vistas no aparezca (ni se pueda aplicar por error) en modo Láminas.</summary>
        private string CurrentKindTag => _itemKind == ExportItemKind.View ? "View" : "Sheet";

        /// <summary>Primera entrada del combo: quita el filtro activo y vuelve a mostrar todas las filas.</summary>
        public const string AllSavedSelectionsLabel = "(Todos)";

        /// <summary>Nombres de los filtros guardados para el modo actual (Láminas o Vistas), ordenados, con <see cref="AllSavedSelectionsLabel"/> primero.</summary>
        public ObservableCollection<string> SavedSelectionNames { get; } = new ObservableCollection<string>();

        /// <summary>true si hay al menos un filtro guardado de verdad (sin contar el "(Todos)" siempre presente).</summary>
        public bool HasSavedSelections => SavedSelectionNames.Count > 1;

        /// <summary>true cuando el filtro elegido es uno real (no "(Todos)") -- gatilla el botón Eliminar y el filtrado de filas.</summary>
        public bool IsRealSavedSelectionActive =>
            !string.IsNullOrEmpty(_selectedSavedSelectionName) && !string.Equals(_selectedSavedSelectionName, AllSavedSelectionsLabel, StringComparison.Ordinal);

        private string _selectedSavedSelectionName;

        /// <summary>
        /// UniqueIds del filtro actualmente aplicado, o null cuando no hay ninguno activo ("(Todos)").
        /// PassesFilter lo consulta para ocultar (no solo desmarcar) las filas que no pertenecen al
        /// filtro -- así "tener un filtro" realmente muestra solo eso, como pidió el cliente,
        /// comparándolo con cómo ProSheets filtra su grilla por View/Sheet Set.
        /// </summary>
        private HashSet<string> _activeSavedFilterIds;

        /// <summary>
        /// Elegir un filtro real en el combo lo aplica de inmediato: oculta el resto de filas y marca
        /// exactamente esas láminas/vistas -- así lo describió el cliente: "abro el filtro, lo marco y
        /// automáticamente se marcan los que ya preseleccioné". Elegir "(Todos)" quita el filtro y
        /// vuelve a mostrar todas las filas, sin tocar lo que ya estaba marcado.
        /// </summary>
        public string SelectedSavedSelectionName
        {
            get => _selectedSavedSelectionName;
            set
            {
                if (!Set(ref _selectedSavedSelectionName, value)) return;

                if (string.IsNullOrEmpty(value) || string.Equals(value, AllSavedSelectionsLabel, StringComparison.Ordinal))
                    _activeSavedFilterIds = null;
                else
                    ApplySavedSelection(value);

                SheetsView.Refresh();
                Raise(nameof(IsRealSavedSelectionActive), nameof(AreAllVisibleSelected));
                DeleteSavedSelectionCommand.RaiseCanExecuteChanged();
            }
        }

        private void SaveCurrentSelectionAsFilter()
        {
            string name = _dialogs.PromptText(
                "Guardar filtro",
                $"Nombre para este filtro de {ItemNounPlural} marcadas:",
                _selectedSavedSelectionName ?? string.Empty);

            if (name == null) return; // el usuario canceló el diálogo -- no es lo mismo que un nombre vacío.

            // Se valida/dedupea contra el mismo nombre saneado que terminará en disco (ver
            // SavedSelectionStore.SanitizeName) -- así un nombre que se reduce a nada (ej. "###")
            // se rechaza aquí en vez de guardarse en memoria y desaparecer en silencio al persistir,
            // y dos nombres que solo difieren en caracteres saneados no chocan en el archivo.
            name = SavedSelectionStore.SanitizeName(name);
            if (name.Length == 0)
            {
                _dialogs.ShowWarning("Guardar filtro", "Escribe un nombre para el filtro (no puede quedar vacío).");
                return;
            }

            List<string> uniqueIds = Sheets
                .Where(item => item.IsSelected && !string.IsNullOrEmpty(item.Sheet.UniqueId))
                .Select(item => item.Sheet.UniqueId)
                .ToList();

            if (uniqueIds.Count == 0)
            {
                _dialogs.ShowWarning("Guardar filtro", $"No hay ninguna {ItemNounSingular} marcada para guardar.");
                return;
            }

            string kind = CurrentKindTag;
            _savedSelections.RemoveAll(s => s.Kind == kind && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            _savedSelections.Add(new SavedSelection(name, kind, uniqueIds));
            _savedSelectionStore.Save(_project.ProjectKey, _savedSelections);

            RefreshSavedSelectionNames();
            Set(ref _selectedSavedSelectionName, name, nameof(SelectedSavedSelectionName));
            _activeSavedFilterIds = new HashSet<string>(uniqueIds, StringComparer.Ordinal);
            SheetsView.Refresh();
            Raise(nameof(IsRealSavedSelectionActive), nameof(AreAllVisibleSelected));
            DeleteSavedSelectionCommand.RaiseCanExecuteChanged();

            StatusText = $"Filtro \"{name}\" guardado ({uniqueIds.Count} {ItemNounPlural}).";
        }

        private void DeleteSelectedSavedSelection()
        {
            string name = SelectedSavedSelectionName;
            if (string.IsNullOrEmpty(name) || string.Equals(name, AllSavedSelectionsLabel, StringComparison.Ordinal)) return;
            if (!_dialogs.Confirm("Eliminar filtro", $"¿Eliminar el filtro \"{name}\"?")) return;

            string kind = CurrentKindTag;
            _savedSelections.RemoveAll(s => s.Kind == kind && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            _savedSelectionStore.Save(_project.ProjectKey, _savedSelections);

            _activeSavedFilterIds = null;
            Set(ref _selectedSavedSelectionName, AllSavedSelectionsLabel, nameof(SelectedSavedSelectionName));
            RefreshSavedSelectionNames();
            SheetsView.Refresh();
            Raise(nameof(IsRealSavedSelectionActive), nameof(AreAllVisibleSelected));
            DeleteSavedSelectionCommand.RaiseCanExecuteChanged();

            StatusText = $"Filtro \"{name}\" eliminado.";
        }

        /// <summary>
        /// Marca exactamente las láminas/vistas del filtro y las deja como el único conjunto visible
        /// -- el filtrado de filas (ocultar el resto) lo aplica PassesFilter vía _activeSavedFilterIds,
        /// que el caller (el setter de SelectedSavedSelectionName) refresca después de llamar esto.
        /// </summary>
        private void ApplySavedSelection(string name)
        {
            string kind = CurrentKindTag;
            SavedSelection match = _savedSelections.FirstOrDefault(
                s => s.Kind == kind && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                _activeSavedFilterIds = null;
                return;
            }

            var ids = new HashSet<string>(match.UniqueIds, StringComparer.Ordinal);
            _activeSavedFilterIds = ids;

            _suppressSelectionNotifications = true;
            try
            {
                foreach (SheetItemViewModel item in Sheets)
                    item.IsSelected = !string.IsNullOrEmpty(item.Sheet.UniqueId) && ids.Contains(item.Sheet.UniqueId);
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            NotifySelectionChanged();
            StatusText = $"Filtro \"{name}\" aplicado ({SelectedCount} de {Sheets.Count} {ItemNounPlural}).";
        }

        /// <summary>Refresca el combo de filtros para el modo actual (Láminas/Vistas); llamado al abrir la ventana y al cambiar de modo.</summary>
        private void RefreshSavedSelectionNames()
        {
            string kind = CurrentKindTag;
            List<string> names = _savedSelections
                .Where(s => s.Kind == kind)
                .Select(s => s.Name)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            SavedSelectionNames.Clear();
            SavedSelectionNames.Add(AllSavedSelectionsLabel);
            foreach (string name in names) SavedSelectionNames.Add(name);

            // La selección elegida ya no existe en la lista (se borró, o cambiamos de modo y no hay
            // un filtro con ese nombre del otro lado): volver a "(Todos)" -- nunca a null, el combo
            // siempre debe tener algo elegido -- y quitar el filtrado de filas sin tocar lo marcado.
            bool stillValid = !string.IsNullOrEmpty(_selectedSavedSelectionName) && SavedSelectionNames.Contains(_selectedSavedSelectionName);
            if (!stillValid)
            {
                _activeSavedFilterIds = null;
                Set(ref _selectedSavedSelectionName, AllSavedSelectionsLabel, nameof(SelectedSavedSelectionName));
            }

            Raise(nameof(HasSavedSelections), nameof(IsRealSavedSelectionActive));
            DeleteSavedSelectionCommand?.RaiseCanExecuteChanged();
        }

        public ObservableCollection<ExportResultViewModel> Results { get; } = new ObservableCollection<ExportResultViewModel>();

        public string DocumentTitle => _project.Title;

        /// <summary>Documento de Revit que esta ventana está exportando -- expuesto para que BatchExportCommand pueda detectar si el usuario cambió de documento activo antes de reactivar una ventana ya abierta.</summary>
        public Document Document => _uiDocument.Document;

        /// <summary>Expuesto solo para que el code-behind reutilice el mismo IUserDialogs al abrir ExportSuccessWindow.</summary>
        public IUserDialogs Dialogs => _dialogs;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!Set(ref _searchText, value)) return;
                SheetsView.Refresh();
                Raise(nameof(AreAllVisibleSelected));
            }
        }

        private string _selectedSheetSet;
        public string SelectedSheetSet
        {
            get => _selectedSheetSet;
            set
            {
                if (!Set(ref _selectedSheetSet, value)) return;
                SheetsView.Refresh();
                Raise(nameof(AreAllVisibleSelected));
            }
        }

        public int SelectedCount => Sheets.Count(sheet => sheet.IsSelected);

        public string SelectionSummary => $"{SelectedCount} de {Sheets.Count} {ItemNounPlural} seleccionadas";

        /// <summary>
        /// Checkbox del encabezado del grid (estilo ProSheets). true = todas las visibles;
        /// false = ninguna; null = selección parcial (IsThreeState).
        /// </summary>
        public bool? AreAllVisibleSelected
        {
            get
            {
                var visible = SheetsView.Cast<SheetItemViewModel>().ToList();
                if (visible.Count == 0) return false;
                int selected = visible.Count(s => s.IsSelected);
                if (selected == 0) return false;
                if (selected == visible.Count) return true;
                return null;
            }
            set
            {
                // WPF IsThreeState cycles false → true → null → false. That does not match
                // "header checks all / unchecks all", especially: true→null is ignored by a
                // naive setter, and null→false clears a partial selection instead of selecting all.
                // Interpret any header write as: all visible selected → deselect all; else select all.
                _ = value;
                var visible = SheetsView.Cast<SheetItemViewModel>().ToList();
                bool allSelected = visible.Count > 0 && visible.All(s => s.IsSelected);
                SetVisibleSelection(!allSelected);
            }
        }
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

        /// <summary>Ruta base deseada (sin sufijo de colisión).</summary>
        private string GetDesiredDestinationFolder(ExportFormat format)
        {
            if (string.IsNullOrWhiteSpace(OutputFolder)) return string.Empty;

            string prefix = format == ExportFormat.Pdf ? "PDF_" : "DWG_";
            return Path.Combine(OutputFolder, prefix + PathSanitizer.SanitizeFolderName(_project.Title));
        }

        /// <summary>
        /// Carpeta que se usará / se está usando: en un lote activo la reservada al iniciar;
        /// en preview, la primera libre estilo Explorer (<c>PDF_Proyecto (1)</c> si ya existe).
        /// </summary>
        private string GetDestinationFolder(ExportFormat format)
        {
            if (_runDestinationFolders != null &&
                _runDestinationFolders.TryGetValue(format, out string runPath) &&
                !string.IsNullOrEmpty(runPath))
            {
                return runPath;
            }

            string desired = GetDesiredDestinationFolder(format);
            if (string.IsNullOrEmpty(desired)) return string.Empty;
            return ExportPathHelper.AllocateUniqueDirectoryPath(desired);
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
                Raise(nameof(NamingPreview), nameof(SelectedNamingPreset));
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
                if (sample == null) return string.IsNullOrWhiteSpace(NamingPattern) ? string.Empty : $"(sin {ItemNounPlural} para previsualizar)";

                var namer = new ExportFileNamer(string.Empty, NamingPattern, string.Empty, _project.ToTokens());
                return namer.Preview(sample);
            }
        }

        /// <summary>
        /// Selector de plantilla: refleja el patrón actual cuando coincide con una plantilla conocida.
        /// Elegir una plantilla rellena el cuadro de patrón (sigue editable a mano).
        /// </summary>
        public NamingPreset SelectedNamingPreset
        {
            get => NamingPresets.ResolveForPattern(_namingPattern);
            set
            {
                if (value != null)
                    NamingPattern = value.Pattern;
            }
        }

        // ---------------------------------------------------------------- format and options

        public IReadOnlyList<ChoiceItem<FormatMode>> FormatChoices { get; private set; }

        /// <summary>Todas las opciones de formato (incluidas las bloqueadas) para el paso 2 con candados.</summary>
        public IReadOnlyList<FormatOptionItem> FormatOptions { get; private set; }

        /// <summary>Formatos que existen en la tool pero no incluye el plan actual.</summary>
        public IReadOnlyList<string> BlockedFormatLabels { get; private set; }

        public bool HasBlockedFormats => BlockedFormatLabels.Count > 0;

        public string BlockedFormatsText => "No incluido en tu plan: " + string.Join(", ", BlockedFormatLabels);

        private static IReadOnlyList<string> BuildBlockedFormatLabels()
        {
            LicenseRuntime.EnsureInitialized();
            var entitlements = LicenseRuntime.Entitlements;
            var blocked = new List<string>();
            if (!entitlements.CanUse(FeatureCodes.FormatPdf)) blocked.Add("PDF");
            if (!entitlements.CanUse(FeatureCodes.FormatDwg)) blocked.Add("DWG");
            return blocked;
        }

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

        private static IReadOnlyList<FormatOptionItem> BuildFormatOptions()
        {
            LicenseRuntime.EnsureInitialized();
            var entitlements = LicenseRuntime.Entitlements;
            bool pdf = entitlements.CanUse(FeatureCodes.FormatPdf);
            bool dwg = entitlements.CanUse(FeatureCodes.FormatDwg);
            bool both = entitlements.CanUse(FeatureCodes.FormatPdfDwg) || (pdf && dwg);

            return new[]
            {
                new FormatOptionItem(FormatMode.Pdf, "PDF", !pdf),
                new FormatOptionItem(FormatMode.Dwg, "DWG", !dwg),
                new FormatOptionItem(FormatMode.PdfAndDwg, "PDF + DWG", !both)
            };
        }

        private void SyncFormatOptionSelection()
        {
            if (FormatOptions == null) return;
            foreach (FormatOptionItem option in FormatOptions)
                option.IsSelected = !option.IsLocked && Equals(option.Mode, _selectedFormatMode);
        }

        private void SelectFormatOption(object parameter)
        {
            var option = parameter as FormatOptionItem;
            if (option == null) return;
            if (option.IsLocked)
            {
                RequestChangePlan?.Invoke();
                return;
            }

            SelectedFormatMode = option.Mode;
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

                SyncFormatOptionSelection();
                Raise(nameof(ShowPdfOptions), nameof(ShowDwgOptions), nameof(ExportButtonLabel),
                      nameof(StrategyDescription), nameof(ShowPrinterSelector), nameof(IsPdfPrinterMissing),
                      nameof(CanExport), nameof(DestinationFolder), nameof(SelectedFormatLabel),
                      nameof(NamingPatternIsUsed), nameof(NamingPatternIsInert), nameof(NamingPatternInertHint),
                      nameof(CanEditNamingPattern));
                ExportCommand.RaiseCanExecuteChanged();
                RefreshIdleProgressScale();
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
            set
            {
                if (Set(ref _pdfFitToPage, value))
                    Raise(nameof(ShowPdfZoomPercentage));
            }
        }

        private int _pdfZoomPercentage = 100;

        /// <summary>Escala de ploteo cuando "Ajustar a la página" está desmarcado. 100 = escala real.</summary>
        public int PdfZoomPercentage
        {
            get => _pdfZoomPercentage;
            // Mismo rango que el cuadro de zoom del diálogo de impresión nativo de Revit (1-999):
            // sin este tope, un valor absurdo tipeado a mano (ej. 100000) llega tal cual a
            // PDFExportOptions.ZoomPercentage / PrintParameters.Zoom, que lo rechaza -- Revit lanza
            // una excepción al construir las opciones y CADA lámina del lote termina fallando con
            // un mensaje crudo de la API en vez de uno claro.
            set => Set(ref _pdfZoomPercentage, Math.Max(1, Math.Min(999, value)));
        }

        /// <summary>El cuadro de zoom % solo tiene sentido cuando no se está ajustando a la página.</summary>
        public bool ShowPdfZoomPercentage => !PdfFitToPage;

        // Nombradas distinto de sus enums a propósito: una propiedad con el mismo nombre que su tipo
        // vuelve ambiguo referenciar los valores del enum (PdfPaperPlacement.X, PdfCornerMarginMode.X)
        // en el resto de la clase.
        private PdfPaperPlacement _pdfPlacementMode = PdfPaperPlacement.Center;
        public PdfPaperPlacement PdfPlacementMode
        {
            get => _pdfPlacementMode;
            set
            {
                if (Set(ref _pdfPlacementMode, value))
                    Raise(nameof(IsPdfPaperPlacementCenter), nameof(IsPdfPaperPlacementOffsetFromCorner),
                          nameof(ShowPdfCornerMarginMode), nameof(ShowPdfOffsetFields));
            }
        }

        public bool IsPdfPaperPlacementCenter
        {
            get => _pdfPlacementMode == PdfPaperPlacement.Center;
            set { if (value) PdfPlacementMode = PdfPaperPlacement.Center; }
        }

        public bool IsPdfPaperPlacementOffsetFromCorner
        {
            get => _pdfPlacementMode == PdfPaperPlacement.OffsetFromCorner;
            set { if (value) PdfPlacementMode = PdfPaperPlacement.OffsetFromCorner; }
        }

        /// <summary>El sub-selector (Sin margen/Límite de impresora/Definido por el usuario) solo aplica bajo "Desde una esquina".</summary>
        public bool ShowPdfCornerMarginMode => IsPdfPaperPlacementOffsetFromCorner;

        private PdfCornerMarginMode _pdfCornerMarginMode = PdfCornerMarginMode.UserDefined;
        public PdfCornerMarginMode PdfCornerMarginMode
        {
            get => _pdfCornerMarginMode;
            set
            {
                if (Set(ref _pdfCornerMarginMode, value))
                    Raise(nameof(IsPdfCornerMarginNoMargin), nameof(IsPdfCornerMarginPrinterLimit),
                          nameof(IsPdfCornerMarginUserDefined), nameof(ShowPdfOffsetFields));
            }
        }

        public bool IsPdfCornerMarginNoMargin
        {
            get => _pdfCornerMarginMode == PdfCornerMarginMode.NoMargin;
            set { if (value) PdfCornerMarginMode = PdfCornerMarginMode.NoMargin; }
        }

        public bool IsPdfCornerMarginPrinterLimit
        {
            get => _pdfCornerMarginMode == PdfCornerMarginMode.PrinterLimit;
            set { if (value) PdfCornerMarginMode = PdfCornerMarginMode.PrinterLimit; }
        }

        public bool IsPdfCornerMarginUserDefined
        {
            get => _pdfCornerMarginMode == PdfCornerMarginMode.UserDefined;
            set { if (value) PdfCornerMarginMode = PdfCornerMarginMode.UserDefined; }
        }

        /// <summary>Los cuadros X/Y solo tienen sentido en "Desde una esquina" + "Definido por el usuario".</summary>
        public bool ShowPdfOffsetFields => IsPdfPaperPlacementOffsetFromCorner && IsPdfCornerMarginUserDefined;

        private double _pdfOffsetXInches;
        public double PdfOffsetXInches
        {
            get => _pdfOffsetXInches;
            set => Set(ref _pdfOffsetXInches, value);
        }

        private double _pdfOffsetYInches;
        public double PdfOffsetYInches
        {
            get => _pdfOffsetYInches;
            set => Set(ref _pdfOffsetYInches, value);
        }

        private bool _pdfHideCropBoundaries = true;
        public bool PdfHideCropBoundaries
        {
            get => _pdfHideCropBoundaries;
            set => Set(ref _pdfHideCropBoundaries, value);
        }

        private bool _pdfHideScopeBoxes = true;
        public bool PdfHideScopeBoxes
        {
            get => _pdfHideScopeBoxes;
            set => Set(ref _pdfHideScopeBoxes, value);
        }

        private bool _pdfHideUnreferencedViewTags = true;
        public bool PdfHideUnreferencedViewTags
        {
            get => _pdfHideUnreferencedViewTags;
            set => Set(ref _pdfHideUnreferencedViewTags, value);
        }

        private bool _pdfHideReferencePlanes = true;
        public bool PdfHideReferencePlanes
        {
            get => _pdfHideReferencePlanes;
            set => Set(ref _pdfHideReferencePlanes, value);
        }

        /// <summary>
        /// "Combinar en un solo archivo" en toda versión soportada: 2022+ usa
        /// <c>CombinedPdfExportJob</c> (API nativa <c>Document.Export</c>); 2021 usa
        /// <c>CombinedPrintDriverPdfExportJob</c> (una impresión por lámina vía PDF24 +
        /// <c>pdf24-DocTool -join</c>). No usar PrintRange.Select / ViewSheetSetting en 2021:
        /// falla de forma intermitente (Views vacías / SaveAs sin Transaction).
        /// </summary>
        public bool CanCombinePdf => true;

        private bool _pdfCombineIntoSingleFile;
        public bool PdfCombineIntoSingleFile
        {
            get => _pdfCombineIntoSingleFile && CanCombinePdf;
            set
            {
                if (Set(ref _pdfCombineIntoSingleFile, value && CanCombinePdf))
                {
                    Raise(nameof(ShowPdfCombinedFileName), nameof(NamingPatternIsUsed), nameof(NamingPatternIsInert),
                          nameof(NamingPatternInertHint), nameof(CanEditNamingPattern));

                    // StepCountFor (y por lo tanto el "X / Y" en reposo del paso Crear) depende de
                    // este valor -- combinado cuenta 1 paso en vez de N. Sin esto, togglear la casilla
                    // dejaba el preview de progreso mostrando el conteo de ANTES hasta que el usuario
                    // tocaba algo más (formato/selección) que sí disparaba RefreshIdleProgressScale.
                    RefreshIdleProgressScale();
                }
            }
        }

        public bool ShowPdfCombinedFileName => PdfCombineIntoSingleFile;

        /// <summary>
        /// El patrón de "Nomenclatura de archivos" solo nombra archivos por-lámina. En modo "Solo PDF" +
        /// "Combinar en un solo archivo" no se genera ningún archivo por-lámina -- el único PDF resultante
        /// toma su nombre de <see cref="PdfCombinedFileName"/> (pestaña Formato), así que ese patrón queda
        /// sin efecto y la tarjeta se atenúa para no sugerir que sigue controlando el resultado. Con DWG
        /// incluido (Solo DWG o PDF+DWG) el patrón sigue aplicando al lado DWG aunque el PDF se combine.
        /// </summary>
        public bool NamingPatternIsUsed => !(SelectedFormatMode == FormatMode.Pdf && PdfCombineIntoSingleFile);

        /// <summary>Inverso de <see cref="NamingPatternIsUsed"/> -- para el binding de Visibility del hint (no hay conversor "invertir" en este proyecto).</summary>
        public bool NamingPatternIsInert => !NamingPatternIsUsed;

        public string NamingPatternInertHint => NamingPatternIsUsed
            ? null
            : "No aplica: al combinar en un solo PDF, el nombre se define arriba en Formato → Archivo → \"Nombre del archivo\".";

        /// <summary>IsEnabled real de la tarjeta "Nomenclatura de archivos": ni exportando ni inerte por combinado.</summary>
        public bool CanEditNamingPattern => CanEditOptions && NamingPatternIsUsed;

        private string _pdfCombinedFileName = "{ProjectTitle}_combinado";
        public string PdfCombinedFileName
        {
            get => _pdfCombinedFileName;
            set => Set(ref _pdfCombinedFileName, value ?? string.Empty);
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

        private PdfHiddenLineProcessing _pdfHiddenLineProcessing = PdfHiddenLineProcessing.Vector;
        public PdfHiddenLineProcessing PdfHiddenLineProcessing
        {
            get => _pdfHiddenLineProcessing;
            set => Set(ref _pdfHiddenLineProcessing, value);
        }

        /// <summary>Atajo de UI para HiddenLineProcessing: una sola casilla en vez de un combo de dos opciones.</summary>
        public bool PdfProcessAsRaster
        {
            get => _pdfHiddenLineProcessing == PdfHiddenLineProcessing.Raster;
            set => PdfHiddenLineProcessing = value ? PdfHiddenLineProcessing.Raster : PdfHiddenLineProcessing.Vector;
        }

        private bool _pdfViewLinksInBlue;
        public bool PdfViewLinksInBlue
        {
            get => _pdfViewLinksInBlue;
            set => Set(ref _pdfViewLinksInBlue, value);
        }

        private bool _pdfReplaceHalftoneWithThinLines;
        public bool PdfReplaceHalftoneWithThinLines
        {
            get => _pdfReplaceHalftoneWithThinLines;
            set => Set(ref _pdfReplaceHalftoneWithThinLines, value);
        }

        private bool _pdfMaskCoincidentLines = true;
        public bool PdfMaskCoincidentLines
        {
            get => _pdfMaskCoincidentLines;
            set => Set(ref _pdfMaskCoincidentLines, value);
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

        /// <summary>Primera opción del combo de configuraciones DWG guardadas -- "usar mis propios controles de arriba".</summary>
        public const string CustomDwgOptionsLabel = "(Personalizada)";

        /// <summary>"(Personalizada)" + los nombres de configuraciones DWG que ya existan en el proyecto (Administrar → Configuraciones DWG).</summary>
        public IReadOnlyList<string> DwgSavedSetupChoices { get; }

        /// <summary>Aviso opcional: nombre de la configuración DWG que el proyecto ya marca como predeterminada en Revit, o null si no hay ninguna.</summary>
        public string DwgActiveSetupHint { get; private set; }

        public bool HasDwgActiveSetupHint => !string.IsNullOrEmpty(DwgActiveSetupHint);

        private string _dwgSavedSetupName;
        public string DwgSavedSetupName
        {
            get => _dwgSavedSetupName;
            set
            {
                if (!Set(ref _dwgSavedSetupName, value)) return;
                Raise(nameof(IsUsingCustomDwgOptions));
            }
        }

        /// <summary>Cuando es false, Versión/Combinar vistas/Coordenadas de arriba se ignoran: se usa la configuración DWG guardada elegida tal cual.</summary>
        public bool IsUsingCustomDwgOptions =>
            string.IsNullOrEmpty(DwgSavedSetupName) || string.Equals(DwgSavedSetupName, CustomDwgOptionsLabel, StringComparison.Ordinal);

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

                Raise(nameof(CanEditOptions), nameof(CanGoBack), nameof(CanGoNext), nameof(CanEditNamingPattern));
                RefreshCommands();
                GoBackCommand.RaiseCanExecuteChanged();
                GoNextCommand.RaiseCanExecuteChanged();
                GoToStepCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Options are locked while a run is in flight, so a job never changes shape mid-flight.</summary>
        public bool CanEditOptions => !IsExporting;

        private int _progressValue;
        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                if (Set(ref _progressValue, value))
                    Raise(nameof(ProgressPercentText), nameof(ProgressFractionText));
            }
        }

        private int _progressMaximum = 1;
        public int ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                if (Set(ref _progressMaximum, value))
                    Raise(nameof(ProgressPercentText), nameof(ProgressFractionText));
            }
        }

        /// <summary>"Completado 42%" — banner del paso Crear (siempre visible).</summary>
        public string ProgressPercentText
        {
            get
            {
                if (ProgressMaximum <= 0) return "Completado 0%";
                int pct = (int)Math.Round(100.0 * ProgressValue / ProgressMaximum);
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                return "Completado " + pct + "%";
            }
        }

        /// <summary>"3 / 7" pasos del lote actual.</summary>
        public string ProgressFractionText
        {
            get
            {
                int max = ProgressMaximum <= 0 ? 0 : ProgressMaximum;
                return ProgressValue + " / " + max;
            }
        }

        public string SelectedFormatLabel
        {
            get
            {
                if (_selectedFormatMode == FormatMode.PdfAndDwg) return "PDF + DWG";
                if (_selectedFormatMode == FormatMode.Dwg) return "DWG";
                return "PDF";
            }
        }

        /// <summary>CTA de upgrade en el banner de progreso cuando el plan es free.</summary>
        public bool ShowUpgradeHint
        {
            get
            {
                LicenseRuntime.EnsureInitialized();
                LicenseClient client = LicenseRuntime.Client;
                return client.IsLicensed
                    && string.Equals(client.PlanCode, "free", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string UpgradeHintText => "Para exportaciones ilimitadas, cambia de plan";

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

        /// <summary>Saves the currently checked rows as a named, reusable filter ("checklist por especialidad").</summary>
        public RelayCommand SaveSelectionCommand { get; }

        public RelayCommand DeleteSavedSelectionCommand { get; }

        public RelayCommand BrowseFolderCommand { get; }

        public RelayCommand OpenFolderCommand { get; }

        public RelayCommand ExportCommand { get; }

        public RelayCommand CancelCommand { get; }

        // ---------------------------------------------------------------- wizard steps (UI_FREEMIUM_PLAN.md §3.1)

        /// <summary>1 = Selección, 2 = Formato, 3 = Crear. Solo controla qué panel se ve; ningún
        /// binding ni comando existente cambia -- las tres secciones son las mismas de siempre.</summary>
        private int _wizardStep = 1;
        public int WizardStep
        {
            get => _wizardStep;
            private set
            {
                if (!Set(ref _wizardStep, value)) return;
                Raise(nameof(IsSelectionStep), nameof(IsFormatStep), nameof(IsCreateStep),
                      nameof(ShowNextButton), nameof(CanGoBack), nameof(CanGoNext));
                GoBackCommand.RaiseCanExecuteChanged();
                GoNextCommand.RaiseCanExecuteChanged();
                GoToStepCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsSelectionStep => WizardStep == 1;
        public bool IsFormatStep => WizardStep == 2;
        public bool IsCreateStep => WizardStep == 3;

        /// <summary>El footer muestra "Siguiente" en los pasos 1-2 y el botón Exportar (ya existente) en el 3.</summary>
        public bool ShowNextButton => !IsCreateStep;

        public bool CanGoBack => WizardStep > 1 && !IsExporting;
        public bool CanGoNext => WizardStep < 3 && !IsExporting && (WizardStep != 1 || SelectedCount > 0);

        public RelayCommand GoBackCommand { get; }
        public RelayCommand GoNextCommand { get; }

        /// <summary>Tabs del wizard clicables (ProSheets-like): saltar a un paso si las precondiciones se cumplen.</summary>
        public RelayCommand GoToStepCommand { get; }

        /// <summary>Clic en una opción de formato (bloqueada → Cambiar plan).</summary>
        public RelayCommand SelectFormatCommand { get; }

        /// <summary>Botón "Cambiar plan" del header/footer/diálogo de éxito -- abre Cuenta/Licencia (mismo flujo que la cinta).</summary>
        public RelayCommand ChangePlanCommand { get; }
        public event Action RequestChangePlan;

        /// <summary>Se dispara cuando un lote termina con al menos una lámina exportada con éxito.</summary>
        public event Action<ExportSummary> ExportSucceeded;

        private void GoBack()
        {
            if (WizardStep > 1) WizardStep--;
        }

        private void GoNext()
        {
            if (WizardStep < 3) WizardStep++;
        }

        private static int ParseWizardStep(object parameter)
        {
            if (parameter is int i) return i;
            if (parameter is string s && int.TryParse(s, out int n)) return n;
            return 0;
        }

        private bool CanGoToStep(object parameter)
        {
            if (IsExporting) return false;
            int step = ParseWizardStep(parameter);
            if (step == 1) return true;
            if (step == 2) return SelectedCount > 0;
            if (step == 3) return SelectedCount > 0 && FormatChoices.Count > 0;
            return false;
        }

        private void GoToStep(object parameter)
        {
            int step = ParseWizardStep(parameter);
            if (!CanGoToStep(step)) return;
            WizardStep = step;
        }

        /// <summary>"Plan X · Usadas A de B este mes" (UI_FREEMIUM_PLAN.md §3.3).</summary>
        public string QuotaFooterText
        {
            get
            {
                LicenseRuntime.EnsureInitialized();
                LicenseClient client = LicenseRuntime.Client;
                if (!client.IsLicensed) return string.Empty;

                string plan = string.IsNullOrWhiteSpace(client.PlanDisplayName) ? "—" : client.PlanDisplayName;
                string usage = QuotaDisplay.FormatSheetsUsage(client.Entitlements);
                return string.IsNullOrEmpty(usage) ? $"Plan {plan}" : $"Plan {plan} · {usage}";
            }
        }

        public bool HasQuotaFooterText => !string.IsNullOrEmpty(QuotaFooterText);

        /// <summary>Aviso suave cuando el plan free está cerca del tope de cuota.</summary>
        public bool ShowQuotaNearLimitHint
        {
            get
            {
                LicenseRuntime.EnsureInitialized();
                LicenseClient client = LicenseRuntime.Client;
                return client.IsLicensed && QuotaDisplay.IsNearLimit(client.Entitlements, client.PlanCode);
            }
        }

        public string QuotaNearLimitHint => "Cerca del límite de tu plan free — cambia de plan para exportar más.";

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

            // Saved sheet sets only ever contain ViewSheet ids -- meaningless in Views mode, where
            // HasSheetSets is already false and the combo is hidden, but guarded here too in case
            // SelectedSheetSet still holds a stale value from before the mode switch.
            if (_itemKind == ExportItemKind.Sheet && !string.Equals(SelectedSheetSet, AllSheetsLabel, StringComparison.Ordinal))
            {
                SheetSetSnapshot set = _sheetSets.FirstOrDefault(s =>
                    string.Equals(s.Name, SelectedSheetSet, StringComparison.Ordinal));

                if (set == null || !set.SheetIds.Contains(item.Sheet.Id)) return false;
            }

            // Filtro guardado activo: oculta lo que no pertenece a él (no solo lo desmarca), tal
            // como pidió el cliente -- "cuando tengo un filtro solo me debe mostrar eso".
            if (_activeSavedFilterIds != null)
            {
                if (string.IsNullOrEmpty(item.Sheet.UniqueId) || !_activeSavedFilterIds.Contains(item.Sheet.UniqueId))
                    return false;
            }

            string term = SearchText?.Trim();
            return string.IsNullOrEmpty(term) || item.Matches(term);
        }

        private void RefreshIdleProgressScale()
        {
            if (IsExporting) return;
            int steps = _selectedFormatMode == FormatMode.PdfAndDwg
                ? StepCountFor(ExportFormat.Pdf, SelectedCount) + StepCountFor(ExportFormat.Dwg, SelectedCount)
                : StepCountFor(_selectedFormatMode == FormatMode.Dwg ? ExportFormat.Dwg : ExportFormat.Pdf, SelectedCount);
            ProgressMaximum = steps > 0 ? steps : 1;
            ProgressValue = 0;
        }

        /// <summary>
        /// How many progress steps one format phase contributes to the run's total.
        /// PDF combinado 2022+: 1 paso (una llamada nativa). PDF combinado 2021: N+1
        /// (una impresión por lámina + merge DocTool). Todo lo demás: un paso por ítem.
        /// </summary>
        private int StepCountFor(ExportFormat format, int selectedCount)
        {
            if (format != ExportFormat.Pdf || !PdfCombineIntoSingleFile)
                return selectedCount;

#if REVIT2022_OR_GREATER
            return 1;
#else
            return selectedCount + 1;
#endif
        }

        /// <summary>When true, row IsSelected changes skip header/summary Raise (bulk update in progress).</summary>
        private bool _suppressSelectionNotifications;

        private void OnSheetItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SheetItemViewModel.IsSelected)) return;
            if (_suppressSelectionNotifications) return;

            NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            Raise(nameof(SelectedCount), nameof(SelectionSummary), nameof(CanExport), nameof(CanGoNext),
                  nameof(AreAllVisibleSelected));
            ExportCommand.RaiseCanExecuteChanged();
            GoNextCommand.RaiseCanExecuteChanged();
            GoToStepCommand.RaiseCanExecuteChanged();
            RefreshIdleProgressScale();
        }

        private void SetVisibleSelection(bool selected)
        {
            _suppressSelectionNotifications = true;
            try
            {
                foreach (SheetItemViewModel item in SheetsView.Cast<SheetItemViewModel>().ToList())
                    item.IsSelected = selected;
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            NotifySelectionChanged();
        }

        private void InvertVisibleSelection()
        {
            _suppressSelectionNotifications = true;
            try
            {
                foreach (SheetItemViewModel item in SheetsView.Cast<SheetItemViewModel>().ToList())
                    item.IsSelected = !item.IsSelected;
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            NotifySelectionChanged();
        }

        private void SelectVisiblePending()
        {
            _suppressSelectionNotifications = true;
            try
            {
                foreach (SheetItemViewModel item in SheetsView.Cast<SheetItemViewModel>().ToList())
                    item.IsSelected = !item.WasExported;
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            NotifySelectionChanged();
        }

        private void BrowseFolder()
        {
            string picked = _dialogs.PickFolder(
                $"Selecciona la carpeta donde se exportarán las {ItemNounPlural}",
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

            if (!TryPrepareRunDestinationFolders(out string pathError))
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
            Raise(nameof(DestinationFolder));
            IsExporting = true;

            foreach (SheetItemViewModel item in Sheets)
                item.ResetRunProgress();
            foreach (SheetItemViewModel item in selectedItems)
                item.RunProgressLabel = "En cola";

            _isMultiFormat = _selectedFormatMode == FormatMode.PdfAndDwg;
            _multiFormatPhase = 0;
            _progressOffset = 0;
            _firstPhaseResult = null;
            _pendingSheets = selected;
            _totalSteps = _isMultiFormat
                ? StepCountFor(ExportFormat.Pdf, selected.Count) + StepCountFor(ExportFormat.Dwg, selected.Count)
                : StepCountFor(_selectedFormatMode == FormatMode.Dwg ? ExportFormat.Dwg : ExportFormat.Pdf, selected.Count);
            ProgressMaximum = _totalSteps;

            ExportFormat firstFormat = _selectedFormatMode == FormatMode.Dwg
                ? ExportFormat.Dwg
                : ExportFormat.Pdf;

            LaunchFormat(firstFormat, selected);
        }

        /// <summary>
        /// Reserva carpetas únicas para el lote (no reutiliza una exportación anterior) y las crea.
        /// </summary>
        private bool TryPrepareRunDestinationFolders(out string error)
        {
            error = null;
            _runDestinationFolders = null;

            if (!ExportPathHelper.TryEnsureWritable(OutputFolder, out error))
                return false;

            var reserved = new Dictionary<ExportFormat, string>();

            if (_selectedFormatMode == FormatMode.PdfAndDwg || _selectedFormatMode == FormatMode.Pdf)
            {
                string pdfPath = ExportPathHelper.AllocateUniqueDirectoryPath(GetDesiredDestinationFolder(ExportFormat.Pdf));
                if (!ExportPathHelper.TryEnsureWritable(pdfPath, out error))
                    return false;
                reserved[ExportFormat.Pdf] = pdfPath;
            }

            if (_selectedFormatMode == FormatMode.PdfAndDwg || _selectedFormatMode == FormatMode.Dwg)
            {
                string dwgPath = ExportPathHelper.AllocateUniqueDirectoryPath(GetDesiredDestinationFolder(ExportFormat.Dwg));
                if (!ExportPathHelper.TryEnsureWritable(dwgPath, out error))
                    return false;
                reserved[ExportFormat.Dwg] = dwgPath;
            }

            _runDestinationFolders = reserved;
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

        /// <param name="onFailedToStart">
        /// Solo lo usa OnFinished para la SEGUNDA fase de un run PDF+DWG (DWG falla al arrancar
        /// DESPUÉS de que PDF ya terminó con éxito). Sin esto, el catch de abajo -- pensado para la
        /// PRIMERA fase (PDF falla al arrancar, cae a intentar solo DWG) -- se quedaba con la
        /// excepción sin relanzarla porque para entonces _multiFormatPhase ya valía 1, así que el
        /// try/catch de OnFinished alrededor de esta misma llamada nunca llegaba a ejecutarse: los
        /// resultados de la fase PDF ya exitosa (historial, cuota, popup de éxito) se perdían en
        /// silencio en vez de cerrarse con FinishExport.
        /// </param>
        private void LaunchFormat(ExportFormat format, List<SheetSnapshot> sheets, Action<Exception> onFailedToStart = null)
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
                // "Combinar en un solo archivo" es una forma de ejecución distinta (una sola llamada/
                // trabajo para todo el lote, no uno por lámina/vista) -- ver el comentario de
                // CombinedPdfExportJob (2022+) / CombinedPrintDriverPdfExportJob (2021).
                // RevitJobScheduler no distingue: cualquier IRevitStepJob le sirve.
                if (format == ExportFormat.Pdf && PdfCombineIntoSingleFile)
                {
#if REVIT2022_OR_GREATER
                    var combinedJob = new CombinedPdfExportJob(request, sheets, OnProgress, OnItemCompleted, OnFinished);
#else
                    var combinedJob = new CombinedPrintDriverPdfExportJob(request, sheets, OnProgress, OnItemCompleted, OnFinished);
#endif
                    _scheduler.Start(combinedJob);
                    return;
                }

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
                    _progressOffset = StepCountFor(format, sheets.Count);
                    StatusText = $"{ExportFormatInfo.Label(format)}: {ex.Message} — Continuando con DWG...";
                    LaunchFormat(ExportFormat.Dwg, sheets);
                    return;
                }

                if (onFailedToStart != null)
                {
                    onFailedToStart(ex);
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
            StatusText = $"Cancelando al terminar la {ItemNounSingular} actual...";
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
                    HideHelperGraphics = true,
                    SavedSetupName = IsUsingCustomDwgOptions ? null : DwgSavedSetupName
                };
            }

            return new PdfExportSettings
            {
                PrinterName = SelectedPrinter ?? string.Empty,
                MatchSheetSize = PdfMatchSheetSize,
                FitToPage = PdfFitToPage,
                ZoomPercentage = PdfZoomPercentage,
                PaperPlacement = PdfPlacementMode,
                CornerMarginMode = PdfCornerMarginMode,
                OffsetXInches = PdfOffsetXInches,
                OffsetYInches = PdfOffsetYInches,
                ColorMode = PdfColorMode,
                RasterQuality = PdfRasterQuality,
                HideCropBoundaries = PdfHideCropBoundaries,
                HideScopeBoxes = PdfHideScopeBoxes,
                HideUnreferencedViewTags = PdfHideUnreferencedViewTags,
                HideReferencePlanes = PdfHideReferencePlanes,
                HiddenLineProcessing = PdfHiddenLineProcessing,
                ViewLinksInBlue = PdfViewLinksInBlue,
                ReplaceHalftoneWithThinLines = PdfReplaceHalftoneWithThinLines,
                MaskCoincidentLines = PdfMaskCoincidentLines,
                CombineIntoSingleFile = PdfCombineIntoSingleFile,
                CombinedFileName = PdfCombinedFileName
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

                // Marca la lámina actual en el grid del paso Crear.
                if (_pendingSheets != null && progress.Completed < _pendingSheets.Count)
                {
                    SheetSnapshot current = _pendingSheets[progress.Completed];
                    if (_itemsBySheet != null && _itemsBySheet.TryGetValue(current, out SheetItemViewModel row))
                        row.RunProgressLabel = "Exportando…";
                }
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

            if (sheet != null && _itemsBySheet != null && _itemsBySheet.TryGetValue(sheet, out SheetItemViewModel row))
            {
                if (result.Succeeded)
                {
                    if (_isMultiFormat)
                    {
                        string label = ExportFormatInfo.Label(_currentExportFormat);
                        row.RunProgressLabel = _multiFormatPhase == 0
                            ? label + " OK"
                            : "PDF+DWG OK";
                    }
                    else
                    {
                        row.RunProgressLabel = "OK";
                    }

                    row.RunErrorDetail = string.Empty;
                    row.RunSucceeded = true;
                }
                else
                {
                    row.RunProgressLabel = "Error";
                    row.RunErrorDetail = result.Message ?? string.Empty;
                    row.RunSucceeded = false;
                }
            }

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

                Raise(nameof(QuotaFooterText), nameof(HasQuotaFooterText), nameof(ShowQuotaNearLimitHint),
                      nameof(ShowUpgradeHint));
            }
            catch (Exception ex)
            {
                _log.Warn("Error al registrar uso de licencia: " + ex.Message);
            }
        }

        private void FlushUsageToServer()
        {
            try
            {
                LicenseRuntime.EnsureInitialized();
                LicenseRuntime.Client.FlushUsageQueueAsync(default).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo sincronizar el uso con el servidor: " + ex.Message);
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
                // La fase 0 de un run PdfAndDwg siempre es PDF (ver LaunchFormat/firstFormat).
                _progressOffset = StepCountFor(ExportFormat.Pdf, _pendingSheets.Count);

                if (_itemsBySheet != null)
                {
                    foreach (SheetSnapshot sheet in _pendingSheets)
                    {
                        if (_itemsBySheet.TryGetValue(sheet, out SheetItemViewModel row)
                            && string.Equals(row.RunProgressLabel, "PDF OK", StringComparison.Ordinal))
                            row.RunProgressLabel = "PDF OK · DWG en cola";
                    }
                }

                if (result.HasSetupError)
                    _log.Warn("La exportación PDF no pudo iniciarse: " + result.SetupError);

                try
                {
                    // onFailedToStart: ver el comentario del parámetro en LaunchFormat. Sin esto, un
                    // fallo aquí no propagaba hasta este catch (quedaba atrapado dentro de
                    // LaunchFormat), así que los resultados ya exitosos de la fase PDF nunca llegaban
                    // a FinishExport -- ni historial, ni cuota, ni popup de éxito, pese a que los PDF
                    // sí se habían generado.
                    LaunchFormat(ExportFormat.Dwg, _pendingSheets, onFailedToStart: ex =>
                    {
                        _log.Error("No se pudo iniciar la exportación DWG.", ex);
                        FinishExport(result);
                    });
                }
                catch (Exception ex)
                {
                    // Red de seguridad genuina para cualquier excepción que LaunchFormat no atrape
                    // internamente (no debería llegar aquí en el caso ya cubierto arriba).
                    _log.Error("No se pudo iniciar la exportación DWG.", ex);
                    FinishExport(result);
                }

                return;
            }

            FinishExport(result);
        }

        private void FinishExport(BatchResult lastResult)
        {
            FlushUsageToServer();

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
                    StatusText = $"No se exportó ninguna {ItemNounSingular}.";
                    _dialogs.ShowError(DialogTitle,
                        $"PDF: {_firstPhaseResult.SetupError}\nDWG: {lastResult.SetupError}");
                    return;
                }
            }
            else if (lastResult.HasSetupError)
            {
                StatusText = $"No se exportó ninguna {ItemNounSingular}.";
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

            // Surface the first failure reason in StatusText (and a dialog when the whole batch
            // failed with the same message, typical of combined PDF). Previously only the grid
            // showed the word "Error" with no detail.
            if (totalFailed > 0)
            {
                string firstFailure = null;
                foreach (ExportResultViewModel row in Results)
                {
                    if (!row.Succeeded && !string.IsNullOrWhiteSpace(row.Detail))
                    {
                        firstFailure = row.Detail;
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(firstFailure))
                {
                    string shortMsg = firstFailure.Length <= 180
                        ? firstFailure
                        : firstFailure.Substring(0, 177) + "…";
                    status += " — " + shortMsg;

                    if (totalSucceeded == 0 && !wasCancelled)
                        _dialogs.ShowError(DialogTitle, firstFailure);
                }
            }

            StatusText = status;

            if (totalFailed > 0)
                ShowResults = true;

            if (totalSucceeded > 0 && OpenFolderWhenDone)
                _dialogs.Reveal(RevealTarget);

            Raise(nameof(QuotaFooterText), nameof(HasQuotaFooterText), nameof(ShowQuotaNearLimitHint));

            if (totalSucceeded > 0)
                ExportSucceeded?.Invoke(new ExportSummary(totalSucceeded, totalFailed, folderText, RevealTarget));

            // Vuelve a null (ver el comentario del campo: "null fuera de un export") ahora que todo lo
            // de arriba que necesitaba las rutas reales de ESTE lote ya las leyó. Sin esto,
            // GetDestinationFolder/DestinationFolder/RevealTarget seguían mostrando la carpeta del
            // lote YA TERMINADO si el usuario cambiaba OutputFolder o de formato/selección antes de
            // exportar de nuevo -- el archivo se escribía en el lugar correcto igual (StartExport
            // recalcula antes de escribir), pero el preview y "Abrir carpeta" quedaban desincronizados
            // hasta el siguiente export.
            _runDestinationFolders = null;
            Raise(nameof(DestinationFolder));
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
            _pdfZoomPercentage = preferences.PdfZoomPercentage;

            // Migración desde el viejo PdfNoMargin (bool), de antes de que existiera
            // PdfPaperPlacementRaw -- ver el comentario en BatchExportPreferences.PdfPaperPlacementRaw.
            // También cubre el enum de 3 valores anterior (Center/OffsetFromCorner/PrinterMargin):
            // "PrinterMargin" ya no es un valor top-level, ahora es CornerMarginMode.PrinterLimit
            // bajo OffsetFromCorner -- de ahí que Enum.TryParse falle para ese valor guardado y caiga
            // al mismo camino que un archivo viejo sin el campo en absoluto.
            if (Enum.TryParse(preferences.PdfPaperPlacementRaw, true, out PdfPaperPlacement savedPlacement))
            {
                _pdfPlacementMode = savedPlacement;
                _pdfCornerMarginMode = Enum.TryParse(preferences.PdfCornerMarginModeRaw, true, out PdfCornerMarginMode savedCornerMode)
                    ? savedCornerMode
                    : PdfCornerMarginMode.UserDefined;
            }
            else if (preferences.PdfNoMargin)
            {
                _pdfPlacementMode = PdfPaperPlacement.Center;
            }
            else
            {
                _pdfPlacementMode = PdfPaperPlacement.OffsetFromCorner;
                _pdfCornerMarginMode = PdfCornerMarginMode.PrinterLimit;
            }
            _pdfOffsetXInches = preferences.PdfOffsetXInches;
            _pdfOffsetYInches = preferences.PdfOffsetYInches;
            _pdfHideCropBoundaries = preferences.PdfHideCropBoundaries;
            _pdfHideScopeBoxes = preferences.PdfHideScopeBoxes;
            _pdfHideUnreferencedViewTags = preferences.PdfHideUnreferencedViewTags;
            _pdfHideReferencePlanes = preferences.PdfHideReferencePlanes;
            _pdfColorMode = preferences.PdfColorMode;
            _pdfRasterQuality = preferences.PdfRasterQuality;
            _pdfHiddenLineProcessing = preferences.PdfHiddenLineProcessing;
            _pdfViewLinksInBlue = preferences.PdfViewLinksInBlue;
            _pdfReplaceHalftoneWithThinLines = preferences.PdfReplaceHalftoneWithThinLines;
            _pdfMaskCoincidentLines = preferences.PdfMaskCoincidentLines;
            _pdfCombineIntoSingleFile = preferences.PdfCombineIntoSingleFile;
            _pdfCombinedFileName = string.IsNullOrWhiteSpace(preferences.PdfCombinedFileName)
                ? "{ProjectTitle}_combinado"
                : preferences.PdfCombinedFileName;

            _dwgFileVersion = preferences.DwgFileVersion;
            _dwgMergeViews = preferences.DwgMergeViews;
            _dwgSharedCoordinates = preferences.DwgSharedCoordinates;
            _dwgAlsoExportImage = preferences.DwgAlsoExportImage;

            _selectedPrinter = PdfPrinterOptions.FindByNameContains(RequiredPdfPrinterHint);

            Raise(nameof(NamingPattern), nameof(NamingPreview), nameof(SelectedNamingPreset));
        }

        private void SavePreferences()
        {
            _preferences.OutputFolder = OutputFolder;
            _preferences.NamingPattern = NamingPattern;
            _preferences.Format = SelectedFormatMode;
            _preferences.OpenFolderWhenDone = OpenFolderWhenDone;
            _preferences.PdfMatchSheetSize = PdfMatchSheetSize;
            _preferences.PdfFitToPage = PdfFitToPage;
            _preferences.PdfZoomPercentage = PdfZoomPercentage;
            _preferences.PdfPaperPlacementRaw = PdfPlacementMode.ToString();
            _preferences.PdfCornerMarginModeRaw = PdfCornerMarginMode.ToString();
            // PdfNoMargin ya no lo lee nadie más que la migración de arriba, pero se sigue
            // escribiendo en sincronía por si alguna vez se necesita rodar el archivo de
            // preferencias hacia atrás a una versión anterior del complemento.
            _preferences.PdfNoMargin = PdfPlacementMode == PdfPaperPlacement.Center;
            _preferences.PdfOffsetXInches = PdfOffsetXInches;
            _preferences.PdfOffsetYInches = PdfOffsetYInches;
            _preferences.PdfHideCropBoundaries = PdfHideCropBoundaries;
            _preferences.PdfHideScopeBoxes = PdfHideScopeBoxes;
            _preferences.PdfHideUnreferencedViewTags = PdfHideUnreferencedViewTags;
            _preferences.PdfHideReferencePlanes = PdfHideReferencePlanes;
            _preferences.PdfColorMode = PdfColorMode;
            _preferences.PdfRasterQuality = PdfRasterQuality;
            _preferences.PdfHiddenLineProcessing = PdfHiddenLineProcessing;
            _preferences.PdfViewLinksInBlue = PdfViewLinksInBlue;
            _preferences.PdfReplaceHalftoneWithThinLines = PdfReplaceHalftoneWithThinLines;
            _preferences.PdfMaskCoincidentLines = PdfMaskCoincidentLines;
            _preferences.PdfCombineIntoSingleFile = PdfCombineIntoSingleFile;
            _preferences.PdfCombinedFileName = PdfCombinedFileName;
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

    /// <summary>Resumen para ExportSuccessWindow -- solo datos ya calculados por FinishExport, sin lógica propia.</summary>
    public sealed class ExportSummary
    {
        public ExportSummary(int succeededCount, int failedCount, string folderText, string revealTarget)
        {
            SucceededCount = succeededCount;
            FailedCount = failedCount;
            FolderText = folderText;
            RevealTarget = revealTarget;
        }

        public int SucceededCount { get; }
        public int FailedCount { get; }
        public string FolderText { get; }
        public string RevealTarget { get; }
    }
}
