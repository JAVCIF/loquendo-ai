using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using Microsoft.Win32;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private const int AssetPageSize = 150;
    private readonly DispatcherTimer _assetSearchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private int _folderLoadSequence;
    private AssetRecord[] _activeAssetPage = [];
    private string? _activeAssetQueryKey;
    private bool _assetPageHasMore;
    private int _assetThumbnailVersion;

    private async void ScriptAssetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var request = ++_assetThumbnailVersion;
        // SelectionChanged may run while InitializeComponent is still assigning controls.
        if (ScriptBlockTypeCombo is null || ScriptAssetCombo is null ||
            SelectedAssetThumbnail is null || SelectedAssetThumbnailText is null) return;
        if (!_loadingScriptUi && _editingScriptBlockId is null && VideoGreenScreenCheck is not null &&
            ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice { Kind: ScriptBlockKind.Video } &&
            (ScriptAssetCombo.SelectedItem as AssetChoice)?.Id is Guid videoId &&
            _scriptAssetCache.TryGetValue(videoId, out var selectedVideo) &&
            SceneComposer.LooksLikeGreenScreen(selectedVideo.DisplayName))
            VideoGreenScreenCheck.IsChecked = true;
        SelectedAssetThumbnail.Source = null;
        SelectedAssetThumbnailText.Visibility = Visibility.Visible;
        SelectedAssetThumbnailText.Text = "Selecciona una imagen";
        if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice { Kind: ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image } ||
            (ScriptAssetCombo.SelectedItem as AssetChoice)?.Id is not Guid id || !_scriptAssetCache.TryGetValue(id, out var asset)) return;

        try
        {
            var path = await ResolveAssetPathAsync(asset);
            if (path is null || !File.Exists(path)) throw new FileNotFoundException("El asset ya no está disponible.", path);
            var thumbnail = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 180;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            });
            if (request != _assetThumbnailVersion || (ScriptAssetCombo.SelectedItem as AssetChoice)?.Id != id) return;
            SelectedAssetThumbnail.Source = thumbnail;
            SelectedAssetThumbnailText.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            if (request == _assetThumbnailVersion) SelectedAssetThumbnailText.Text = "Sin miniatura";
        }
    }

    private string? CurrentAssetQueryKey(ScriptBlockKind kind)
    {
        if (CharacterFolderCombo.SelectedItem is not CharacterFolderChoice folder || folder.SourceId == Guid.Empty ||
            RelevantAssetKinds(kind).Count == 0) return null;
        return $"{kind}|{FolderKey(folder, SpriteSubfoldersCheck.IsChecked == true)}|{SpriteNameSearchBox.Text.Trim().ToUpperInvariant()}";
    }

    private bool AssetBelongsToSelectedFolder(AssetRecord asset)
    {
        if (CharacterFolderCombo.SelectedItem is not CharacterFolderChoice folder || asset.SourceId != folder.SourceId ||
            asset.SourceRelativePath is not string path || asset.IsMissing) return false;
        var prefix = folder.RelativePath.Replace('\\', '/').Trim('/');
        if (prefix.Length > 0) prefix += "/";
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               (SpriteSubfoldersCheck.IsChecked == true || !normalized[prefix.Length..].Contains('/'));
    }

    // Resource folders usually contain their files in nested directories. Showing the
    // first page recursively makes source roots useful without changing sprite browsing.
    private void PreferResourceSubfolders(ScriptBlockKind kind)
    {
        if (kind == ScriptBlockKind.CharacterShow || RelevantAssetKinds(kind).Count == 0) return;
        var wasLoading = _loadingScriptUi;
        _loadingScriptUi = true;
        try { SpriteSubfoldersCheck.IsChecked = true; }
        finally { _loadingScriptUi = wasLoading; }
    }

    private async void BrowseCatalogAsset_Click(object sender, RoutedEventArgs e)
    {
        var repository = _currentRepository;
        if (repository is null || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice ||
            RelevantAssetKinds(choice.Kind).Count == 0) return;
        var kind = choice.Kind;
        BrowseCatalogAssetButton.IsEnabled = false;
        try
        {
            IReadOnlyList<AssetSource> sources;
            await _scriptAssetLoadLock.WaitAsync();
            try { sources = await repository.GetAssetSourcesAsync(); }
            finally { _scriptAssetLoadLock.Release(); }
            if (repository != _currentRepository || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice current || current.Kind != kind) return;

            var active = CharacterFolderCombo.SelectedItem as CharacterFolderChoice;
            var selectedSource = sources.FirstOrDefault(x => x.Id == active?.SourceId);
            var startingFolder = selectedSource is null ? null : Path.Combine(selectedSource.RootPath,
                active!.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (startingFolder is null || !Directory.Exists(startingFolder))
                startingFolder = sources.FirstOrDefault(x => Directory.Exists(x.RootPath))?.RootPath;
            var dialog = new OpenFileDialog
            {
                Title = "Selecciona un archivo catalogado",
                InitialDirectory = startingFolder ?? "",
                Filter = kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music
                    ? "Audio|*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aac;*.wma;*.opus|Todos los archivos|*.*"
                    : kind == ScriptBlockKind.Video
                        ? "Video|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.mpg;*.mpeg;*.m4v;*.ts;*.m2ts|Todos los archivos|*.*"
                        : "Imágenes|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff|Todos los archivos|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (repository != _currentRepository || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice activeKind || activeKind.Kind != kind) return;

            var selectedPath = Path.GetFullPath(dialog.FileName);
            var source = sources.Where(x => Directory.Exists(x.RootPath))
                .OrderByDescending(x => x.RootPath.Length)
                .FirstOrDefault(x => selectedPath.StartsWith(Path.GetFullPath(x.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                MessageBox.Show(this, "El archivo debe estar dentro de una fuente agregada en Biblioteca de assets.",
                    "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var relative = Path.GetRelativePath(source.RootPath, selectedPath).Replace('\\', '/');
            var asset = await Task.Run(() => repository.GetAssetBySourcePathAsync(source.Id, relative));
            if (repository != _currentRepository || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice selectedKind || selectedKind.Kind != kind) return;
            if (asset is null)
            {
                MessageBox.Show(this, "El archivo todavía no figura en el catálogo. Escanea su fuente en Biblioteca y vuelve a intentarlo.",
                    "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (SceneComposer.IsVisualBlock(kind) ? !DirectorAssetCompatible(kind, asset) : !RelevantAssetKinds(kind).Contains(asset.Kind))
            {
                MessageBox.Show(this, $"'{asset.DisplayName}' figura como {KindLabel(asset.Kind)} en Biblioteca y su formato no sirve para este tipo de bloque.",
                    "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var slash = relative.LastIndexOf('/');
            var folder = slash < 0 ? "" : relative[..slash];
            var selectedFolder = new CharacterFolderChoice(source.Id, folder, $"{source.Name}/{folder}");
            var wasLoading = _loadingScriptUi;
            _loadingScriptUi = true;
            try
            {
                SpriteSubfoldersCheck.IsChecked = false;
                SpriteNameSearchBox.Text = string.Empty;
            }
            finally { _loadingScriptUi = wasLoading; }
            _assetSearchTimer.Stop();
            await LoadAssetFoldersForKindAsync(kind, selectedFolder);
            if (CharacterFolderCombo.SelectedItem is not CharacterFolderChoice currentFolder || !SameFolder(currentFolder, selectedFolder)) return;
            await EnsureAssetsForKindAsync(kind);
            if (repository != _currentRepository || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice finalKind || finalKind.Kind != kind ||
                CharacterFolderCombo.SelectedItem is not CharacterFolderChoice finalFolder || !SameFolder(finalFolder, selectedFolder)) return;
            _scriptAssetCache[asset.Id] = asset;
            RefreshAssetChoices(kind, asset.Id);
            UpdateScriptBlockEditorState();
            ScriptStatusText.Text = $"Archivo catalogado seleccionado: {asset.DisplayName}. Guarda el bloque para aplicarlo.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { BrowseCatalogAssetButton.IsEnabled = ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice activeChoice && RelevantAssetKinds(activeChoice.Kind).Count > 0; }
    }

    private async Task LoadAssetFoldersForKindAsync(ScriptBlockKind kind, CharacterFolderChoice? preferred)
    {
        if (kind == ScriptBlockKind.CharacterShow)
            await LoadSpriteFoldersAsync((ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id, preferred);
        else if (RelevantAssetKinds(kind).Count > 0)
            await LoadResourceFoldersAsync(kind, preferred);
        else
            ClearSpriteFolderSelection();
    }

    private async Task LoadResourceFoldersAsync(ScriptBlockKind kind, CharacterFolderChoice? preferred)
    {
        var repository = _currentRepository;
        if (repository is null) return;
        var request = ++_folderLoadSequence;
        var revision = _scriptAssetCacheRevision;
        var kinds = RelevantAssetKinds(kind);
        IReadOnlyList<Guid> sourceIds;
        IReadOnlyList<AssetSource> sources;
        IReadOnlyList<string> children = [];
        await _scriptAssetLoadLock.WaitAsync();
        try
        {
            sourceIds = await Task.Run(() => repository.GetAssetSourceIdsByKindsAsync(kinds));
            sources = await repository.GetAssetSourcesAsync();
            if (preferred is { SourceId: var source } && source != Guid.Empty)
            {
                var parent = new AssetFolder(source, preferred.RelativePath);
                children = await Task.Run(() => repository.GetAssetSubfoldersAsync(kinds, parent));
            }
        }
        finally { _scriptAssetLoadLock.Release(); }
        if (request != _folderLoadSequence || revision != _scriptAssetCacheRevision || repository != _currentRepository ||
            ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || choice.Kind != kind) return;

        var labels = sources.ToDictionary(x => x.Id, x => x.Name);
        var options = new List<CharacterFolderChoice> { new(Guid.Empty, "", "— Selecciona una carpeta —") };
        options.AddRange(sourceIds.Where(labels.ContainsKey).Select(id => new CharacterFolderChoice(id, "", labels[id] + "/")));
        if (preferred is CharacterFolderChoice selectedFolder && selectedFolder.SourceId != Guid.Empty)
        {
            var preferredId = selectedFolder.SourceId;
            var sourceName = labels.GetValueOrDefault(preferredId, "Fuente");
            if (!options.Any(x => SameFolder(x, selectedFolder)))
                options.Add(new CharacterFolderChoice(preferredId, selectedFolder.RelativePath,
                    $"{sourceName}/{selectedFolder.RelativePath}"));
            options.AddRange(children.Select(child =>
            {
                var childPath = selectedFolder.RelativePath.Length == 0 ? child : selectedFolder.RelativePath.TrimEnd('/') + "/" + child;
                return new CharacterFolderChoice(preferredId, childPath, $"{sourceName}/{childPath}");
            }));
        }
        var selected = options[0];
        if (preferred is not null)
            selected = options.FirstOrDefault(x => SameFolder(x, preferred)) ?? options[0];
        var wasLoading = _loadingScriptUi;
        _loadingScriptUi = true;
        try
        {
            CharacterFolderCombo.ItemsSource = options;
            CharacterFolderCombo.SelectedItem = selected;
        }
        finally { _loadingScriptUi = wasLoading; }
    }

    private async void AssetFolderUp_Click(object sender, RoutedEventArgs e)
    {
        if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || choice.Kind == ScriptBlockKind.CharacterShow ||
            CharacterFolderCombo.SelectedItem is not CharacterFolderChoice { SourceId: var id } folder || id == Guid.Empty) return;
        var path = folder.RelativePath.Trim('/');
        var slash = path.LastIndexOf('/');
        CharacterFolderChoice? parent = path.Length == 0 ? null : new(id,
            slash >= 0 ? path[..slash] : "", "");
        try
        {
            if (parent is not null) PreferResourceSubfolders(choice.Kind);
            await LoadResourceFoldersAsync(choice.Kind, parent);
            await ReloadSelectedSpriteFolderAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void AssetSearchTimer_Tick(object? sender, EventArgs e)
    {
        _assetSearchTimer.Stop();
        _ = ReloadFilteredAssetsSafelyAsync();
    }

    private async Task ReloadFilteredAssetsSafelyAsync()
    {
        try { await ReloadSelectedSpriteFolderAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private string SerializeResourceParameters(ScriptBlockKind kind)
    {
        var folder = CharacterFolderCombo.SelectedItem as CharacterFolderChoice;
        var video = kind == ScriptBlockKind.Video;
        var audio = kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music;
        return (EditorVisualParameters(kind) with
        {
            WaitForEnd = kind == ScriptBlockKind.SoundEffect ? SfxWaitCheck.IsChecked == true : null,
            Position = (CharacterPositionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "centro",
            AutoTrimBorders = kind == ScriptBlockKind.Background ? BackgroundTrimBordersCheck.IsChecked == true : null,
            FolderSourceId = folder?.SourceId,
            FolderRelativePath = folder?.RelativePath,
            IncludeSubfolders = SpriteSubfoldersCheck.IsChecked == true,
            VisualDurationMs = kind is ScriptBlockKind.Background or ScriptBlockKind.Image or ScriptBlockKind.Video &&
                long.TryParse(VisualDurationBox.Text.Trim(), out var duration) ? duration : null,
            AudioDurationMs = audio && long.TryParse(VisualDurationBox.Text.Trim(), out var audioDuration) ? audioDuration : null,
            AudioDurationMode = audio ? AudioDurationModeCombo.SelectedIndex == 1 ? "tempo" : "loop" : null,
            VolumePercent = (audio || video) && int.TryParse(ResourceVolumeBox.Text.Trim(), out var volume) ? volume : null,
            VideoLayer = video ? VideoLayerCombo.SelectedIndex switch { 0 => "fondo-fijo", 2 => "sobre", _ => "guion" } : null,
            GreenScreen = video ? VideoGreenScreenCheck.IsChecked == true : null,
            GreenScreenMode = video ? VideoGreenScreenCheck.IsChecked == true ? "on" : "off" : null,
            KeyColor = video ? VideoKeyColorBox.Text.Trim().TrimStart('#').ToUpperInvariant() : null,
            KeyTolerance = video ? double.TryParse(VideoKeyToleranceBox.Text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var tolerance) ? tolerance : BlockDefaults.KeyTolerance : null
        }).ToJson();
    }
}
