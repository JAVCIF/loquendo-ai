using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Dialogue;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

/// <summary>Dialogue module: «Prompt maestro…», pasting text, the format help, AI generation and «Exportar a escenas».</summary>
public partial class MainWindow
{
    private const string DialogueMasterPromptFile = "prompt-maestro.txt";
    private string? _dialogueMasterPrompt;

    private async Task LoadDialogueMasterPromptAsync(SqliteProjectRepository repository)
    {
        _dialogueMasterPrompt = null;
        var file = Path.Combine(repository.ProjectRoot, DialogueFolderName, DialogueMasterPromptFile);
        if (!File.Exists(file)) return;
        var content = await File.ReadAllTextAsync(file);
        if (content.Length <= 20000 && !string.IsNullOrWhiteSpace(content)) _dialogueMasterPrompt = content;
    }

    private string DialogueInstructions => _dialogueMasterPrompt ?? DialogueScript.BaseInstructions;

    private Window DialogueWindow(string title, double width, double height) => new()
    {
        Owner = this, Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 520), MinHeight = Math.Min(height, 380),
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    private static Button DialogButton(string text, bool accent = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        // Only the accent button gets a style: setting Style = null would drop the theme's (white button in dark mode).
        if (accent && Application.Current.TryFindResource("AccentButton") is Style style) button.Style = style;
        return button;
    }

    /// <summary>A one-line text question; null when cancelled or empty.</summary>
    private string? AskDialogueText(string title, string label, string initial)
    {
        var dialog = DialogueWindow(title, 420, 170);
        dialog.ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { Text = initial, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 60 };
        panel.Children.Add(box);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = DialogButton("Cancelar");
        cancel.IsCancel = true;
        var ok = DialogButton("Aceptar", accent: true);
        ok.IsDefault = true;
        ok.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        panel.Children.Add(actions);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        if (dialog.ShowDialog() != true) return null;
        var text = box.Text.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, ' ');
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    // ───────────────────────────── Paste and format ─────────────────────────────

    private void PasteDialogueText_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null) return;
        var dialog = DialogueWindow("Pegar o escribir diálogos", 760, 560);
        var layout = new Grid { Margin = new Thickness(14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new TextBlock
        {
            Text = "Pega aquí la respuesta de la IA web o escribe el guion: «Nombre: texto» por línea, una línea sin nombre es " +
                   "narración y «[ESCENA] título» empieza una escena. Si viene en otro formato (guion de cine, novela…), elige «Modo análisis».",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        var editor = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13
        };
        try { if (Clipboard.ContainsText()) editor.Text = Clipboard.GetText(); } catch (Exception) { /* clipboard busy */ }
        Grid.SetRow(editor, 1);
        layout.Children.Add(editor);
        var actions = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var mode = new ComboBox { Width = 190, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        mode.Items.Add(new ComboBoxItem { Content = "Formato recomendado" });
        mode.Items.Add(new ComboBoxItem { Content = "Modo análisis" });
        mode.SelectedIndex = DialogueParseModeCombo.SelectedIndex;
        DockPanel.SetDock(mode, Dock.Left);
        actions.Children.Add(mode);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var paste = DialogButton("Pegar del portapapeles");
        paste.Click += (_, _) => { try { editor.Text = Clipboard.GetText(); } catch (Exception) { } };
        var cancel = DialogButton("Cancelar");
        cancel.IsCancel = true;
        var import = DialogButton("Importar", accent: true);
        import.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(paste);
        buttons.Children.Add(cancel);
        buttons.Children.Add(import);
        actions.Children.Add(buttons);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        dialog.Content = layout;
        if (dialog.ShowDialog() != true) return;
        DialogueParseModeCombo.SelectedIndex = mode.SelectedIndex;
        ImportDialogueEntries(DialogueScript.ParseText(editor.Text, SelectedDialogueParseMode), "el texto pegado");
    }

    private const string DialogueFormatHelp = """
        FORMATO RECOMENDADO (el más cómodo; también es el que pide el «Prompt maestro» a la IA web)

        [ESCENA] En la cocina
        Narrador: Era un lunes cualquiera en Springfield.
        Homero: Marge, ¿dónde están mis donas?
        Marge: (molesta) Te las comiste anoche, Homero.
        Vendedor: ¡Donas, donas calientes!
        Y así empezó el peor día de Homero.

        • «Nombre: texto» → diálogo. Si el nombre es un personaje registrado usa su perfil de voz; si no, es un NPC
          (voz NPC, Jorge por defecto; en la tabla le puedes dar cualquier voz de la lista —TTS7, SAPI4, SAPI5— o un perfil). Aparece un aviso.
        • Una línea sin nombre, o «Narrador: …» → narración (voz NPC o la que elijas).
        • «[ESCENA] título» → aquí empieza una escena nueva al exportar.
        • «(acotación)» al inicio del texto, o sola en su línea antes de la frase → columna Comentario.
        • Las líneas que empiezan con # o // son notas y se ignoran.
        • También sirve «Nombre (enojado): texto», «**Nombre:** texto» y listas con guiones o números.

        MODO ANÁLISIS (para textos que no siguen el formato)
        • Guion de cine: NOMBRE en mayúsculas en su línea y el diálogo debajo; «INT./EXT.» empieza escena.
        • Novela: «—Hola —dijo Bart—. ¿Vienes?» o «Hola», respondió Lisa (sin atribución queda para que elijas).
        • Chats «[Bart] hola» o «<Bart> hola», «Bart - hola», tiempos «00:01:02», encabezados «Escena 2: …».

        WORD Y EXCEL
        • Word (.docx): se leen los párrafos y las tablas, en orden.
        • Excel (.xlsx, primera hoja) o CSV: con encabezados «Tipo», «Personaje» / «Quién habla», «Diálogo» / «Texto»,
          «Comentario»; sin encabezados, 2 columnas = personaje y texto.
        • Los .doc y .xls antiguos no: guárdalos como .docx / .xlsx.

        LÍMITES: 480 líneas por guion (12 escenas × 40 líneas, lo que admite el Director IA por episodio).
        """;

    private void ShowDialogueFormat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = DialogueWindow("Formato de los diálogos", 780, 640);
        var layout = new DockPanel { Margin = new Thickness(14) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var copy = DialogButton("Copiar ejemplo");
        copy.Click += (_, _) =>
        {
            var help = DialogueFormatHelp.Replace("\r\n", "\n"); // CRLF in a Windows checkout
            var start = help.IndexOf("[ESCENA]", StringComparison.Ordinal);
            var end = help.IndexOf("\n\n", start, StringComparison.Ordinal);
            try { if (start >= 0 && end > start) Clipboard.SetText(help[start..end]); }
            catch (Exception) { }
        };
        var close = DialogButton("Cerrar", accent: true);
        close.IsCancel = true;
        actions.Children.Add(copy);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(new TextBox
        {
            Text = DialogueFormatHelp, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13
        });
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    private void ExportDialogueText_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        if (_dialogueRows.Count == 0)
        {
            DialogueStatusText.Text = "No hay filas que exportar.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar diálogos a texto", Filter = "Texto (*.txt)|*.txt", DefaultExt = ".txt",
            FileName = SafeFilePart(_dialogueSet?.Name ?? "dialogos") + ".txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, DialogueScript.ToText(_dialogueRows.Select(x => x.ToEntry())), new UTF8Encoding(true));
            DialogueStatusText.Text = $"Exportado a {Path.GetFileName(dialog.FileName)} (formato recomendado).";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ───────────────────────────── Prompt maestro ─────────────────────────────

    private void DialogueMasterPrompt_Click(object sender, RoutedEventArgs e) => ShowDialoguePromptWindow();

    /// <summary>
    /// Asks which characters take part (and other names, length and story) and shows the prompt for a web AI, with
    /// its editable base. Keeps the choice for «Generar diálogos con IA». True when the user accepted.
    /// </summary>
    private bool ShowDialoguePromptWindow()
    {
        if (_currentRepository is not { } repository) return false;
        var dialog = DialogueWindow("Prompt maestro · Diálogos", 1040, 720);
        var layout = new Grid { Margin = new Thickness(14) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto,
                     GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            left.RowDefinitions.Add(new RowDefinition { Height = height });
        void Put(FrameworkElement element, int row) { Grid.SetRow(element, row); left.Children.Add(element); }
        Put(new TextBlock { Text = "¿Qué personajes participan? (clic para marcar)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }, 0);
        var characters = new ListBox { SelectionMode = SelectionMode.Multiple, DisplayMemberPath = "Name" };
        var choices = _characters.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new CharacterChoice(x.Id, x.Name)).ToArray();
        characters.ItemsSource = choices;
        foreach (var choice in choices.Where(x => x.Id is Guid id && _dialogueAi.Characters.Contains(id))) characters.SelectedItems.Add(choice);
        Put(characters, 1);
        Put(new TextBlock
        {
            Text = "Otros (secundarios, separados por comas):", Margin = new Thickness(0, 10, 0, 4),
            ToolTip = "Nombres que no están registrados: serán NPC con la voz NPC (puedes cambiársela en la tabla)."
        }, 2);
        var extras = new TextBox { Text = _dialogueAi.Extras, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        Put(extras, 3);
        var linesPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        linesPanel.Children.Add(new TextBlock { Text = "Líneas aproximadas:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var lines = new TextBox { Text = DialogueAiLinesBox.Text, Width = 50, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        linesPanel.Children.Add(lines);
        Put(linesPanel, 4);
        Put(new TextBlock { Text = "Historia (de qué trata, tono, final):", Margin = new Thickness(0, 10, 0, 4) }, 5);
        Put(new TextBlock
        {
            Text = "Vacía: el prompt deja un hueco para escribirla en la IA web.", Foreground = Application.Current.TryFindResource("Theme.TextMuted") as System.Windows.Media.Brush,
            FontSize = 11, Margin = new Thickness(0, 0, 0, 4)
        }, 6);
        var story = new TextBox { Text = DialogueStoryBox.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Put(story, 7);
        layout.Children.Add(left);

        var tabs = new TabControl();
        Grid.SetColumn(tabs, 1);
        var preview = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12.5, Margin = new Thickness(5)
        };
        var editor = new TextBox
        {
            Text = DialogueInstructions, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12.5, Margin = new Thickness(5)
        };
        tabs.Items.Add(new TabItem { Header = "Prompt para IA web", Content = preview });
        tabs.Items.Add(new TabItem { Header = "Base editable", Content = editor });
        tabs.SelectedIndex = 0;
        layout.Children.Add(tabs);

        int Lines() => int.TryParse(lines.Text.Trim(), out var value) ? Math.Clamp(value, 4, DialogueScript.MaxLines) : 30;
        CharacterDefinition[] Chosen() => characters.SelectedItems.OfType<CharacterChoice>()
            .Select(c => _characters.FirstOrDefault(x => x.Id == c.Id)).OfType<CharacterDefinition>().ToArray();
        string[] Extras() => extras.Text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        void Update() => preview.Text = DialogueScript.WebPrompt(editor.Text,
            DialogueScript.CharactersSection(Chosen().Select(x => (x.Name, x.Notes ?? "")), Extras()), story.Text, Lines());
        characters.SelectionChanged += (_, _) => Update();
        extras.TextChanged += (_, _) => Update();
        lines.TextChanged += (_, _) => Update();
        story.TextChanged += (_, _) => Update();
        editor.TextChanged += (_, _) => Update();
        Update();

        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Text = "Copia el prompt, pégalo en la IA web y trae su respuesta con «Pegar / escribir texto…»." };
        var actions = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var reset = DialogButton("Restablecer base");
        reset.Click += (_, _) => { editor.Text = DialogueScript.BaseInstructions; tabs.SelectedIndex = 1; };
        var copy = DialogButton("Copiar prompt");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(preview.Text); status.Text = "Prompt copiado. Pégalo en ChatGPT, Gemini, Claude…"; }
            catch (Exception ex) { status.Text = "No se pudo copiar: " + ex.Message; }
        };
        var cancel = DialogButton("Cancelar");
        cancel.IsCancel = true;
        var save = DialogButton("Guardar", accent: true);
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            if (editor.Text.Trim().Length is < 1 or > 20000)
            {
                MessageBox.Show(dialog, "La base debe tener entre 1 y 20 000 caracteres.", "Prompt maestro", MessageBoxButton.OK, MessageBoxImage.Information);
                tabs.SelectedIndex = 1;
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(reset);
        buttons.Children.Add(copy);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right);
        actions.Children.Add(buttons);
        actions.Children.Add(status);
        Grid.SetRow(actions, 1);
        Grid.SetColumnSpan(actions, 2);
        layout.Children.Add(actions);
        dialog.Content = layout;
        if (dialog.ShowDialog() != true || _currentRepository != repository) return false;

        _dialogueAi.Characters = Chosen().Select(x => x.Id).ToList();
        _dialogueAi.Extras = extras.Text.Trim();
        _dialogueAi.Lines = Math.Clamp(Lines(), 4, DialogueScript.MaxAiLinesPerRun);
        DialogueAiLinesBox.Text = _dialogueAi.Lines.ToString();
        DialogueStoryBox.Text = story.Text;
        MarkDialogueDirty();
        var content = editor.Text.Trim();
        try
        {
            var file = Path.Combine(repository.ProjectRoot, DialogueFolderName, DialogueMasterPromptFile);
            if (content == DialogueScript.BaseInstructions.Trim())
            {
                if (File.Exists(file)) File.Delete(file);
                _dialogueMasterPrompt = null;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, content);
                _dialogueMasterPrompt = content;
            }
        }
        catch (Exception ex) { ShowError(ex); }
        DialogueStatusText.Text = $"Personajes para la IA: {string.Join(", ", Chosen().Select(x => x.Name).Concat(Extras())).DefaultIfEmpty("los que necesite la historia")}.";
        return true;
    }

    // ───────────────────────────── AI generation ─────────────────────────────

    /// <summary>Writes dialogue lines only (no scenes) with the chosen model, the «Prompt maestro» base and the
    /// characters chosen there. With lines already in the table it continues the story or replaces it.</summary>
    private async void GenerateDialogueAi_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository || _dialogueWork is not null) return;
        CommitDialogueEdits();
        if (DialogueModelBox.SelectedItem is not AiModelChoice model)
        {
            DialogueStatusText.Text = "Elige un modelo (se configuran en «Configurar IA…», pestaña Director IA).";
            return;
        }
        var storyText = DialogueStoryBox.Text.Trim();
        if (storyText.Length is < 8 or > 12000)
        {
            DialogueStatusText.Text = "Escribe arriba la historia (8 a 12 000 caracteres): de qué trata, el tono y cómo termina.";
            DialogueStoryBox.Focus();
            return;
        }
        if (_dialogueAi.Characters.Count == 0 && _dialogueAi.Extras.Trim().Length == 0 && !ShowDialoguePromptWindow()) return;
        var requested = int.TryParse(DialogueAiLinesBox.Text.Trim(), out var value) ? Math.Clamp(value, 4, DialogueScript.MaxAiLinesPerRun) : 30;
        var room = DialogueScript.MaxLines - _dialogueRows.Count(x => x.IsLine);
        if (room < 1)
        {
            DialogueStatusText.Text = $"El guion ya tiene {DialogueScript.MaxLines} líneas: crea otro guion de diálogos.";
            return;
        }
        requested = Math.Min(requested, room);
        bool? append = null;
        if (_dialogueRows.Any(x => x.IsLine))
        {
            var answer = MessageBox.Show(this, "La tabla ya tiene líneas.\n\nSí = la IA continúa la historia y se añade al final\nNo = reemplazar la tabla",
                "Generar diálogos con IA", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            append = answer == MessageBoxResult.Yes;
        }

        var chosen = _characters.Where(x => _dialogueAi.Characters.Contains(x.Id)).ToArray();
        var extras = _dialogueAi.Extras.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var prompt = new StringBuilder();
        prompt.AppendLine(DialogueScript.CharactersSection(chosen.Select(x => (x.Name, x.Notes ?? "")), extras));
        prompt.AppendLine($"EXTENSIÓN: unas {requested} líneas.").AppendLine();
        prompt.AppendLine("HISTORIA:").AppendLine(storyText);
        if (append == true)
        {
            prompt.AppendLine().AppendLine("CONTINÚA la historia desde aquí, sin repetir lo ya dicho. Las últimas líneas fueron:");
            prompt.Append(DialogueScript.ToText(_dialogueRows.TakeLast(14).Select(x => x.ToEntry())));
        }
        var system = DialogueInstructions.Trim() + "\n\n" + DialogueScript.JsonFormatRules.Trim();

        var targetSet = _dialogueSet;
        using var cancellation = new CancellationTokenSource();
        _dialogueWork = cancellation;
        SetDialogueBusy(true);
        var progress = new Progress<string>(text =>
        {
            if (ReferenceEquals(_dialogueWork, cancellation)) DialogueStatusText.Text = text;
        });
        try
        {
            DialogueStatusText.Text = $"Escribiendo {requested} líneas con {model}…";
            var output = await GenerateAiResponseAsync(model.Provider, model.Model, system, prompt.ToString(),
                DialogueScript.JsonSchema(requested + 10), null, cancellation.Token, progress,
                new OllamaChatOptions(Math.Clamp(requested * 90 + 800, 2048, 16384), "dialogos"));
            if (_currentRepository != repository || _dialogueSet != targetSet)
            {
                DialogueStatusText.Text = "La IA terminó, pero cambiaste de guion o de proyecto: no se aplicó.";
                return;
            }
            var entries = DialogueScript.ParseAiOutput(output);
            AiDiagnostics.Note($"dialogos: IA {model.Key} → {entries.Count} filas (pedidas {requested})");
            if (entries.Count == 0) throw new InvalidDataException("La IA no devolvió líneas.");
            ImportDialogueEntries(entries, "la IA", append ?? false);
        }
        catch (OperationCanceledException) { DialogueStatusText.Text = "Generación con IA cancelada."; }
        catch (Exception ex)
        {
            ErrorLog.Record(ex);
            DialogueStatusText.Text = "La IA no pudo escribir los diálogos: " + ex.Message;
        }
        finally
        {
            _dialogueWork = null;
            SetDialogueBusy(false);
        }
    }

    // ───────────────────────────── Export to scenes ─────────────────────────────

    private async Task RefreshDialogueEpisodesAsync()
    {
        if (_currentRepository is not { } repository) return;
        var previous = DialogueEpisodeCombo.SelectedItem as DialogueEpisodeChoice;
        var episodes = await repository.GetEpisodesAsync();
        if (_currentRepository != repository) return;
        var choices = episodes.OrderBy(x => x.Number)
            .Select(x => new DialogueEpisodeChoice(x.Id, $"{x.Number}. {x.Title}"))
            .Append(new DialogueEpisodeChoice(null, "(Episodio nuevo)")).ToArray();
        DialogueEpisodeCombo.ItemsSource = choices;
        var current = (EpisodesList.SelectedItem as EpisodeScriptRow)?.Episode.Id;
        DialogueEpisodeCombo.SelectedItem = choices.FirstOrDefault(x => previous is not null && x.Id == previous.Id)
            ?? choices.FirstOrDefault(x => x.Id is not null && x.Id == current) ?? choices[0];
    }

    private async void DialogueEpisodeCombo_DropDownOpened(object? sender, EventArgs e) => await RefreshDialogueEpisodesAsync();

    private void DialogueExportMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DialoguePerSceneBox is null) return;
        DialoguePerSceneBox.IsEnabled = DialoguePerSceneLabel.IsEnabled = DialogueExportModeCombo.SelectedIndex == 1;
    }

    /// <summary>
    /// Creates NEW scenes at the end of the episode with the lines as recorded takes (their WAV copied into the
    /// project, the text as transcript and the voice they were generated with, so «Regenerar con TTS» keeps it).
    /// One scene with everything (for Director IA «Generar episodio» with recorded voices) or one scene per cut,
    /// split at the per-scene limit. Unregistered speakers become characters without profile (label, NPC voice).
    /// </summary>
    private async void ExportDialogueToScenes_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository || _dialogueWork is not null) return;
        CommitDialogueEdits();
        StopDialoguePlayback();
        RefreshAllDialogueRows();
        var lines = _dialogueRows.Where(x => x.IsLine).ToArray();
        if (lines.Length == 0)
        {
            DialogueStatusText.Text = "No hay líneas que exportar.";
            return;
        }
        var invalid = lines.Where(x => x.Text.Length == 0 || x.Kind == DialogueKind.Dialogue && x.Speaker.Length == 0)
            .Select(x => $"#{x.Order}: {(x.Text.Length == 0 ? "sin texto" : "falta quién habla")}").ToArray();
        if (invalid.Length > 0)
        {
            MessageBox.Show(this, "Corrige antes estas líneas:\n\n" + string.Join("\n", invalid.Take(15)), "Exportar a escenas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await RefreshDialogueEpisodesAsync();
        if (DialogueEpisodeCombo.SelectedItem is not DialogueEpisodeChoice target) return;
        var single = DialogueExportModeCombo.SelectedIndex == 0;
        var perScene = int.TryParse(DialoguePerSceneBox.Text.Trim(), out var limit) ? Math.Clamp(limit, 1, DialogueScript.MaxLinesPerScene) : DialogueScript.MaxLinesPerScene;
        DialoguePerSceneBox.Text = perScene.ToString();
        var entries = _dialogueRows.Select(x => x.ToEntry()).ToList();
        var parts = single
            ? [new DialogueScenePart(_dialogueSet?.Name ?? "Diálogos", "", _dialogueRows.Select((row, i) => (row, i)).Where(x => x.row.IsLine).Select(x => x.i).ToArray())]
            : DialogueScript.SplitScenes(entries, perScene);
        var newNames = lines.Where(x => x.Kind == DialogueKind.Dialogue && x.CharacterId is null)
            .Select(x => x.Speaker).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var outline = string.Join("\n", parts.Take(14).Select((p, i) => $"{i + 1}. {p.Title} · {p.Lines.Count} líneas")) +
            (parts.Count > 14 ? $"\n… y {parts.Count - 14} más" : "");
        if (MessageBox.Show(this,
                $"Se crearán {parts.Count} escena(s) NUEVAS al final de «{target.Name}» con {lines.Length} voces:\n\n{outline}\n\n" +
                (newNames.Length > 0 ? $"Personajes nuevos (sin perfil, voz NPC): {string.Join(", ", newNames.Take(10))}{(newNames.Length > 10 ? "…" : "")}\n\n" : "") +
                "Antes se generan los audios que falten. Las escenas existentes no se tocan. ¿Continuar?",
                "Exportar a escenas", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        // What is exported is fixed now: later edits of the table (it stays usable while the audio is generated)
        // do not change it, and every audio is checked before any scene is created.
        var partRows = parts.Select(part => (Part: part, Rows: part.Lines.Select(i => DialogueExportLine.Of(_dialogueRows[i], ResolveDialogueVoice(_dialogueRows[i]))).ToArray())).ToArray();
        var exportSet = _dialogueSet;
        if (!await GenerateDialogueAudioAsync(lines, force: false))
        {
            DialogueStatusText.Text = "Exportación detenida: faltan audios (revisa la columna Estado).";
            return;
        }
        var root = repository.ProjectRoot;
        var missing = partRows.SelectMany(x => x.Rows).Where(x => x.Voice is null || VoiceCache.Find(root, x.Voice.Hash, IsValidWave) is null)
            .Select(x => x.Order).ToArray();
        if (_currentRepository != repository || _dialogueSet != exportSet || missing.Length > 0)
        {
            DialogueStatusText.Text = missing.Length > 0
                ? $"Exportación detenida: la tabla cambió mientras se generaban los audios (líneas {string.Join(", ", missing.Take(8))}). Vuelve a exportar."
                : "Exportación detenida: cambió el proyecto o el guion.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _dialogueWork = cancellation;
        SetDialogueBusy(true);
        var created = new List<Scene>();
        var copied = new List<string>();
        Guid? pendingScene = null;
        try
        {
            Guid episodeId;
            if (target.Id is Guid existing) episodeId = existing;
            else
            {
                var episodes = await repository.GetEpisodesAsync();
                var number = episodes.Count == 0 ? 1 : episodes.Max(x => x.Number) + 1;
                var episode = new Episode(Guid.NewGuid(), number, TrimTitle(exportSet?.Name ?? $"Episodio {number}"));
                await repository.UpsertEpisodeAsync(episode);
                episodeId = episode.Id;
            }
            var characterIds = new Dictionary<string, Guid>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var name in partRows.SelectMany(x => x.Rows).Where(x => x.Kind == DialogueKind.Dialogue && x.CharacterId is null)
                         .Select(x => x.Speaker).Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                var existingCharacter = (await repository.GetCharactersAsync()).FirstOrDefault(x => SameDirectorName(x.Name, name));
                if (existingCharacter is not null) { characterIds[name] = existingCharacter.Id; continue; }
                var character = new CharacterDefinition(Guid.NewGuid(), name.Length > 60 ? name[..60] : name, null,
                    $"Creado desde Diálogos («{exportSet?.Name}»): NPC sin perfil propio, habla con la voz NPC o la elegida en cada línea.");
                await repository.UpsertCharacterAsync(character);
                characterIds[name] = character.Id;
            }
            var scenes = await repository.GetScenesAsync(episodeId);
            var nextIndex = scenes.Count == 0 ? 1 : scenes.Max(x => x.Index) + 1;
            foreach (var (part, rowsOfPart) in partRows)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var sceneId = Guid.NewGuid();
                var folder = Path.Combine(root, "generated", "imported_voices", $"scene_{sceneId:N}");
                Directory.CreateDirectory(folder);
                var blocks = new List<SceneScriptBlock>();
                long total = 0;
                foreach (var row in rowsOfPart)
                {
                    var voice = row.Voice ?? throw new InvalidOperationException($"La línea {row.Order} no tiene voz.");
                    var cached = VoiceCache.Find(root, voice.Hash, IsValidWave) ?? throw new InvalidOperationException($"Falta el audio de la línea {row.Order}.");
                    var blockId = Guid.NewGuid();
                    var targetPath = Path.Combine(folder, $"{blockId:N}.wav");
                    File.Copy(cached, targetPath);
                    copied.Add(targetPath);
                    string hash;
                    using (var stream = File.OpenRead(targetPath))
                        hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    var duration = GetWaveDurationMs(targetPath);
                    total += duration;
                    var characterId = row.Kind == DialogueKind.Narration ? null
                        : row.CharacterId ?? (characterIds.TryGetValue(row.Speaker, out var id) ? id : null);
                    var parameters = WithDirect(BlockParameters.Empty, DirectVoiceOf(voice.Block)) with
                    {
                        TranscriptReviewed = true, TranscriptOrigin = "tts"
                    };
                    if (row.Comment.Length > 0) parameters = parameters.WithExtra("acotacion", JsonValue.Create(row.Comment));
                    blocks.Add(new SceneScriptBlock(blockId, sceneId, blocks.Count,
                        characterId is null ? ScriptBlockKind.Narration : ScriptBlockKind.Dialogue,
                        CharacterId: characterId, Text: row.Text,
                        // The voice the take was generated with, so «Regenerar con TTS» uses the same one.
                        VoiceProfileId: voice.Profile.Id == Guid.Empty ? null : voice.Profile.Id,
                        VoicePitchOverride: row.Pitch, VoiceSpeedOverride: row.Speed, VoiceVolumeOverride: row.Volume,
                        ParametersJson: parameters.ToJson(),
                        GeneratedAudioPath: Path.GetRelativePath(root, targetPath).Replace('\\', '/'),
                        GeneratedAudioHash: ImportedVoicePrefix + hash, GeneratedDurationMs: duration));
                }
                var notes = string.Join("\n", new[]
                {
                    part.Notes,
                    single ? $"Todas las voces del guion de diálogos «{exportSet?.Name}». Director IA → «Montar voces grabadas» → " +
                             "«Generar episodio» las reparte en escenas." : $"Voces del guion de diálogos «{exportSet?.Name}»."
                }.Where(x => x.Length > 0));
                var scene = new Scene(sceneId, episodeId, nextIndex++, TrimTitle(part.Title), total, DirectionNotes: notes);
                pendingScene = sceneId;
                await repository.UpsertSceneAsync(scene);
                await repository.ReplaceSceneScriptBlocksAsync(sceneId, blocks);
                pendingScene = null;
                copied.Clear(); // the files belong to the saved scene now
                created.Add(scene);
            }
            DialogueStatusText.Text = $"{created.Count} escena(s) creadas con {lines.Length} voces en «{target.Name}».";
        }
        catch (Exception ex)
        {
            foreach (var path in copied)
                try { File.Delete(path); } catch (IOException) { }
            if (pendingScene is Guid half) // a scene saved without its blocks is not left behind
                try { await repository.DeleteSceneAsync(half); } catch (Exception cleanup) { ErrorLog.Record(cleanup); }
            ErrorLog.Record(ex);
            DialogueStatusText.Text = (ex is OperationCanceledException ? "Exportación cancelada." : "Exportación interrumpida: " + ex.Message) +
                (created.Count > 0 ? $" Se conservaron {created.Count} escena(s) ya creadas." : "");
        }
        finally
        {
            _dialogueWork = null;
            SetDialogueBusy(false);
        }
        if (_currentRepository != repository) return;
        await RefreshVoiceLabAsync();
        await RefreshDialogueEpisodesAsync();
        if (created.Count == 0) return;
        await RefreshScriptAsync(created[0].EpisodeId, created[0].Id);
        if (MessageBox.Show(this, $"{created.Count} escena(s) creadas. Las voces están como voces grabadas: en Guion → Director IA elige " +
                $"«Montar voces grabadas» para montar cada escena{(single ? " o «Generar episodio» para repartirlas" : "")}.\n\n¿Ir a Guion?",
                "Exportar a escenas", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            ScriptTab.IsSelected = true;
    }
}

/// <summary>A line as it was when «Exportar a escenas» was confirmed.</summary>
internal sealed record DialogueExportLine(int Order, DialogueKind Kind, string Speaker, Guid? CharacterId, string Text, string Comment,
    int? Pitch, int? Speed, int? Volume, MainWindow.DialogueVoice? Voice)
{
    public static DialogueExportLine Of(DialogueRow row, MainWindow.DialogueVoice? voice) =>
        new(row.Order, row.Kind, row.Speaker, row.CharacterId, row.Text, row.Comment, row.Pitch, row.Speed, row.Volume, voice);
}
