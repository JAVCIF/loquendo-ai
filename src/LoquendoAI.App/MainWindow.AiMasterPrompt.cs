using System.IO;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private const string MasterPromptFile = "director-prompt-master.txt";
    private string? _directorMasterPrompt;

    private async Task LoadDirectorMasterPromptAsync(SqliteProjectRepository repository)
    {
        _directorMasterPrompt = null;
        var file = Path.Combine(repository.ProjectRoot, MasterPromptFile);
        if (!File.Exists(file)) return;
        var content = await File.ReadAllTextAsync(file);
        if (content.Length <= 20000 && !string.IsNullOrWhiteSpace(content))
            _directorMasterPrompt = content;
    }

    private async void ViewMasterPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository)
        {
            AiDirectorStatusText.Text = "Abre un proyecto para editar su prompt maestro.";
            return;
        }
        var recorded = AiDirectorModeCombo.SelectedIndex == 1;
        var dialog = new Window
        {
            Owner = this, Title = "Prompt maestro del Director IA", Width = 830, Height = 710,
            MinWidth = 630, MinHeight = 510, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new Grid { Margin = new Thickness(14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new TextBlock
        {
            Text = "Instrucciones base del sistema. Se añaden reglas fijas del modo y, al generar, " +
                   "tu encargo y el catálogo de la escena. Cambiarlas afecta las próximas generaciones de este proyecto." +
                   (DirectorAiGrammar.IsLegacyPrompt(_directorMasterPrompt)
                       ? " ⚠ Este prompt guardado describe la sintaxis antigua de líneas; desde hotfix 18 la IA responde con acciones " +
                         "tipadas y esa parte ya no hace falta. Pulsa «Restablecer base» y vuelve a añadir tus criterios propios."
                       : ""),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        var tabs = new TabControl();
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);
        var editor = new TextBox
        {
            Text = _directorMasterPrompt ?? DirectorAiGrammar.BaseInstructions,
            AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12.5,
            Margin = new Thickness(5)
        };
        var preview = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12.5,
            Margin = new Thickness(5)
        };
        void UpdatePreview() => preview.Text = DirectorAiGrammar.UseLegacyOutput
            ? DirectorAiGrammar.ForMode(recorded, editor.Text)
            : DirectorAiGrammar.ForTypedMode(recorded, editor.Text);
        editor.TextChanged += (_, _) => UpdatePreview();
        tabs.Items.Add(new TabItem { Header = "Base editable", Content = editor });
        tabs.Items.Add(new TabItem
        {
            Header = recorded ? "Enviado en modo voces" : "Enviado en modo historia",
            Content = preview
        });
        tabs.SelectedIndex = 0;
        UpdatePreview();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var reset = new Button { Content = "Restablecer base", Padding = new Thickness(12, 5, 12, 5) };
        reset.Click += (_, _) => { editor.Text = DirectorAiGrammar.BaseInstructions; tabs.SelectedIndex = 0; };
        var cancel = new Button
        {
            Content = "Cancelar", Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0), IsCancel = true
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var save = new Button
        {
            Content = "Guardar", Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0), IsDefault = true
        };
        save.Click += (_, _) =>
        {
            if (editor.Text.Trim().Length is < 1 or > 20000)
            {
                MessageBox.Show(dialog, "El prompt debe contener entre 1 y 20 000 caracteres.",
                    "Prompt maestro", MessageBoxButton.OK, MessageBoxImage.Information);
                tabs.SelectedIndex = 0;
                editor.Focus();
                return;
            }
            dialog.DialogResult = true;
        };
        actions.Children.Add(reset);
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        dialog.Content = layout;
        if (dialog.ShowDialog() != true || _currentRepository != repository) return;
        var content = editor.Text.Trim();
        try
        {
            var file = Path.Combine(repository.ProjectRoot, MasterPromptFile);
            if (content == DirectorAiGrammar.BaseInstructions.Trim() || content == DirectorAiGrammar.Instructions.Trim())
            {
                if (File.Exists(file)) File.Delete(file);
                _directorMasterPrompt = null;
                AiDirectorStatusText.Text = "Prompt maestro restablecido para este proyecto.";
            }
            else
            {
                await File.WriteAllTextAsync(file, content);
                if (_currentRepository != repository) return;
                _directorMasterPrompt = content;
                AiDirectorStatusText.Text = "Prompt maestro guardado. Se usará en las próximas generaciones.";
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
