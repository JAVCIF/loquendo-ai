using System.IO;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Projects;
using Microsoft.Win32;

namespace LoquendoAI.App;

/// <summary>
/// Sharing the library between projects (1.4.8): «Importar de otro proyecto» (library, voice profiles and characters,
/// once) and the «biblioteca principal» (one project whose library others follow: they receive its new sources and
/// assets every time they are opened). See LibraryTransfer for the rules of the merge.
/// </summary>
public partial class MainWindow
{
    private void UpdateMainLibraryPanel()
    {
        if (_currentRepository is not { } repository) return;
        var settings = MainLibrarySettings.Load();
        var id = repository.Manifest.Id;
        var isMain = settings.ProjectId == id;
        var exists = settings.Project is { } path && Directory.Exists(path);
        MainLibraryText.Text = settings.Project is null ? "Sin biblioteca principal."
            : isMain ? "Este proyecto es la biblioteca principal."
            : $"Biblioteca principal: «{settings.ProjectName}»" + (exists ? "" : " (no se encuentra su carpeta)");
        MainLibraryText.ToolTip = settings.Project;
        FollowMainLibraryCheck.Visibility = settings.Project is not null && !isMain ? Visibility.Visible : Visibility.Collapsed;
        FollowMainLibraryCheck.IsChecked = settings.Follows(id);
        SyncMainLibraryButton.Visibility = settings.Follows(id) ? Visibility.Visible : Visibility.Collapsed;
        SetMainLibraryButton.Visibility = isMain ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ImportFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository) return;
        var folder = new OpenFolderDialog { Title = "Carpeta del proyecto del que traer la biblioteca" };
        if (folder.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetFullPath(folder.FolderName).TrimEnd('\\'), repository.ProjectRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Ese es el proyecto abierto. Elige otro.", "Importar de otro proyecto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!File.Exists(Path.Combine(folder.FolderName, ProjectLayout.DatabaseFileName)))
        {
            MessageBox.Show(this, "Esa carpeta no es un proyecto de Loquendo AI (no tiene project.db).", "Importar de otro proyecto",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var choice = new ImportProjectWindow(Path.GetFileName(folder.FolderName.TrimEnd('\\'))) { Owner = this };
        if (choice.ShowDialog() != true) return;
        try
        {
            StatusText.Text = "Importando de otro proyecto…";
            var plan = await LibraryTransfer.ImportAsync(folder.FolderName, repository, choice.Options);
            if (_currentRepository != repository) return;
            await RefreshLibraryAsync();
            if (choice.Options.Profiles || choice.Options.Characters) await RefreshVoiceLabAsync();
            StatusText.Text = TransferSummary(plan, "Importado");
            MessageBox.Show(this, TransferSummary(plan, "Importado") + "\n\nEl otro proyecto no cambió. Lo que ya tenías se conserva; " +
                "para los mismos archivos manda la clasificación importada.", "Importar de otro proyecto", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SetMainLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository) return;
        var settings = MainLibrarySettings.Load();
        if (settings.Project is not null && settings.ProjectId != repository.Manifest.Id &&
            MessageBox.Show(this, $"La biblioteca principal ahora es «{settings.ProjectName}». ¿Cambiarla por la de este proyecto?\n\n" +
                    "Los proyectos que la siguen pasarán a seguir esta.", "Biblioteca principal",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        settings.Project = repository.ProjectRoot;
        settings.ProjectId = repository.Manifest.Id;
        settings.ProjectName = repository.Manifest.Name;
        settings.Followers.Remove(repository.Manifest.Id);
        settings.Received.Clear(); // another main: every follower receives it next time
        settings.Save();
        UpdateMainLibraryPanel();
        StatusText.Text = $"«{repository.Manifest.Name}» es la biblioteca principal. Los demás proyectos pueden seguirla (casilla «Seguirla» en su Biblioteca).";
    }

    private async void FollowMainLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository) return;
        var settings = MainLibrarySettings.Load();
        var id = repository.Manifest.Id;
        settings.Followers.Remove(id);
        settings.Received.Remove(id);
        if (FollowMainLibraryCheck.IsChecked == true) settings.Followers.Add(id);
        settings.Save();
        UpdateMainLibraryPanel();
        if (settings.Follows(id)) await SyncMainLibraryAsync(repository, force: true);
        else StatusText.Text = "Este proyecto ya no sigue la biblioteca principal (lo recibido se queda).";
    }

    private async void SyncMainLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is { } repository) await SyncMainLibraryAsync(repository, force: true);
    }

    /// <summary>Brings the main library into a project that follows it. Without <paramref name="force"/>, only when the main
    /// database changed since the last time (on opening). Never blocks opening the project: problems go to the status line.</summary>
    private async Task SyncMainLibraryAsync(SqliteProjectRepository repository, bool force)
    {
        var settings = MainLibrarySettings.Load();
        var id = repository.Manifest.Id;
        if (!settings.Follows(id) || settings.Project is not { } main) return;
        if (!File.Exists(Path.Combine(main, ProjectLayout.DatabaseFileName)))
        {
            StatusText.Text = $"No se encuentra la biblioteca principal «{settings.ProjectName}» ({main}).";
            return;
        }
        var stamp = MainLibrarySettings.DatabaseStamp(main);
        if (!force && settings.Received.GetValueOrDefault(id) >= stamp) return;
        try
        {
            StatusText.Text = $"Actualizando la biblioteca desde «{settings.ProjectName}»…";
            var plan = await LibraryTransfer.ImportAsync(main, repository, new LibraryTransferOptions(Library: true));
            settings = MainLibrarySettings.Load();
            settings.Received[id] = stamp;
            settings.Save();
            if (_currentRepository != repository) return;
            if (!plan.IsEmpty) await RefreshLibraryAsync();
            StatusText.Text = plan.IsEmpty ? $"Biblioteca al día con «{settings.ProjectName}»." : TransferSummary(plan, $"Desde «{settings.ProjectName}»");
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex);
            if (_currentRepository == repository)
                StatusText.Text = "No se pudo actualizar desde la biblioteca principal: " + ErrorLog.Summary(ex);
        }
    }

    /// <summary>A new project, when there is a main library: follow it?</summary>
    private async Task OfferMainLibraryAsync(SqliteProjectRepository repository)
    {
        var settings = MainLibrarySettings.Load();
        if (settings.Project is not { } main || settings.ProjectId == repository.Manifest.Id ||
            !File.Exists(Path.Combine(main, ProjectLayout.DatabaseFileName)))
            return;
        if (MessageBox.Show(this, $"¿Este proyecto sigue la biblioteca principal «{settings.ProjectName}»?\n\n" +
                    "Sí: recibe ahora sus fuentes y recursos, y lo nuevo cada vez que lo abras. Se puede cambiar en la Biblioteca.",
                "Biblioteca principal", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
            return;
        settings.Followers.Remove(repository.Manifest.Id);
        settings.Followers.Add(repository.Manifest.Id);
        settings.Save();
        UpdateMainLibraryPanel();
        await SyncMainLibraryAsync(repository, force: true);
    }

    private static string TransferSummary(LibraryTransferPlan plan, string what)
    {
        if (plan.IsEmpty) return $"{what}: nada nuevo, ya estaba todo.";
        var parts = new List<string>();
        if (plan.NewSources > 0) parts.Add($"{plan.NewSources} fuente(s)");
        if (plan.NewAssets > 0) parts.Add($"{plan.NewAssets} recurso(s) nuevo(s)");
        if (plan.UpdatedAssets > 0) parts.Add($"{plan.UpdatedAssets} recurso(s) con clasificación actualizada");
        if (plan.NewProfiles > 0) parts.Add($"{plan.NewProfiles} perfil(es) de voz");
        if (plan.NewCharacters > 0) parts.Add($"{plan.NewCharacters} personaje(s)");
        if (parts.Count == 0) parts.Add("reglas de carpeta y personajes actualizados");
        return $"{what}: " + string.Join(", ", parts) + ".";
    }
}

/// <summary>What to bring from the other project.</summary>
internal sealed class ImportProjectWindow : Window
{
    private readonly CheckBox _library = new() { Content = "Biblioteca: fuentes, reglas de carpeta, clasificación y etiquetas", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _profiles = new() { Content = "Perfiles de voz (Voice Lab)", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _characters = new() { Content = "Personajes (con su voz)", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };

    public LibraryTransferOptions Options => new(_library.IsChecked == true, _profiles.IsChecked == true, _characters.IsChecked == true);

    public ImportProjectWindow(string project)
    {
        Title = "Importar de otro proyecto";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Qué traer de «{project}». Lo que ya está aquí no se duplica (la misma carpeta, el mismo archivo, el mismo " +
                   "nombre de perfil o personaje) y el otro proyecto no cambia.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(_library);
        panel.Children.Add(_profiles);
        panel.Children.Add(_characters);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Importar", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (Options is { Library: false, Profiles: false, Characters: false }) return;
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5), IsCancel = true });
        panel.Children.Add(buttons);
        Content = panel;
    }
}
