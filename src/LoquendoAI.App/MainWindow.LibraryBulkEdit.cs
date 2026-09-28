using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private async void EditAssetClassification_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository) return;
        var selected = AssetsGrid.SelectedItems.Cast<AssetRow>().Select(x => x.AssetId).Distinct().ToArray();
        var filtered = _assetFiltersDirty ? Array.Empty<Guid>() :
            _filteredAssetRows.Select(x => x.AssetId).Distinct().ToArray();
        if (selected.Length == 0 && filtered.Length == 0)
        {
            FooterText.Text = "Selecciona recursos o ejecuta una búsqueda con resultados.";
            return;
        }

        var dialog = new Window
        {
            Owner = this, Title = "Editar tipo y personaje / sujeto", Width = 530, Height = 340,
            MinWidth = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Elige los campos que deseas cambiar. Los demás conservarán su valor actual.",
            TextWrapping = TextWrapping.Wrap
        });
        var selectionTarget = new RadioButton
        {
            Content = $"Solo seleccionados ({selected.Length})", IsChecked = selected.Length > 0,
            IsEnabled = selected.Length > 0, Margin = new Thickness(0, 12, 0, 4), GroupName = "LibraryTarget"
        };
        var filteredTarget = new RadioButton
        {
            Content = _assetFiltersDirty ? "Todos los filtrados (pulsa Buscar primero)" :
                $"Todos los filtrados ({filtered.Length}, incluidos los no visibles)",
            IsChecked = selected.Length == 0, IsEnabled = filtered.Length > 0,
            GroupName = "LibraryTarget"
        };
        panel.Children.Add(selectionTarget);
        panel.Children.Add(filteredTarget);

        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var changeKind = new CheckBox { Content = "Cambiar tipo", Width = 175, VerticalAlignment = VerticalAlignment.Center };
        var kindChoices = Enum.GetValues<AssetKind>()
            .Select(x => new LibraryKindChoice(x, KindLabel(x))).ToArray();
        var kindBox = new ComboBox
        {
            Width = 255, Height = 28, IsEnabled = false, DisplayMemberPath = "Name",
            ItemsSource = kindChoices,
            SelectedIndex = selected.Length == 1
                ? Array.FindIndex(kindChoices, x => x.Kind == AssetsGrid.SelectedItems.Cast<AssetRow>().First().AssetKind)
                : -1
        };
        changeKind.Checked += (_, _) => kindBox.IsEnabled = true;
        changeKind.Unchecked += (_, _) => kindBox.IsEnabled = false;
        kindRow.Children.Add(changeKind);
        kindRow.Children.Add(kindBox);
        panel.Children.Add(kindRow);

        var subjectRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var changeSubject = new CheckBox
        {
            Content = "Cambiar personaje / sujeto", Width = 175,
            VerticalAlignment = VerticalAlignment.Center
        };
        var subjectBox = new TextBox
        {
            Width = 255, Height = 28, IsEnabled = false,
            ToolTip = "Vacío borra el sujeto. Usa el nombre exacto del personaje para orientar al Director IA."
        };
        changeSubject.Checked += (_, _) => subjectBox.IsEnabled = true;
        changeSubject.Unchecked += (_, _) => subjectBox.IsEnabled = false;
        subjectRow.Children.Add(changeSubject);
        subjectRow.Children.Add(subjectBox);
        panel.Children.Add(subjectRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Estos cambios manuales sobreviven a los reescaneos de las fuentes.",
            Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 19, 0, 0)
        };
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5), IsCancel = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var save = new Button { Content = "Aplicar cambios", Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) =>
        {
            if (changeKind.IsChecked != true && changeSubject.IsChecked != true ||
                changeKind.IsChecked == true && kindBox.SelectedItem is null)
            {
                MessageBox.Show(dialog, "Marca los campos a cambiar y elige un tipo cuando corresponda.",
                    "Editar catálogo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (subjectBox.Text.Trim().Length > 200)
            {
                MessageBox.Show(dialog, "Personaje / sujeto admite hasta 200 caracteres.",
                    "Editar catálogo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() != true || _currentRepository != repository) return;

        var ids = filteredTarget.IsChecked == true ? filtered : selected;
        var kind = changeKind.IsChecked == true ? (kindBox.SelectedItem as LibraryKindChoice)?.Kind : null;
        var setSubject = changeSubject.IsChecked == true;
        var subject = subjectBox.Text.Trim();
        try
        {
            FooterText.Text = $"Actualizando {ids.Length} recursos…";
            AssetsGrid.IsEnabled = false;
            var count = await Task.Run(async () =>
            {
                await using var writer = new SqliteProjectRepository(repository.ProjectRoot, repository.Manifest);
                await writer.AttachAsync().ConfigureAwait(false);
                return await writer.UpdateAssetClassificationsAsync(ids, kind, setSubject, subject)
                    .ConfigureAwait(false);
            });
            if (_currentRepository != repository) return;
            await RefreshLibraryAsync();
            FooterText.Text = $"{count} recurso(s) actualizados. El Director IA usará la clasificación manual.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { AssetsGrid.IsEnabled = true; }
    }
}
