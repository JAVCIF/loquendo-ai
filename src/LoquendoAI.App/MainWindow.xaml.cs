using System.IO;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Projects;
using Microsoft.Win32;

namespace LoquendoAI.App;

public partial class MainWindow : Window
{
    private readonly ProjectService _projects = new();
    private readonly AssetLibraryService _assetLibrary = new();
    private SqliteProjectRepository? _currentRepository;
    private IReadOnlyList<AssetRow> _allAssetRows = Array.Empty<AssetRow>();
    private IReadOnlyList<AssetRow> _filteredAssetRows = Array.Empty<AssetRow>();
    private LibraryFolderChoice[] _allLibraryFolders = [];
    private Dictionary<Guid, LibraryFolderChoice[]> _libraryFoldersBySource = new();
    private int _assetFilterVersion;
    private bool _assetFiltersDirty;
    private const int AssetVisibleLimit = 1200;
    private CancellationTokenSource? _scanCancellation;
    private bool _isWorking;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowBounds();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        InitializeVoiceLab();
        InitializeScriptEditor();
        InitializeDialogues();
    }

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dónde guardar el proyecto Loquendo AI" };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await SetRepositoryAsync(await _projects.CreateAsync(dialog.FolderName, ProjectNameBox.Text));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Abrir proyecto Loquendo AI" };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var root = dialog.FolderName;
            var repository = await Task.Run(() => _projects.OpenAsync(root));
            await SetRepositoryAsync(repository);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task SetRepositoryAsync(SqliteProjectRepository repository)
    {
        _aiRequestCancellation?.Cancel();
        _voicePreviewCancellation?.Cancel();
        _sceneVoiceGenerationCancellation?.Cancel();
        _sceneRenderCancellation?.Cancel();
        ClearScenePreview();
        _directorSceneId = null;
        _directorDraft = [];
        _directorDraftValidated = false;
        _aiDraftPrompt = null;
        _aiRecordedDraft = false;
        AiDraftGrid.ItemsSource = null;
        AiApplyButton.IsEnabled = false;
        DirectorReplaceCheck.IsEnabled = true;
        DirectorDraftGrid.ItemsSource = null;
        DirectorApplyButton.IsEnabled = false;
        DirectorStatusText.Text = "Selecciona una escena y prepara el borrador.";
        AiDirectorStatusText.Text = "Escribe la premisa y elige un modelo de Ollama.";
        StopVoicePreview(deleteLastFile: true);
        _scriptAudioPlayer.Stop();
        _scriptAudioPlayer.Close();

        if (_currentRepository is not null)
        {
            CloseDialogueProject();
            await _currentRepository.DisposeAsync();
        }

        _currentRepository = repository;
        await EnsureNpcProfileAsync(repository);
        StartAutomaticBackup(repository);
        await LoadDirectorMasterPromptAsync(repository);
        ResetScriptAssetCache();
        StatusText.Text = $"Proyecto abierto: {repository.Manifest.Name} | schema v{repository.SchemaVersion}";
        ProjectPathText.Text = repository.ProjectRoot;
        LibraryTab.IsEnabled = true;
        VoiceLabTab.IsEnabled = true;
        ScriptTab.IsEnabled = true;
        await RefreshLibraryAsync();
        await RefreshVoiceLabAsync();
        await RefreshScriptAsync();
        await RefreshDirectVoicesAsync();
        await MatchNpcVoiceToCatalogAsync();
        await OpenDialogueProjectAsync(repository);
    }

    private async void AddSource_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null)
            return;

        var dialog = new OpenFolderDialog { Title = "Selecciona una carpeta fuente de assets" };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var root = Path.GetFullPath(dialog.FolderName);
            var existingSources = await _currentRepository.GetAssetSourcesAsync();
            var existing = existingSources.FirstOrDefault(s => PathsEqual(s.RootPath, root));
            if (existing is not null)
            {
                MessageBox.Show(this, "Esa carpeta ya está agregada como fuente. Se abrirán sus reglas.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Information);
                await EditSourceAsync(existing);
                return;
            }

            var setup = new AssetSourceSetupWindow(root) { Owner = this };
            if (setup.ShowDialog() != true || setup.ResultSource is null || setup.ResultRules is null)
                return;

            await SaveAndScanSourceAsync(setup.ResultSource, setup.ResultRules);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void EditSource_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesGrid.SelectedItem is not SourceRow selected)
        {
            MessageBox.Show(this, "Selecciona una fuente primero.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await EditSourceAsync(selected.Source);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task EditSourceAsync(AssetSource source)
    {
        if (_currentRepository is null)
            return;

        if (!Directory.Exists(source.RootPath))
        {
            MessageBox.Show(this, $"La carpeta no existe actualmente:\n{source.RootPath}", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rules = await _currentRepository.GetFolderRulesAsync(source.Id);
        var setup = new AssetSourceSetupWindow(source.RootPath, source, rules) { Owner = this };
        if (setup.ShowDialog() != true || setup.ResultSource is null || setup.ResultRules is null)
            return;

        await SaveAndScanSourceAsync(setup.ResultSource with { LastScanUtc = source.LastScanUtc }, setup.ResultRules);
    }

    private async Task SaveAndScanSourceAsync(AssetSource source, IReadOnlyList<FolderRule> rules)
    {
        if (_currentRepository is null)
            return;

        await _currentRepository.UpsertAssetSourceAsync(source);
        await _currentRepository.ReplaceFolderRulesAsync(source.Id, rules);
        await ScanSourceAsync(source, rules);
    }


    private async void RelinkSource_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || SourcesGrid.SelectedItem is not SourceRow selected)
        {
            MessageBox.Show(this, "Selecciona una fuente primero.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Selecciona la nueva ubicación de la fuente",
            InitialDirectory = Directory.Exists(selected.Source.RootPath) ? selected.Source.RootPath : string.Empty
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var newRoot = Path.GetFullPath(dialog.FolderName);
            var sources = await _currentRepository.GetAssetSourcesAsync();
            if (sources.Any(s => s.Id != selected.Source.Id && PathsEqual(s.RootPath, newRoot)))
            {
                MessageBox.Show(this, "Esa carpeta ya pertenece a otra fuente del proyecto.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var updated = selected.Source with { RootPath = newRoot };
            await _currentRepository.UpsertAssetSourceAsync(updated);
            var rules = await _currentRepository.GetFolderRulesAsync(updated.Id);
            await ScanSourceAsync(updated, rules);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void RescanSource_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || SourcesGrid.SelectedItem is not SourceRow selected)
        {
            MessageBox.Show(this, "Selecciona una fuente primero.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var rules = await _currentRepository.GetFolderRulesAsync(selected.Source.Id);
            await ScanSourceAsync(selected.Source, rules);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void RescanAll_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || _isWorking)
            return;

        try
        {
            var sources = await _currentRepository.GetAssetSourcesAsync();
            foreach (var source in sources.Where(s => s.Enabled))
            {
                if (!Directory.Exists(source.RootPath))
                    continue;

                var rules = await _currentRepository.GetFolderRulesAsync(source.Id);
                if (!await ScanSourceAsync(source, rules, refreshAfter: false))
                    break;
            }

            if (!_isWorking)
                await RefreshLibraryAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task<bool> ScanSourceAsync(AssetSource source, IReadOnlyList<FolderRule> rules, bool refreshAfter = true)
    {
        if (_currentRepository is null || _isWorking)
            return false;

        var projectRoot = _currentRepository.ProjectRoot;
        var manifest = _currentRepository.Manifest;
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        SetWorking(true, $"Escaneando {source.Name} en segundo plano…");

        try
        {
            var progress = new Progress<(int Current, string Message)>(p =>
            {
                FooterText.Text = $"{source.Name}: {p.Current:N0} archivos revisados — {p.Message}";
            });

            // Important: scans can involve tens of thousands of files.  Use a dedicated
            // repository/SQLite connection on a worker thread so WPF never performs the
            // filesystem walk, hashing or per-asset database work on the UI thread.
            var summary = await Task.Run(async () =>
            {
                await using var scanRepository = new SqliteProjectRepository(projectRoot, manifest);
                await scanRepository.AttachAsync(cancellation.Token).ConfigureAwait(false);
                return await _assetLibrary
                    .ScanSourceAsync(scanRepository, source, rules, progress, cancellation.Token)
                    .ConfigureAwait(false);
            }, cancellation.Token);

            FooterText.Text = $"{source.Name}: +{summary.Imported} nuevos, {summary.Updated} actualizados ({summary.Moved} movidos/renombrados), {summary.Skipped} sin cambios, {summary.Missing} faltantes, {summary.NeedsCutout} renders pendientes de fondo.";

            if (summary.Errors > 0)
            {
                var preview = string.Join(Environment.NewLine, summary.ErrorMessages.Take(8));
                MessageBox.Show(this,
                    $"El escaneo terminó con {summary.Errors} avisos. Los demás archivos sí fueron procesados.\n\n{preview}",
                    "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (refreshAfter)
                await RefreshLibraryAsync();

            return true;
        }
        catch (OperationCanceledException)
        {
            FooterText.Text = $"Escaneo de {source.Name} cancelado. Los archivos ya procesados permanecen catalogados.";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, cancellation))
                _scanCancellation = null;
            cancellation.Dispose();
            SetWorking(false, FooterText.Text);
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is null || _scanCancellation.IsCancellationRequested)
            return;

        FooterText.Text = "Cancelando escaneo…";
        CancelScanButton.IsEnabled = false;
        _scanCancellation.Cancel();
    }

    private async void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (_isWorking) return;
        if (_currentRepository is null || SourcesGrid.SelectedItem is not SourceRow selected)
        {
            MessageBox.Show(this, "Selecciona una fuente primero.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(this,
            $"Quitar '{selected.Source.Name}' del catálogo?\n\nLos archivos originales NO se borrarán.",
            "Loquendo AI", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            // Removing a source deletes all its catalog rows (and their search entries): tens of
            // thousands of rows for a big library, so it runs on a worker connection.
            var repository = _currentRepository;
            SetWorking(true, $"Quitando {selected.Source.Name} del catálogo…");
            CancelScanButton.Visibility = Visibility.Collapsed;
            try
            {
                await Task.Run(async () =>
                {
                    await using var writer = new SqliteProjectRepository(repository.ProjectRoot, repository.Manifest);
                    await writer.AttachAsync().ConfigureAwait(false);
                    await writer.DeleteAssetSourceAsync(selected.Source.Id).ConfigureAwait(false);
                });
            }
            finally { SetWorking(false, $"{selected.Source.Name} quitada del catálogo (los archivos originales siguen en su carpeta)."); }
            if (_currentRepository != repository) return;
            await RefreshLibraryAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task RefreshLibraryAsync()
    {
        if (_currentRepository is null)
            return;

        var repository = _currentRepository;
        var selectedSource = (AssetSourceFilterBox.SelectedItem as LibrarySourceChoice)?.Id;
        var selectedFolder = (AssetFolderFilterBox.SelectedItem as LibraryFolderChoice)?.Path;
        var selectedKind = (AssetKindFilterBox.SelectedItem as LibraryKindChoice)?.Kind;
        // Building a 60k+ row library snapshot is real work too. The catalog reads run on their own
        // read-only connections off the dispatcher; grouping and sorting stay on the worker as well
        // and only the finished arrays are bound.
        var snapshot = await Task.Run(async () =>
        {
            var sources = await repository.GetAssetSourcesAsync().ConfigureAwait(false);
            var assets = await repository.GetAssetsAsync().ConfigureAwait(false);
            var directorTags = (await repository.GetAssetTagsAsync().ConfigureAwait(false))
                .GroupBy(tag => tag.AssetId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var sourceNames = sources.ToDictionary(s => s.Id, s => s.Name);
            var assetsBySource = assets
                .Where(a => a.SourceId is not null)
                .GroupBy(a => a.SourceId!.Value)
                .ToDictionary(g => g.Key, g => g.ToArray());

            var sourceRows = sources.Select(source =>
            {
                assetsBySource.TryGetValue(source.Id, out var sourceAssets);
                sourceAssets ??= Array.Empty<AssetRecord>();
                return new SourceRow(
                    source,
                    sourceAssets.Length,
                    sourceAssets.Count(a => a.CutoutStatus == CutoutStatus.NeedsCutout),
                    sourceAssets.Count(a => a.IsMissing));
            }).ToArray();

            var assetRows = assets
                .Where(a => a.SourceId is not null)
                .Select(a => new AssetRow(
                    a.DisplayName,
                    KindLabel(a.Kind),
                    a.SubjectName ?? string.Empty,
                    a.CollectionName ?? string.Empty,
                    CutoutLabel(a.CutoutStatus),
                    a.SourceId is Guid id && sourceNames.TryGetValue(id, out var sourceName) ? sourceName : string.Empty,
                    a.SourceRelativePath ?? a.RelativePath,
                    a.IsMissing ? "Faltante" : "OK",
                    a.Id,
                    directorTags.TryGetValue(a.Id, out var tags)
                        ? tags.FirstOrDefault(x => x.Key == "director.manual.description")?.Value ??
                          tags.FirstOrDefault(x => x.Key == "director.auto.description")?.Value ?? ""
                        : "",
                    a.SourceId, a.Kind))
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var pathsBySource = new Dictionary<Guid, HashSet<string>>();
            var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assetRows)
            {
                if (asset.SourceId is not Guid sourceId) continue;
                if (!pathsBySource.TryGetValue(sourceId, out var sourcePaths))
                    pathsBySource[sourceId] = sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var parts = asset.RelativePath.Replace('\\', '/').Split('/');
                for (var i = 1; i < parts.Length; i++)
                {
                    var path = string.Join('/', parts.Take(i));
                    sourcePaths.Add(path);
                    allPaths.Add(path);
                }
            }
            LibraryFolderChoice[] Choices(IEnumerable<string> paths) =>
                [new LibraryFolderChoice("", "Todas las carpetas"),
                    .. paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Select(path => new LibraryFolderChoice(path, path))];

            return (SourceRows: sourceRows, AssetRows: (IReadOnlyList<AssetRow>)assetRows,
                Sources: sources, AllFolders: Choices(allPaths),
                FoldersBySource: pathsBySource.ToDictionary(pair => pair.Key,
                    pair => Choices(pair.Value)));
        });

        if (_currentRepository != repository) return;
        SourcesGrid.ItemsSource = snapshot.SourceRows;
        _allAssetRows = snapshot.AssetRows;
        _allLibraryFolders = snapshot.AllFolders;
        _libraryFoldersBySource = snapshot.FoldersBySource;
        AssetKindFilterBox.ItemsSource = new[] { new LibraryKindChoice(null, "Todos los tipos") }
            .Concat(Enum.GetValues<AssetKind>().Select(kind => new LibraryKindChoice(kind, KindLabel(kind))))
            .ToArray();
        AssetKindFilterBox.SelectedItem = AssetKindFilterBox.Items.Cast<LibraryKindChoice>()
            .FirstOrDefault(choice => choice.Kind == selectedKind);
        AssetSourceFilterBox.ItemsSource = new[] { new LibrarySourceChoice(null, "Todas las fuentes") }
            .Concat(snapshot.Sources.Select(source => new LibrarySourceChoice(source.Id, source.Name)))
            .ToArray();
        AssetSourceFilterBox.SelectedItem = AssetSourceFilterBox.Items.Cast<LibrarySourceChoice>()
            .FirstOrDefault(choice => choice.Id == selectedSource);
        PopulateAssetFolderFilter();
        AssetFolderFilterBox.SelectedItem = AssetFolderFilterBox.Items.Cast<LibraryFolderChoice>()
            .FirstOrDefault(choice => choice.Path == selectedFolder) ?? AssetFolderFilterBox.Items[0];
        await ApplyAssetFilterAsync();

        // The script editor has its own lazy per-kind cache. A newly scanned SFX
        // folder (or a reclassified rule) must become visible without reopening
        // the project. Keep the current asset selection whenever it still exists.
        await RefreshScriptAssetsAfterCatalogChangeAsync();
    }

    private sealed record LibraryKindChoice(AssetKind? Kind, string Name);
    private sealed record LibrarySourceChoice(Guid? Id, string Name);
    private sealed record LibraryFolderChoice(string Path, string Name);

    private void AssetSourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkAssetFiltersDirty();
        PopulateAssetFolderFilter();
    }

    private void AssetFilterSelection_Changed(object sender, SelectionChangedEventArgs e)
        => MarkAssetFiltersDirty();
    private void AssetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => MarkAssetFiltersDirty();

    private void MarkAssetFiltersDirty()
    {
        _assetFiltersDirty = true;
        if (AssetFilterStatusText is not null)
            AssetFilterStatusText.Text = "Filtros cambiados: pulsa Buscar antes de editar todos los filtrados.";
    }

    private void PopulateAssetFolderFilter()
    {
        if (AssetFolderFilterBox is null) return;
        var sourceId = (AssetSourceFilterBox.SelectedItem as LibrarySourceChoice)?.Id;
        AssetFolderFilterBox.ItemsSource = sourceId is Guid id &&
            _libraryFoldersBySource.TryGetValue(id, out var folders) ? folders : _allLibraryFolders;
        AssetFolderFilterBox.SelectedIndex = 0;
    }

    private async void AssetSearch_Click(object sender, RoutedEventArgs e) => await ApplyAssetFilterAsync();
    private async void AssetSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Return) return;
        e.Handled = true;
        await ApplyAssetFilterAsync();
    }

    private async Task ApplyAssetFilterAsync()
    {
        if (AssetsGrid is null) return;
        var version = ++_assetFilterVersion;
        var repository = _currentRepository;
        var snapshot = _allAssetRows;
        var query = AssetSearchBox.Text.Trim();
        var kind = (AssetKindFilterBox.SelectedItem as LibraryKindChoice)?.Kind;
        var source = (AssetSourceFilterBox.SelectedItem as LibrarySourceChoice)?.Id;
        var folder = (AssetFolderFilterBox.SelectedItem as LibraryFolderChoice)?.Path ?? "";
        AssetFilterStatusText.Text = "Buscando…";
        // Full-text index (every word, as a prefix, without accents, also in AI/manual tags), best
        // first; the plain "contains the whole text" match is kept so nothing found before is lost.
        IReadOnlyList<Guid>? ranked = null;
        if (query.Length > 0 && repository is not null)
            try { ranked = await Task.Run(() => repository.SearchAssetIdsAsync(query)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { ranked = null; }
        var rank = ranked?.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index) ?? new Dictionary<Guid, int>();
        var matches = await Task.Run(() => snapshot.Where(asset =>
            (kind is null || asset.AssetKind == kind) &&
            (source is null || asset.SourceId == source) &&
            (folder.Length == 0 || asset.RelativePath.Replace('\\', '/').StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || rank.ContainsKey(asset.AssetId) ||
             asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             asset.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             asset.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             asset.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             asset.Collection.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(asset => rank.TryGetValue(asset.AssetId, out var position) ? position : int.MaxValue)
            .ToArray());
        if (version != _assetFilterVersion || _currentRepository != repository || !ReferenceEquals(snapshot, _allAssetRows)) return;
        _filteredAssetRows = matches;
        _assetFiltersDirty = false;
        AssetsGrid.ItemsSource = matches.Take(AssetVisibleLimit).ToArray();
        AssetFilterStatusText.Text = matches.Length <= AssetVisibleLimit
            ? $"{matches.Length} resultado(s)" :
              $"{matches.Length} resultado(s); mostrando {AssetVisibleLimit}. Editar filtrados incluye todos.";
    }

    private void SetWorking(bool working, string message)
    {
        _isWorking = working;
        WorkProgress.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        CancelScanButton.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        CancelScanButton.IsEnabled = working;
        LibraryActionsPanel.IsEnabled = !working;
        ProjectActionsPanel.IsEnabled = !working;
        FooterText.Text = message;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private void ShowError(Exception ex)
    {
        var log = ErrorLog.Record(ex);
        MessageBox.Show(this, $"{ErrorLog.Summary(ex)}{Environment.NewLine}{Environment.NewLine}" +
            $"Detalles: {log}", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string KindLabel(AssetKind kind) => kind switch
    {
        AssetKind.CharacterSprite => "Render",
        AssetKind.Background => "Fondo",
        AssetKind.SoundEffect => "SFX",
        AssetKind.Music => "Música",
        AssetKind.Video => "Video",
        AssetKind.VisualEffect => "Efecto visual",
        AssetKind.Audio => "Audio",
        AssetKind.Meme => "Meme",
        AssetKind.Prop => "Prop",
        AssetKind.Font => "Fuente",
        AssetKind.Overlay => "Overlay",
        _ => "Sin definir"
    };

    private static string CutoutLabel(CutoutStatus status) => status switch
    {
        CutoutStatus.Ready => "Transparente",
        CutoutStatus.NeedsCutout => "Pendiente",
        CutoutStatus.IntentionallyOpaque => "Opaco intencional",
        CutoutStatus.NotApplicable => "—",
        _ => "Sin revisar"
    };

    protected override void OnClosed(EventArgs e)
    {
        _scenePreviewTimer.Stop();
        _scanCancellation?.Cancel();
        _voicePreviewCancellation?.Cancel();
        _sceneVoiceGenerationCancellation?.Cancel();
        _sceneRenderCancellation?.Cancel();
        ClearScenePreview();
        StopVoicePreview(deleteLastFile: true);
        _scriptAudioPlayer.Stop();
        _scriptAudioPlayer.Close();

        if (_currentRepository is not null)
            _currentRepository.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosed(e);
    }
}

public sealed record SourceRow(AssetSource Source, int AssetCount, int NeedsCutoutCount, int MissingCount)
{
    public string Name => Source.Name;
    public string RootPath => Source.RootPath;
    public string State => Directory.Exists(Source.RootPath) ? "Online" : "Offline";
    public string LastScanText => Source.LastScanUtc?.LocalDateTime.ToString("g") ?? "Nunca";
}

public sealed record AssetRow(
    string Name,
    string Kind,
    string Subject,
    string Collection,
    string Cutout,
    string SourceName,
    string RelativePath,
    string State,
    Guid AssetId,
    string InitialDescription,
    Guid? SourceId,
    AssetKind AssetKind)
{
    public string Description { get; set; } = InitialDescription;

    // Editable row: compare by reference so editing Description does not change the hash code
    // the DataGrid selection relies on (same bug class as the Director draft rows).
    public bool Equals(AssetRow? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
