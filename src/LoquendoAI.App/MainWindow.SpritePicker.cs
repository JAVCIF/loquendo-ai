using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using Microsoft.Win32;

namespace LoquendoAI.App;

public sealed record CharacterFolderChoice(Guid SourceId, string RelativePath, string Name);

public partial class MainWindow
{
    private readonly Dictionary<Guid, CharacterFolderChoice[]> _folderChoicesByCharacter = new();

    private static string FolderKey(CharacterFolderChoice folder, bool recursive)
        => $"{folder.SourceId:D}|{folder.RelativePath.Trim('/').ToUpperInvariant()}|{recursive}";

    private static bool SameFolder(CharacterFolderChoice a, CharacterFolderChoice b)
        => a.SourceId == b.SourceId && string.Equals(a.RelativePath.Trim('/'), b.RelativePath.Trim('/'), StringComparison.OrdinalIgnoreCase);

    private void ClearSpriteFolderSelection()
    {
        var loading = _loadingScriptUi;
        _loadingScriptUi = true;
        try
        {
            CharacterFolderCombo.ItemsSource = new[] { new CharacterFolderChoice(Guid.Empty, "", "— Selecciona una carpeta —") };
            CharacterFolderCombo.SelectedIndex = 0;
        }
        finally { _loadingScriptUi = loading; }
    }

    private async Task LoadSpriteFoldersAsync(Guid? characterId, CharacterFolderChoice? preferred)
    {
        if (_currentRepository is null) return;
        var repository = _currentRepository;
        var request = ++_folderLoadSequence;
        var revision = _scriptAssetCacheRevision;
        CharacterFolderChoice[] folders = [];
        if (characterId is Guid id)
        {
            if (_folderChoicesByCharacter.TryGetValue(id, out var cachedFolders))
                folders = cachedFolders;
            else
            {
                var characterName = _characters.FirstOrDefault(x => x.Id == id)?.Name;
                if (characterName is not null)
                {
                    IReadOnlyList<AssetFolder> found;
                    Dictionary<Guid, string> sources;
                    await _scriptAssetLoadLock.WaitAsync();
                    try
                    {
                        found = await Task.Run(() => repository.GetSpriteFoldersForCharacterAsync(characterName));
                        sources = (await repository.GetAssetSourcesAsync()).ToDictionary(x => x.Id, x => x.Name);
                    }
                    finally { _scriptAssetLoadLock.Release(); }
                    folders = found.Select(x => new CharacterFolderChoice(x.SourceId, x.RelativePath,
                        $"{sources.GetValueOrDefault(x.SourceId, "Fuente")}/{x.RelativePath}")).ToArray();
                    if (repository != _currentRepository || revision != _scriptAssetCacheRevision) return;
                    _folderChoicesByCharacter[id] = folders;
                }
            }
        }
        if (request != _folderLoadSequence || repository != _currentRepository || revision != _scriptAssetCacheRevision ||
            (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id != characterId ||
            ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice { Kind: ScriptBlockKind.CharacterShow }) return;
        var options = new List<CharacterFolderChoice> { new(Guid.Empty, "", "— Selecciona una carpeta —") };
        options.AddRange(folders);
        if (preferred is not null && !options.Any(x => SameFolder(x, preferred))) options.Add(preferred);
        var selection = preferred is null ? options.Skip(1).FirstOrDefault() : options.FirstOrDefault(x => SameFolder(x, preferred));
        var prior = _loadingScriptUi;
        _loadingScriptUi = true;
        try
        {
            CharacterFolderCombo.ItemsSource = options;
            CharacterFolderCombo.SelectedItem = selection ?? options[0];
        }
        finally { _loadingScriptUi = prior; }
        if (characterId is not null && folders.Length > 1 && preferred is null)
            ScriptStatusText.Text = "Hay varias carpetas de ese personaje. Elige la correcta antes de guardar.";
    }

    private static CharacterFolderChoice? StoredSpriteFolder(SceneScriptBlock block, IReadOnlyDictionary<Guid, AssetRecord> assets)
    {
        var parameters = BlockParameters.Of(block);
        if (parameters.FolderSourceId is Guid sourceId && parameters.FolderRelativePath is string folder)
            return new CharacterFolderChoice(sourceId, folder, folder);
        if (block.AssetId is not Guid id || !assets.TryGetValue(id, out var asset) || asset.SourceId is not Guid source ||
            asset.SourceRelativePath is not string path) return null;
        path = path.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var parent = slash < 0 ? "" : path[..slash];
        return new CharacterFolderChoice(source, parent, parent);
    }

    private static bool StoredSpriteSubfolders(SceneScriptBlock block) => BlockParameters.Of(block).IncludeSubfolders == true;

    private string SerializeSpriteParameters()
    {
        var folder = CharacterFolderCombo.SelectedItem as CharacterFolderChoice;
        return (EditorVisualParameters(ScriptBlockKind.CharacterShow) with
        {
            Position = (CharacterPositionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "centro",
            FramingPreset = CharacterFramingCombo.SelectedIndex switch
            {
                1 => "auto", 2 => "entero", 3 => "medio", 4 => "detalle", _ => "original"
            },
            FolderSourceId = folder?.SourceId,
            FolderRelativePath = folder?.RelativePath,
            IncludeSubfolders = SpriteSubfoldersCheck.IsChecked == true,
            VisualDurationMs = long.TryParse(VisualDurationBox.Text.Trim(), out var duration) ? duration : null,
            Scale = EditorRenderScale() is double scale && scale != 1 ? scale : null
        }).ToJson();
    }

    /// <summary>Size, offset, flip, rotation, motion and entry transition of the visual in the editor.
    /// Empty or invalid boxes fall back to the block type's defaults.</summary>
    private BlockParameters EditorVisualParameters(ScriptBlockKind kind)
    {
        var box = BlockDefaults.VisualBox(kind);
        static int Int(TextBox text, int fallback) => int.TryParse(text.Text.Trim(), out var value) ? value : fallback;
        static double Real(TextBox text) => double.TryParse(text.Text.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        var visual = BlockParameters.IsVisual(kind);
        var plugin = SelectedVegasPlugin();
        return new BlockParameters
        {
            VisualMaxWidth = Int(VisualWidthBox, box.Width),
            VisualMaxHeight = Int(VisualHeightBox, box.Height),
            VisualOffsetX = Int(VisualOffsetXBox, 0),
            VisualOffsetY = Int(VisualOffsetYBox, 0),
            FlipHorizontal = visual && VisualFlipHorizontalCheck.IsChecked == true,
            FlipVertical = visual && VisualFlipVerticalCheck.IsChecked == true,
            ChangeDirection = visual && VisualChangeDirectionCheck.IsChecked == true,
            RotationDegrees = Real(VisualRotationBox),
            MotionOffsetX = Int(MotionOffsetXBox, 0),
            MotionOffsetY = Int(MotionOffsetYBox, 0),
            MotionRotationDegrees = Real(MotionRotationBox),
            MotionDurationMs = long.TryParse(MotionDurationBox.Text.Trim(), out var motionMs) ? motionMs : 0,
            TransitionOverride = SelectedVisualTransitionMode(),
            VegasPluginId = plugin.Id.Length > 0 ? plugin.Id : null,
            VegasPluginPreset = plugin.Preset.Length > 0 ? plugin.Preset : null
        };
    }

    private async void BrowseSpriteFolder_Click(object sender, RoutedEventArgs e)
    {
        var repository = _currentRepository;
        if (repository is null) return;
        try
        {
            IReadOnlyList<AssetSource> sources;
            await _scriptAssetLoadLock.WaitAsync();
            try { sources = await repository.GetAssetSourcesAsync(); }
            finally { _scriptAssetLoadLock.Release(); }
            if (repository != _currentRepository) return;
            var active = CharacterFolderCombo.SelectedItem as CharacterFolderChoice;
            var currentSource = sources.FirstOrDefault(x => x.Id == active?.SourceId);
            var initial = currentSource is null ? sources.FirstOrDefault(x => Directory.Exists(x.RootPath))?.RootPath
                : Path.Combine(currentSource.RootPath, active!.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dialog = new OpenFolderDialog { Title = "Carpeta de assets catalogados", InitialDirectory = initial ?? "" };
            if (dialog.ShowDialog(this) != true) return;
            if (repository != _currentRepository) return;
            var selected = Path.GetFullPath(dialog.FolderName);
            var source = sources.Where(x => Directory.Exists(x.RootPath))
                .OrderByDescending(x => x.RootPath.Length)
                .FirstOrDefault(x => selected.Equals(Path.GetFullPath(x.RootPath), StringComparison.OrdinalIgnoreCase) ||
                    selected.StartsWith(Path.GetFullPath(x.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                MessageBox.Show(this, "La carpeta debe estar dentro de una fuente catalogada. Agrégala primero en Biblioteca de assets.",
                    "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var relative = Path.GetRelativePath(source.RootPath, selected).Replace('\\', '/');
            if (relative == ".") relative = "";
            var choice = new CharacterFolderChoice(source.Id, relative, $"{source.Name}/{relative}");
            if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice kind) return;
            PreferResourceSubfolders(kind.Kind);
            await LoadAssetFoldersForKindAsync(kind.Kind, choice);
            await ReloadSelectedSpriteFolderAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void CharacterFolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingScriptUi || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || RelevantAssetKinds(choice.Kind).Count == 0) return;
        try
        {
            if (CharacterFolderCombo.SelectedItem is CharacterFolderChoice { SourceId: var sourceId } && sourceId != Guid.Empty)
                PreferResourceSubfolders(choice.Kind);
            RefreshAssetChoices(choice.Kind, null);
            UpdateScriptBlockEditorState();
            if (choice.Kind != ScriptBlockKind.CharacterShow && CharacterFolderCombo.SelectedItem is CharacterFolderChoice { SourceId: var id } folder && id != Guid.Empty)
                await LoadResourceFoldersAsync(choice.Kind, folder);
            await ReloadSelectedSpriteFolderAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void SpriteSubfoldersCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingScriptUi || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || RelevantAssetKinds(choice.Kind).Count == 0) return;
        try { await ReloadSelectedSpriteFolderAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task ReloadSelectedSpriteFolderAsync()
    {
        if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || RelevantAssetKinds(choice.Kind).Count == 0) return;
        var kind = choice.Kind;
        var selected = CharacterFolderCombo.SelectedItem as CharacterFolderChoice;
        var recursive = SpriteSubfoldersCheck.IsChecked == true;
        var search = SpriteNameSearchBox.Text;
        RefreshAssetChoices(kind, null);
        await EnsureAssetsForKindAsync(kind);
        if (CharacterFolderCombo.SelectedItem is CharacterFolderChoice current && selected is not null &&
            SameFolder(current, selected) && (SpriteSubfoldersCheck.IsChecked == true) == recursive &&
            SpriteNameSearchBox.Text == search && ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice active && active.Kind == kind)
        {
            RefreshAssetChoices(kind, null);
            UpdateScriptBlockEditorState();
        }
    }

    private void SpriteNameSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingScriptUi || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || RelevantAssetKinds(choice.Kind).Count == 0) return;
        _assetSearchTimer.Stop();
        RefreshAssetChoices(choice.Kind, null);
        _assetSearchTimer.Start();
    }
}
