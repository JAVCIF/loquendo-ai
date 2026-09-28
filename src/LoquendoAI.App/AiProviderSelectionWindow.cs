using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LoquendoAI.App;

/// <summary>
/// «Configurar IA» (1.0.0-beta.3; ChatGPT since 1.3.0): connect Ollama, Gemini, Claude and ChatGPT at the same time, choose which of
/// their models appear in the Director / image lists, and pick the default models and reasoning levels.
/// Changes apply only when «Guardar» is pressed.
/// </summary>
internal sealed class AiProviderSelectionWindow : Window
{
    private readonly AiProviderSettings _settings;
    private readonly Dictionary<string, List<string>> _known;
    private readonly Dictionary<string, bool> _visibility;
    private readonly Dictionary<string, List<string>> _capabilities;

    private readonly TextBlock _ollamaStatus = Hint("");
    private readonly PasswordBox _geminiKey = new() { Width = 280 };
    private readonly TextBlock _geminiStatus = Hint("");
    private readonly PasswordBox _claudeKey = new() { Width = 280 };
    private readonly TextBlock _claudeStatus = Hint("");
    private readonly PasswordBox _openAiKey = new() { Width = 280 };
    private readonly TextBlock _openAiStatus = Hint("");
    private readonly TextBox _filter = new() { Width = 260 };
    private readonly StackPanel _modelList = new();
    private readonly TextBlock _modelCount = Hint("");
    private readonly ComboBox _director = new() { Width = 360 };
    private readonly ComboBox _vision = new() { Width = 360 };
    private readonly ComboBox _geminiThinking = new() { Width = 170 };
    private readonly ComboBox _claudeEffort = new() { Width = 170 };
    private readonly ComboBox _openAiEffort = new() { Width = 170 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly HashSet<string> _busy = [];

    public AiProviderSelectionWindow(AiProviderSettings settings)
    {
        _settings = settings;
        _known = settings.KnownModels.ToDictionary(x => x.Key, x => x.Value.ToList());
        _visibility = new Dictionary<string, bool>(settings.ModelVisibility);
        _capabilities = settings.ModelCapabilities.ToDictionary(x => x.Key, x => x.Value.ToList());
        Title = "Configurar IA · Loquendo AI";
        Width = 780; Height = 820; MinWidth = 640; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock
        {
            Text = "Conecta los proveedores que quieras usar, elige qué modelos aparecen en las listas del Director y de la " +
                   "Biblioteca, y cuáles se usan por defecto. En cada lista puedes elegir cualquier modelo visible, de cualquier proveedor.",
            TextWrapping = TextWrapping.Wrap
        });

        content.Children.Add(Section("1 · Proveedores"));
        content.Children.Add(ProviderRow("Ollama (local)", null, _ollamaStatus, "Refrescar modelos", () => RefreshOllamaAsync(), null));
        content.Children.Add(ProviderRow("Gemini (Google)", _geminiKey, _geminiStatus, "Guardar clave y consultar",
            () => RefreshGeminiAsync(), () =>
            {
                AiProviderSettings.DeleteGeminiKey();
                _geminiKey.Clear();
                _known.Remove("gemini");
                _geminiStatus.Text = "Clave borrada y modelos de Gemini quitados de las listas" +
                    (AiProviderSettings.HasGeminiKey ? " (GEMINI_API_KEY sigue definida)." : ".");
                RebuildModels();
            }));
        content.Children.Add(ProviderRow("Claude (Anthropic)", _claudeKey, _claudeStatus, "Guardar clave y consultar",
            () => RefreshClaudeAsync(), () =>
            {
                AiProviderSettings.DeleteClaudeKey();
                _claudeKey.Clear();
                _known.Remove("claude");
                _claudeStatus.Text = "Clave borrada y modelos de Claude quitados de las listas" +
                    (AiProviderSettings.HasClaudeKey ? " (ANTHROPIC_API_KEY sigue definida)." : ".");
                RebuildModels();
            }));
        content.Children.Add(ProviderRow("ChatGPT (OpenAI)", _openAiKey, _openAiStatus, "Guardar clave y consultar",
            () => RefreshOpenAiAsync(), () =>
            {
                AiProviderSettings.DeleteOpenAiKey();
                _openAiKey.Clear();
                _known.Remove("openai");
                _openAiStatus.Text = "Clave borrada y modelos de ChatGPT quitados de las listas" +
                    (AiProviderSettings.HasOpenAiKey ? " (OPENAI_API_KEY sigue definida)." : ".");
                RebuildModels();
            }));
        content.Children.Add(Hint("Las claves se guardan cifradas en Windows para tu usuario (también valen GEMINI_API_KEY, ANTHROPIC_API_KEY y " +
            "OPENAI_API_KEY). La API de ChatGPT se paga aparte de la suscripción ChatGPT Plus (platform.openai.com). " +
            "Con Gemini, Claude o ChatGPT, el borrador envía la premisa y el catálogo a ese servicio y el análisis visual envía las imágenes " +
            "seleccionadas; el uso de la API se cobra en tu cuenta. Ollama trabaja en tu equipo."));

        content.Children.Add(Section("2 · Modelos en las listas"));
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        tools.Children.Add(new TextBlock { Text = "Buscar:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _filter.TextChanged += (_, _) => RebuildModelList();
        tools.Children.Add(_filter);
        tools.Children.Add(SmallButton("Marcar todos", () => SetFiltered(_ => true)));
        tools.Children.Add(SmallButton("Ninguno", () => SetFiltered(_ => false)));
        tools.Children.Add(SmallButton("Recomendados", () => SetFiltered(AiProviderSettings.DefaultVisible)));
        content.Children.Add(tools);
        content.Children.Add(ThemeManager.Themed(new Border
        {
            BorderThickness = new Thickness(1), Height = 250,
            Child = new ScrollViewer { Content = _modelList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6) }
        }, Border.BorderBrushProperty, "Theme.Border"));
        content.Children.Add(_modelCount);

        content.Children.Add(Section("3 · Predeterminados"));
        content.Children.Add(Label("Modelo del Director IA"));
        content.Children.Add(_director);
        content.Children.Add(Label("Modelo para analizar imágenes (Biblioteca)"));
        content.Children.Add(_vision);
        var levels = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        levels.Children.Add(new TextBlock { Text = "Razonamiento Gemini 3:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _geminiThinking.ItemsSource = new[] { "default", "low", "medium", "high" }.Select(Level).ToArray();
        levels.Children.Add(_geminiThinking);
        levels.Children.Add(new TextBlock { Text = "Esfuerzo Claude:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 6, 0) });
        _claudeEffort.ItemsSource = new[] { "default", "low", "medium", "high", "max" }.Select(Level).ToArray();
        levels.Children.Add(_claudeEffort);
        levels.Children.Add(new TextBlock { Text = "Razonamiento ChatGPT:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 6, 0) });
        _openAiEffort.ItemsSource = new[] { "default", "low", "medium", "high" }.Select(Level).ToArray();
        levels.Children.Add(_openAiEffort);
        content.Children.Add(levels);
        content.Children.Add(Hint("Se pueden cambiar para cada borrador junto al modelo del Director. Más esfuerzo = más lento y más tokens. " +
            "«Máximo» solo en los modelos de Claude que lo admiten."));
        Select(_geminiThinking, settings.ThinkingLevel);
        Select(_claudeEffort, settings.ClaudeEffort);
        Select(_openAiEffort, settings.OpenAiEffort);

        content.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12, 4, 12, 4), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "Guardar", Padding = new Thickness(14, 4, 14, 4), IsDefault = true };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _geminiStatus.Text = AiProviderSettings.HasGeminiKey ? Count("gemini", "Clave guardada.") : "Sin clave.";
        _claudeStatus.Text = AiProviderSettings.HasClaudeKey ? Count("claude", "Clave guardada.") : "Sin clave.";
        _openAiStatus.Text = AiProviderSettings.HasOpenAiKey ? Count("openai", "Clave guardada.") : "Sin clave.";
        Loaded += async (_, _) =>
        {
            RebuildModels();
            await RefreshOllamaAsync();
            // Remote catalogues are cached; ask only when a key exists and nothing is cached yet.
            if (AiProviderSettings.HasGeminiKey && !_known.ContainsKey("gemini")) await RefreshGeminiAsync();
            if (AiProviderSettings.HasClaudeKey && !_known.ContainsKey("claude")) await RefreshClaudeAsync();
            if (AiProviderSettings.HasOpenAiKey && !_known.ContainsKey("openai")) await RefreshOpenAiAsync();
        };
    }

    private string Count(string provider, string prefix) =>
        _known.TryGetValue(provider, out var list) && list.Count > 0 ? $"{prefix} {list.Count} modelo(s) guardados; «consultar» actualiza la lista." : prefix;

    private static ComboBoxItem Level(string value) => new() { Content = MainWindow.EffortName(value), Tag = value };

    private static void Select(ComboBox box, string value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag as string == value) ?? box.Items[0];

    private static string Selected(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "default";

    private static TextBlock Hint(string text) => ThemeManager.Themed(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
    }, TextBlock.ForegroundProperty, "Theme.TextMuted");

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 9, 0, 3) };

    private static TextBlock Section(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 18, 0, 6)
    };

    private static Button SmallButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private FrameworkElement ProviderRow(string name, PasswordBox? key, TextBlock status, string action, Func<Task> run, Action? remove)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = name, Width = 150, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        if (key is not null)
        {
            key.ToolTip = "Pega la API key y pulsa «Guardar clave y consultar». Vacía = usar la guardada.";
            row.Children.Add(key);
        }
        var button = new Button { Content = action, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += async (_, _) => await run();
        row.Children.Add(button);
        if (remove is not null)
        {
            var delete = new Button { Content = "Borrar clave", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(6, 0, 0, 0) };
            delete.Click += (_, _) => remove();
            row.Children.Add(delete);
        }
        panel.Children.Add(row);
        status.Margin = new Thickness(150, 2, 0, 0);
        panel.Children.Add(status);
        return panel;
    }

    private async Task RunOnce(string provider, TextBlock status, Func<Task<string>> work)
    {
        if (!_busy.Add(provider)) return;
        status.Text = "Consultando modelos…";
        try { status.Text = await work(); }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            _busy.Remove(provider);
            RebuildModels();
        }
    }

    private Task RefreshOllamaAsync() => RunOnce("ollama", _ollamaStatus, async () =>
    {
        var models = await OllamaDirectorClient.ListInstalledModelsAsync();
        _known["ollama"] = models.ToList();
        return models.Count == 0
            ? "Ollama responde pero no tiene modelos instalados (ollama pull …)."
            : $"{models.Count} modelo(s) instalados. Los nuevos aparecen marcados.";
    });

    private Task RefreshGeminiAsync() => RunOnce("gemini", _geminiStatus, async () =>
    {
        if (_geminiKey.Password.Length > 0) AiProviderSettings.SaveGeminiKey(_geminiKey.Password);
        var key = AiProviderSettings.GetGeminiKey();
        if (key.Length == 0) return "Pega tu API key de Gemini (Google AI Studio).";
        var models = await GeminiDirectorClient.ListModelsAsync(key);
        _geminiKey.Clear();
        _known["gemini"] = models.ToList();
        return $"Clave guardada. {models.Count} modelo(s); solo los principales quedan marcados al principio.";
    });

    private Task RefreshClaudeAsync() => RunOnce("claude", _claudeStatus, async () =>
    {
        if (_claudeKey.Password.Length > 0) AiProviderSettings.SaveClaudeKey(_claudeKey.Password);
        var key = AiProviderSettings.GetClaudeKey();
        if (key.Length == 0) return "Pega tu API key de Claude (platform.claude.com).";
        var models = await ClaudeDirectorClient.ListModelsAsync(key);
        _claudeKey.Clear();
        _known["claude"] = models.Select(x => x.Id).ToList();
        foreach (var model in models)
            _capabilities["claude|" + model.Id] = new[] { model.Vision ? "vision" : "", model.Effort ? "effort" : "",
                model.EffortMax ? "effort-max" : "" }.Where(x => x.Length > 0).ToList();
        return $"Clave guardada. {models.Count} modelo(s) con salida estructurada.";
    });

    private Task RefreshOpenAiAsync() => RunOnce("openai", _openAiStatus, async () =>
    {
        if (_openAiKey.Password.Length > 0) AiProviderSettings.SaveOpenAiKey(_openAiKey.Password);
        var key = AiProviderSettings.GetOpenAiKey();
        if (key.Length == 0) return "Pega tu API key de OpenAI (platform.openai.com → API keys).";
        var models = await OpenAiDirectorClient.ListModelsAsync(key);
        _openAiKey.Clear();
        _known["openai"] = models.Select(x => x.Id).ToList();
        foreach (var model in models)
            _capabilities["openai|" + model.Id] = new[] { model.Vision ? "vision" : "", model.Reasoning ? "reasoning" : "" }
                .Where(x => x.Length > 0).ToList();
        return $"Clave guardada. {models.Count} modelo(s) de chat; solo los principales quedan marcados al principio.";
    });

    private IReadOnlyList<AiModelChoice> AllChoices() => AiProviderSettings.Providers
        .SelectMany(provider => (_known.TryGetValue(provider, out var list) ? list : [])
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(model => new AiModelChoice(provider, model)))
        .ToArray();

    private bool IsListed(AiModelChoice choice) =>
        _visibility.TryGetValue(choice.Key, out var visible) ? visible : AiProviderSettings.DefaultVisible(choice);

    private IEnumerable<AiModelChoice> Filtered() =>
        AllChoices().Where(x => _filter.Text.Trim().Length == 0 ||
            x.ToString().Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase));

    private void SetFiltered(Func<AiModelChoice, bool> visible)
    {
        foreach (var choice in Filtered()) _visibility[choice.Key] = visible(choice);
        RebuildModels();
    }

    private void RebuildModels()
    {
        RebuildModelList();
        RebuildDefaults();
    }

    private void RebuildModelList()
    {
        _modelList.Children.Clear();
        foreach (var group in Filtered().GroupBy(x => x.Provider))
        {
            _modelList.Children.Add(new TextBlock
            {
                Text = AiProviderSettings.ProviderName(group.Key), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2)
            });
            foreach (var choice in group)
            {
                var caps = _capabilities.TryGetValue(choice.Key, out var list) ? list : [];
                var notes = new List<string>();
                if (caps.Contains("vision") || choice.Provider == "gemini") notes.Add("imágenes");
                if (caps.Contains("effort")) notes.Add(caps.Contains("effort-max") ? "esfuerzo hasta máximo" : "esfuerzo");
                if (caps.Contains("reasoning")) notes.Add("razonamiento");
                if (choice.Provider == "gemini" && choice.Model.StartsWith("gemini-3", StringComparison.Ordinal)) notes.Add("razonamiento");
                var box = new CheckBox
                {
                    Content = choice.Model + (notes.Count > 0 ? "   (" + string.Join(", ", notes) + ")" : ""),
                    IsChecked = IsListed(choice), Margin = new Thickness(12, 1, 0, 1)
                };
                box.Checked += (_, _) => { _visibility[choice.Key] = true; RebuildDefaults(); };
                box.Unchecked += (_, _) => { _visibility[choice.Key] = false; RebuildDefaults(); };
                _modelList.Children.Add(box);
            }
        }
        if (_modelList.Children.Count == 0)
            _modelList.Children.Add(Hint(AllChoices().Count == 0
                ? "Todavía no hay modelos: refresca Ollama o consulta Gemini, Claude o ChatGPT arriba."
                : "Ningún modelo coincide con la búsqueda."));
        var all = AllChoices();
        _modelCount.Text = $"{all.Count(IsListed)} de {all.Count} modelos aparecerán en las listas.";
    }

    private void RebuildDefaults()
    {
        var visible = AllChoices().Where(IsListed).ToArray();
        void Fill(ComboBox box, string savedKey)
        {
            var current = box.SelectedItem as AiModelChoice ?? AiModelChoice.Parse(savedKey);
            box.ItemsSource = visible;
            box.SelectedItem = visible.FirstOrDefault(x => x == current) ?? visible.FirstOrDefault();
        }
        Fill(_director, _settings.DirectorModel);
        Fill(_vision, _settings.VisionModel);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Keys typed but not yet used for a query are saved too.
            if (_geminiKey.Password.Length > 0) AiProviderSettings.SaveGeminiKey(_geminiKey.Password);
            if (_claudeKey.Password.Length > 0) AiProviderSettings.SaveClaudeKey(_claudeKey.Password);
            if (_openAiKey.Password.Length > 0) AiProviderSettings.SaveOpenAiKey(_openAiKey.Password);
            if (AllChoices().Count > 0 && _director.SelectedItem is not AiModelChoice)
            {
                _status.Text = "Marca al menos un modelo y elige el del Director.";
                return;
            }
            _settings.KnownModels = _known.ToDictionary(x => x.Key, x => x.Value.ToList());
            _settings.ModelVisibility = new Dictionary<string, bool>(_visibility);
            _settings.ModelCapabilities = _capabilities.ToDictionary(x => x.Key, x => x.Value.ToList());
            _settings.DirectorModel = (_director.SelectedItem as AiModelChoice)?.Key ?? "";
            _settings.VisionModel = (_vision.SelectedItem as AiModelChoice)?.Key ?? "";
            _settings.ThinkingLevel = Selected(_geminiThinking);
            _settings.ClaudeEffort = Selected(_claudeEffort);
            _settings.OpenAiEffort = Selected(_openAiEffort);
            _settings.Version = 2;
            _settings.Save();
            DialogResult = true;
        }
        catch (Exception ex) { _status.Text = "No se pudo guardar la configuración: " + ex.Message; }
    }
}
