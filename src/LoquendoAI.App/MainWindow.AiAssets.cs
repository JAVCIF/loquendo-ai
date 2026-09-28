using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using LoquendoAI.Core.Models;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private const string DirectorImageAnalyzerVersion = "0.9.0-alpha-1";
    private static string TagValue(IEnumerable<AssetTag> tags, Guid id, string field)
    {
        var matching = tags.Where(t => t.AssetId == id).ToArray();
        return matching.FirstOrDefault(t => t.Key == "director.manual." + field)?.Value ??
            matching.FirstOrDefault(t => t.Key == "director.auto." + field)?.Value ?? "";
    }

    private async void AssetsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.Column.Header?.ToString() != "Descripción IA (editable)" ||
            e.Row.Item is not AssetRow row || e.EditingElement is not TextBox editor ||
            _currentRepository is not { } repository) return;
        var description = editor.Text.Trim();
        if (string.Equals(description, row.InitialDescription.Trim(), StringComparison.Ordinal))
            return; // Leaving the cell to open another window is not an edit.
        if (description.Length > 500)
        {
            FooterText.Text = "La descripción admite hasta 500 caracteres; recórtala antes de guardar.";
            e.Cancel = true;
            return;
        }
        try
        {
            var tags = await repository.GetAssetTagsAsync(row.AssetId);
            if (_currentRepository != repository) return;
            var manual = tags.Where(t => t.Key.StartsWith("director.manual.", StringComparison.Ordinal) &&
                t.Key != "director.manual.description").ToList();
            if (description.Length > 0)
                manual.Add(new AssetTag(row.AssetId, "director.manual.description", description));
            await repository.ReplaceDirectorAssetTagsAsync(row.AssetId, "director.manual.", manual);
            row.Description = description.Length > 0 ? description :
                tags.FirstOrDefault(x => x.Key == "director.auto.description")?.Value ?? "";
            if (description.Length == 0)
                _ = Dispatcher.BeginInvoke(new Action(() => AssetsGrid.Items.Refresh()));
            FooterText.Text = "Descripción guardada; úsala para orientar al Director IA.";
        }
        catch (Exception ex)
        {
            FooterText.Text = "No se guardó la descripción: " + ex.Message;
            // The edit event fires before WPF completes the binding update.
            // Reload after it finishes so a failed write never appears saved.
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await RefreshLibraryAsync(); }
                catch (Exception refreshError) { FooterText.Text = refreshError.Message; }
            }));
        }
    }

    private void AssetsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AssetsGrid.SelectedItem is not AssetRow selected) return;
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not DataGridCell)
            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement content => content.Parent,
                _ => null
            };
        if (current is not DataGridCell cell || !ReferenceEquals(cell.DataContext, selected))
            return; // Ignore double-clicks on headers, scrollbars and empty grid space.
        if (cell.Column.Header?.ToString() == "Descripción IA (editable)")
            return; // Double-click this cell to edit the inline description.
        e.Handled = true;
        EditDirectorAssetTags_Click(sender, e);
    }

    private async void EditDirectorAssetTags_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || AssetsGrid.SelectedItem is not AssetRow row)
        {
            FooterText.Text = "Selecciona un recurso de la biblioteca para editar sus etiquetas.";
            return;
        }
        var repository = _currentRepository;
        try
        {
            var tags = await repository.GetAssetTagsAsync(row.AssetId);
            if (_currentRepository != repository) return;
            var dialog = new Window
            {
                Owner = this, Title = "Etiquetas del Director · " + row.Name,
                Width = 520, Height = 460, MinWidth = 440, MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            // 1.3.0: a field without a manual value shows what the AI wrote (director.auto.*). It becomes a manual
            // value only if it is changed here, so a later analysis can still update it.
            var automatic = new Dictionary<string, string>();
            var manualBefore = new HashSet<string>();
            TextBox Field(string name, string key, int height = 29)
            {
                var manual = tags.FirstOrDefault(t => t.Key == "director.manual." + key)?.Value;
                var auto = tags.FirstOrDefault(t => t.Key == "director.auto." + key)?.Value ?? "";
                automatic[key] = auto;
                if (manual is not null) manualBefore.Add(key);
                panel.Children.Add(new TextBlock
                {
                    Text = name + (manual is null && auto.Length > 0 ? "   · escrito por la IA" : ""), Margin = new Thickness(0, 8, 0, 4)
                });
                var box = new TextBox
                {
                    Text = manual ?? auto,
                    Height = height, TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = height > 30, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    ToolTip = manual is null && auto.Length > 0
                        ? "Lo escribió la IA. Si lo cambias, tu versión tiene prioridad; si no, la IA puede actualizarlo al volver a analizar."
                        : "Vacío: se usará la descripción automática, si existe."
                };
                panel.Children.Add(box);
                return box;
            }
            var description = Field("Descripción (qué se ve)", "description", 90);
            var role = Field("Uso: fondo, render, hoja_de_sprites, prop, música ambiental, meme…", "role");
            var mood = Field("Ánimo / expresión", "mood");
            var subject = Field("Sujeto o lugar", "subject");
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new Button
            {
                Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0), IsCancel = true
            };
            cancel.Click += (_, _) => dialog.DialogResult = false;
            actions.Children.Add(cancel);
            var save = new Button
            {
                Content = "Guardar etiquetas", Padding = new Thickness(12, 5, 12, 5),
                IsDefault = true
            };
            save.Click += (_, _) => dialog.DialogResult = true;
            actions.Children.Add(save);
            panel.Children.Add(actions);
            dialog.Content = panel;
            if (dialog.ShowDialog() != true || _currentRepository != repository)
            {
                FooterText.Text = "Edición de etiquetas cancelada; no se modificó el catálogo.";
                return;
            }
            var fields = new Dictionary<string, string>
            {
                ["description"] = description.Text.Trim(), ["role"] = role.Text.Trim(),
                ["mood"] = mood.Text.Trim(), ["subject"] = subject.Text.Trim()
            };
            await repository.ReplaceDirectorAssetTagsAsync(row.AssetId, "director.manual.",
                fields.Where(pair => pair.Value.Length > 0 &&
                        (manualBefore.Contains(pair.Key) || pair.Value != automatic.GetValueOrDefault(pair.Key, "").Trim())).Select(pair =>
                    new AssetTag(row.AssetId, "director.manual." + pair.Key, pair.Value[..Math.Min(500, pair.Value.Length)])));
            await RefreshLibraryAsync();
            FooterText.Text = "Etiquetas guardadas. Las correcciones manuales tienen prioridad en el Director IA.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static string ImageForAi(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0]; // For animated GIFs, describe their first frame.
        var factor = Math.Min(1d, 768d / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource image = factor < 1 ? new TransformedBitmap(frame, new ScaleTransform(factor, factor)) : frame;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
