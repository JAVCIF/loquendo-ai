using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

/// <summary>
/// Chooses a catalogued resource from the project database instead of the Windows file
/// dialog: results are filtered by what the draft row can play, show the catalogue category
/// and AI/manual tags, and can be previewed (thumbnail or audio) before choosing.
/// </summary>
internal sealed class AssetPickerWindow : Window
{
    internal sealed record PickerRow(AssetPickerItem Item, string KindLabel, string Folder)
    {
        public string Name => Item.Asset.DisplayName + Item.Asset.Extension;
        public string Kind => KindLabel;
        public string Subject => string.IsNullOrWhiteSpace(Item.Asset.SubjectName) ? Item.Subject : Item.Asset.SubjectName!;
        public string Description => Item.Description;
        public string Mood => Item.Mood;
        public string Path => Folder;
        public string Duration { get; init; } = "";
    }

    private readonly SqliteProjectRepository _repository;
    private readonly IReadOnlyCollection<string> _extensions;
    private readonly IReadOnlyCollection<AssetKind> _kinds;
    private readonly IReadOnlyList<AssetSource> _sources;
    private readonly Func<AssetRecord, Task<string?>> _resolvePath;
    private readonly bool _audio;
    private readonly bool _image;
    private readonly TextBox _search = new() { MinWidth = 260, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _source = new() { Width = 190, Margin = new Thickness(6, 0, 6, 0) };
    private readonly CheckBox _onlyKind = new() { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly CheckBox _onlyCharacter = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
        CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 8, 0, 8)
    };
    private readonly Image _thumbnail = new() { Width = 220, Height = 160, Stretch = Stretch.Uniform };
    private readonly Button _play = new() { Content = "▶ Escuchar", Padding = new Thickness(10, 4, 10, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _choose = new() { Content = "Usar este recurso", Padding = new Thickness(12, 5, 12, 5), IsDefault = true, IsEnabled = false };
    private readonly MediaPlayer _player = new();
    private readonly string? _characterName;
    private readonly ScriptBlockKind? _audioUse;
    private CancellationTokenSource? _query;
    private int _previewVersion;
    private bool _playing;

    public AssetRecord? SelectedAsset { get; private set; }

    public AssetPickerWindow(SqliteProjectRepository repository, string kindLabel, IReadOnlyCollection<string> extensions,
        IReadOnlyCollection<AssetKind> kinds, IReadOnlyList<AssetSource> sources, Func<AssetRecord, Task<string?>> resolvePath,
        string? characterName = null, string initialSearch = "", bool audio = false, bool image = false,
        ScriptBlockKind? audioUse = null)
    {
        _audioUse = audioUse;
        _repository = repository;
        _extensions = extensions;
        _kinds = kinds;
        _sources = sources;
        _resolvePath = resolvePath;
        _characterName = string.IsNullOrWhiteSpace(characterName) ? null : characterName.Trim();
        _audio = audio;
        _image = image;
        Title = "Elegir recurso catalogado · " + kindLabel;
        Width = 1040; Height = 640; MinWidth = 760; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var layout = new Grid { Margin = new Thickness(14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var filters = new WrapPanel();
        filters.Children.Add(new TextBlock { Text = "Buscar (nombre, carpeta o etiquetas):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _search.Text = initialSearch;
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunQueryAsync(); } };
        filters.Children.Add(_search);
        var searchButton = new Button { Content = "Buscar", Padding = new Thickness(10, 3, 10, 3) };
        searchButton.Click += async (_, _) => await RunQueryAsync();
        filters.Children.Add(searchButton);
        _source.ItemsSource = new[] { "Todas las fuentes" }.Concat(sources.Select(x => x.Name)).ToArray();
        _source.SelectedIndex = 0;
        _source.SelectionChanged += async (_, _) => { if (IsLoaded) await RunQueryAsync(); };
        filters.Children.Add(_source);
        _onlyKind.Content = "Solo catalogados como " + kindLabel.ToLowerInvariant();
        _onlyKind.ToolTip = "Desmarca para ver todos los archivos del formato adecuado, aunque su tipo en la biblioteca sea otro o esté sin clasificar.";
        _onlyKind.Click += async (_, _) => await RunQueryAsync();
        filters.Children.Add(_onlyKind);
        if (_characterName is not null)
        {
            _onlyCharacter.Content = "Solo de " + _characterName;
            _onlyCharacter.IsChecked = true;
            _onlyCharacter.ToolTip = "Por sujeto, etiqueta o carpeta con el nombre exacto del personaje.";
            _onlyCharacter.Click += async (_, _) => await RunQueryAsync();
            filters.Children.Add(_onlyCharacter);
        }
        layout.Children.Add(filters);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddColumn("Nombre", nameof(PickerRow.Name), 190);
        AddColumn("Tipo", nameof(PickerRow.Kind), 90);
        AddColumn("Sujeto", nameof(PickerRow.Subject), 100);
        AddColumn("Descripción", nameof(PickerRow.Description), 250);
        if (audio) AddColumn("Duración ≈", nameof(PickerRow.Duration), 80);
        else AddColumn("Ánimo / expresión", nameof(PickerRow.Mood), 120);
        AddColumn("Carpeta", nameof(PickerRow.Path), 220);
        _grid.SelectionChanged += async (_, _) => await ShowPreviewAsync();
        _grid.MouseDoubleClick += (_, _) => Choose();
        body.Children.Add(_grid);

        var preview = new StackPanel { Margin = new Thickness(10, 8, 0, 8), Width = 230 };
        preview.Children.Add(ThemeManager.Themed(new Border
        {
            BorderThickness = new Thickness(1), Height = 164,
            Child = _thumbnail, Visibility = image ? Visibility.Visible : Visibility.Collapsed
        }, Border.BorderBrushProperty, "Theme.Border"));
        _play.Click += (_, _) => TogglePlayback();
        preview.Children.Add(_play);
        Grid.SetColumn(preview, 1);
        body.Children.Add(preview);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var footer = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        _choose.Click += (_, _) => Choose();
        buttons.Children.Add(_choose);
        buttons.Children.Add(cancel);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        Content = layout;

        Loaded += async (_, _) => { _search.Focus(); await RunQueryAsync(); };
        Closed += (_, _) =>
        {
            _query?.Cancel();
            try { _player.Stop(); _player.Close(); } catch (Exception) { }
        };
    }

    private void AddColumn(string header, string path, double width) =>
        _grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(width) });

    private async Task RunQueryAsync()
    {
        _query?.Cancel();
        var cancellation = new CancellationTokenSource();
        _query = cancellation;
        var sourceId = _source.SelectedIndex > 0 ? _sources[_source.SelectedIndex - 1].Id : (Guid?)null;
        var query = new AssetPickerQuery(_extensions, _onlyKind.IsChecked == true ? _kinds : null, _search.Text,
            sourceId, _onlyCharacter.IsChecked == true ? _characterName : null, 400);
        _status.Text = "Buscando en la biblioteca…";
        try
        {
            var items = await Task.Run(() => _repository.SearchAssetsForPickerAsync(query, cancellation.Token), cancellation.Token);
            if (!ReferenceEquals(_query, cancellation)) return;
            var sourceNames = _sources.ToDictionary(x => x.Id, x => x.Name);
            _grid.ItemsSource = items.Select(item => new PickerRow(item, KindLabel(item.Asset.Kind),
                FolderLabel(item.Asset, sourceNames))
            {
                Duration = _audio && item.Asset.FileSize > 0
                    ? "≈" + AudioDurationEstimate.Label(AudioDurationEstimate.Seconds(item.Asset).Typical) : ""
            }).ToArray();
            _status.Text = items.Count == 0
                ? _onlyKind.IsChecked == true
                    ? "Sin resultados con este tipo. Desmarca «Solo catalogados como…» para ver todos los archivos compatibles."
                    : "Sin resultados. Prueba otra palabra o revisa que la fuente esté escaneada."
                : $"{items.Count:N0} resultado(s){(items.Count >= 400 ? " (máximo 400: afina la búsqueda)" : "")}. Doble clic para elegir.";
            if (items.Count > 0) _grid.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private async Task ShowPreviewAsync()
    {
        StopPlayback();
        var version = ++_previewVersion;
        _thumbnail.Source = null;
        var row = _grid.SelectedItem as PickerRow;
        _choose.IsEnabled = row is not null;
        _play.Visibility = row is not null && _audio ? Visibility.Visible : Visibility.Collapsed;
        if (row is not null && _audio) { await ShowAudioLengthAsync(row, version); return; }
        if (row is null || !_image) return;
        try
        {
            var path = await _resolvePath(row.Item.Asset);
            if (path is null || !File.Exists(path) || version != _previewVersion) return;
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 220;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            });
            if (version == _previewVersion) _thumbnail.Source = bitmap;
        }
        catch (Exception) { /* A thumbnail is optional. */ }
    }

    /// <summary>Exact length of the selected sound, with a warning when it does not fit the row
    /// (a long track as SFX or a blip as music). The choice stays with the user.</summary>
    private async Task ShowAudioLengthAsync(PickerRow row, int version)
    {
        try
        {
            var path = await _resolvePath(row.Item.Asset);
            if (path is null || !File.Exists(path) || version != _previewVersion) return;
            var ms = await LoquendoAI.Infrastructure.Composition.SceneComposer.ProbeDurationAsync(path);
            if (version != _previewVersion) return;
            var text = "Duración: " + AudioDurationEstimate.Label(ms / 1000d);
            if (_audioUse == ScriptBlockKind.SoundEffect && ms > 20_000)
                text += " — ojo: parece música o ambiente, no un efecto.";
            else if (_audioUse == ScriptBlockKind.Music && ms < 8_000)
                text += " — ojo: es muy corto para música de fondo.";
            _status.Text = text;
        }
        catch (Exception) { /* ffprobe missing or unreadable file: keep the estimate column. */ }
    }

    private async void TogglePlayback()
    {
        if (_playing) { StopPlayback(); return; }
        if (_grid.SelectedItem is not PickerRow row) return;
        try
        {
            var path = await _resolvePath(row.Item.Asset);
            if (path is null || !File.Exists(path)) { _status.Text = "El archivo ya no está disponible."; return; }
            _player.Open(new Uri(path));
            _player.Play();
            _playing = true;
            _play.Content = "■ Detener";
        }
        catch (Exception ex) { _status.Text = "No se pudo reproducir: " + ex.Message; }
    }

    private void StopPlayback()
    {
        if (!_playing) return;
        try { _player.Stop(); _player.Close(); } catch (Exception) { }
        _playing = false;
        _play.Content = "▶ Escuchar";
    }

    private void Choose()
    {
        if (_grid.SelectedItem is not PickerRow row) return;
        SelectedAsset = row.Item.Asset;
        DialogResult = true;
    }

    private static string FolderLabel(AssetRecord asset, IReadOnlyDictionary<Guid, string> sources)
    {
        var relative = (asset.SourceRelativePath ?? asset.RelativePath).Replace('\\', '/');
        var folder = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
        var source = asset.SourceId is Guid id && sources.TryGetValue(id, out var name) ? name : "";
        return source.Length == 0 ? folder : folder.Length == 0 ? source : source + "/" + folder;
    }

    internal static string KindLabel(AssetKind kind) => kind switch
    {
        AssetKind.CharacterSprite => "Render",
        AssetKind.Background => "Fondo",
        AssetKind.Prop => "Prop",
        AssetKind.Meme => "Meme",
        AssetKind.SoundEffect => "SFX",
        AssetKind.Music => "Música",
        AssetKind.Audio => "Audio",
        AssetKind.Video => "Vídeo",
        AssetKind.VisualEffect => "Efecto visual",
        AssetKind.Overlay => "Overlay",
        AssetKind.Font => "Fuente",
        _ => "Sin tipo"
    };
}
