using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Projects;
using Microsoft.Win32;

namespace LoquendoAI.App;

public partial class AssetSourceSetupWindow : Window
{
    private readonly Guid _sourceId;
    private readonly DateTimeOffset _createdUtc;

    public ObservableCollection<FolderRuleRow> Rules { get; } = new();

    public IReadOnlyList<EnumOption<FolderClassification>> ClassificationOptions { get; } =
    [
        new(FolderClassification.Auto, "Automático / sin definir"),
        new(FolderClassification.CharacterRenders, "Renders / personajes"),
        new(FolderClassification.Backgrounds, "Fondos"),
        new(FolderClassification.SoundEffects, "Efectos de sonido"),
        new(FolderClassification.Music, "Música"),
        new(FolderClassification.Videos, "Videos"),
        new(FolderClassification.VisualEffects, "Efectos visuales"),
        new(FolderClassification.Memes, "Memes / relleno"),
        new(FolderClassification.Props, "Props / objetos"),
        new(FolderClassification.Mixed, "Mixto (detectar por archivo)"),
        new(FolderClassification.Ignore, "Ignorar")
    ];

    public IReadOnlyList<EnumOption<CutoutStatus>> CutoutOptions { get; } =
    [
        new(CutoutStatus.Unknown, "Automático"),
        new(CutoutStatus.Ready, "Ya transparente"),
        new(CutoutStatus.NeedsCutout, "Quitar fondo después"),
        new(CutoutStatus.IntentionallyOpaque, "Opaco a propósito")
    ];

    public IReadOnlyList<EnumOption<FolderNameRole>> FolderNameRoleOptions { get; } =
    [
        new(FolderNameRole.Subject, "Nombre carpeta → personaje"),
        new(FolderNameRole.Collection, "Nombre carpeta → colección"),
        new(FolderNameRole.None, "No usar nombre de carpeta")
    ];

    public string RootPath { get; }
    public AssetSource? ResultSource { get; private set; }
    public IReadOnlyList<FolderRule>? ResultRules { get; private set; }

    public AssetSourceSetupWindow(string rootPath, AssetSource? existingSource = null, IReadOnlyList<FolderRule>? existingRules = null)
    {
        RootPath = Path.GetFullPath(rootPath);
        _sourceId = existingSource?.Id ?? Guid.NewGuid();
        _createdUtc = existingSource?.CreatedUtc ?? DateTimeOffset.UtcNow;

        InitializeComponent();
        DataContext = this;

        RootPathText.Text = RootPath;
        SourceNameBox.Text = existingSource?.Name ?? new DirectoryInfo(RootPath).Name;
        RootClassificationBox.SelectedValue = FolderClassification.Auto;
        RootCutoutBox.SelectedValue = CutoutStatus.Unknown;
        BulkClassificationBox.SelectedValue = FolderClassification.CharacterRenders;
        FolderNameRoleBox.SelectedValue = FolderNameRole.Subject;

        LoadRules(existingRules);
    }

    private void LoadRules(IReadOnlyList<FolderRule>? existingRules)
    {
        var byFolder = existingRules?.ToDictionary(
            r => AssetLibraryService.NormalizeRelativePath(r.RelativeFolder),
            StringComparer.OrdinalIgnoreCase);

        if (byFolder is not null && byFolder.TryGetValue(string.Empty, out var rootRule))
        {
            RootClassificationBox.SelectedValue = rootRule.Classification;
            RootSubjectBox.Text = rootRule.SubjectName ?? string.Empty;
            RootCollectionBox.Text = rootRule.CollectionName ?? string.Empty;
            RootCutoutBox.SelectedValue = rootRule.DefaultCutoutStatus;
        }

        foreach (var directory in Directory.EnumerateDirectories(RootPath)
                     .OrderBy(d => Path.GetFileName(d) ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
        {
            var relative = AssetLibraryService.NormalizeRelativePath(Path.GetRelativePath(RootPath, directory));
            if (byFolder is not null && byFolder.TryGetValue(relative, out var existing))
            {
                Rules.Add(FolderRuleRow.From(existing));
                continue;
            }

            var folderName = Path.GetFileName(directory) ?? string.Empty;
            var suggestion = FolderRuleSuggestions.Suggest(folderName);
            Rules.Add(new FolderRuleRow
            {
                RelativeFolder = relative,
                Classification = suggestion,
                CollectionName = suggestion == FolderClassification.CharacterRenders ? folderName : null,
                IncludeSubfolders = true,
                DefaultCutoutStatus = CutoutStatus.Unknown
            });
        }

        if (existingRules is null)
            return;

        var topLevel = new HashSet<string>(Rules.Select(r => r.RelativeFolder), StringComparer.OrdinalIgnoreCase);
        foreach (var rule in existingRules.Where(r => !string.IsNullOrEmpty(r.RelativeFolder)))
        {
            var normalized = AssetLibraryService.NormalizeRelativePath(rule.RelativeFolder);
            if (!topLevel.Contains(normalized))
                Rules.Add(FolderRuleRow.From(rule));
        }
    }

    private void ApplyBulk_Click(object sender, RoutedEventArgs e)
    {
        if (BulkClassificationBox.SelectedValue is not FolderClassification classification)
            return;

        foreach (var row in RulesGrid.SelectedItems.OfType<FolderRuleRow>())
        {
            row.Classification = classification;
            if (classification == FolderClassification.CharacterRenders)
                ApplyFolderNameRole(row);
        }
    }


    private void AutoRowsToRenders_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rules.Where(r => r.Classification == FolderClassification.Auto))
        {
            row.Classification = FolderClassification.CharacterRenders;
            ApplyFolderNameRole(row);
        }
    }


    private void ApplyFolderNameRole(FolderRuleRow row)
    {
        var folderName = Path.GetFileName(row.RelativeFolder.TrimEnd('/')) ?? row.RelativeFolder;
        var role = FolderNameRoleBox.SelectedValue is FolderNameRole selected ? selected : FolderNameRole.None;
        if (role == FolderNameRole.Subject && string.IsNullOrWhiteSpace(row.SubjectName))
            row.SubjectName = folderName;
        else if (role == FolderNameRole.Collection && string.IsNullOrWhiteSpace(row.CollectionName))
            row.CollectionName = folderName;
    }

    private void AddSpecificRule_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecciona una subcarpeta dentro de la fuente",
            InitialDirectory = RootPath
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var full = Path.GetFullPath(dialog.FolderName);
        var relative = AssetLibraryService.NormalizeRelativePath(Path.GetRelativePath(RootPath, full));
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            MessageBox.Show(this, "La regla debe apuntar a una carpeta dentro de la fuente.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Rules.Any(r => string.Equals(r.RelativeFolder, relative, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Esa carpeta ya tiene una regla visible.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Rules.Add(new FolderRuleRow
        {
            RelativeFolder = relative,
            Classification = FolderRuleSuggestions.Suggest(Path.GetFileName(full) ?? string.Empty),
            IncludeSubfolders = true,
            DefaultCutoutStatus = CutoutStatus.Unknown
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = SourceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Ponle un nombre a la fuente.", "Loquendo AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var source = new AssetSource(
            _sourceId,
            name,
            RootPath,
            true,
            _createdUtc,
            null);

        var rules = new List<FolderRule>();
        var rootClassification = RootClassificationBox.SelectedValue is FolderClassification root
            ? root
            : FolderClassification.Auto;

        var rootCutout = RootCutoutBox.SelectedValue is CutoutStatus rootCutoutStatus
            ? rootCutoutStatus
            : CutoutStatus.Unknown;

        rules.Add(new FolderRule(
            Guid.NewGuid(),
            _sourceId,
            string.Empty,
            rootClassification,
            Clean(RootSubjectBox.Text),
            Clean(RootCollectionBox.Text),
            true,
            rootCutout));

        rules.AddRange(Rules
            .Where(row => row.Classification != FolderClassification.Auto ||
                          !string.IsNullOrWhiteSpace(row.SubjectName) ||
                          !string.IsNullOrWhiteSpace(row.CollectionName) ||
                          !row.IncludeSubfolders ||
                          row.DefaultCutoutStatus != CutoutStatus.Unknown)
            .Select(row => new FolderRule(
                Guid.NewGuid(),
                _sourceId,
                AssetLibraryService.NormalizeRelativePath(row.RelativeFolder),
                row.Classification,
                Clean(row.SubjectName),
                Clean(row.CollectionName),
                row.IncludeSubfolders,
                row.DefaultCutoutStatus)));

        ResultSource = source;
        ResultRules = rules;
        DialogResult = true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum FolderNameRole
{
    None = 0,
    Subject,
    Collection
}

public sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;

public sealed class FolderRuleRow : INotifyPropertyChanged
{
    private FolderClassification _classification;
    private string? _subjectName;
    private string? _collectionName;
    private bool _includeSubfolders = true;
    private CutoutStatus _defaultCutoutStatus;

    public string RelativeFolder { get; init; } = string.Empty;

    public FolderClassification Classification
    {
        get => _classification;
        set => SetField(ref _classification, value);
    }

    public string? SubjectName
    {
        get => _subjectName;
        set => SetField(ref _subjectName, value);
    }

    public string? CollectionName
    {
        get => _collectionName;
        set => SetField(ref _collectionName, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetField(ref _includeSubfolders, value);
    }

    public CutoutStatus DefaultCutoutStatus
    {
        get => _defaultCutoutStatus;
        set => SetField(ref _defaultCutoutStatus, value);
    }

    public static FolderRuleRow From(FolderRule rule) => new()
    {
        RelativeFolder = rule.RelativeFolder,
        Classification = rule.Classification,
        SubjectName = rule.SubjectName,
        CollectionName = rule.CollectionName,
        IncludeSubfolders = rule.IncludeSubfolders,
        DefaultCutoutStatus = rule.DefaultCutoutStatus
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
