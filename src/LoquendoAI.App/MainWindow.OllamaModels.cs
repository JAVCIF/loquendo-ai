using System.Windows;
using System.Windows.Controls;

namespace LoquendoAI.App;

/// <summary>
/// Model selection (1.0.0-beta.3): the Director and image lists hold the visible models of every
/// configured provider ("gemma4:12b · Ollama", "gemini-2.5-flash · Gemini", "claude-sonnet-5 · Claude").
/// Each request goes to the provider of the chosen model. Provider-specific options (Gemini 3 thinking,
/// Claude effort) are shown only when the chosen model supports them.
/// </summary>
public partial class MainWindow
{
    private readonly AiProviderSettings _aiProvider = AiProviderSettings.Load();
    private bool _loadingAiModels;
    private bool _refreshAiPending;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreLayout();
        // The configuration opens on its own only until a default model has been chosen.
        if (!_aiProvider.IsConfigured) ShowAiProviderDialog();
        UpdateAiProviderLabels();
        await RefreshAiModelsAsync();
    }

    private async void ConfigureAi_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRequestCancellation is not null)
        {
            FooterText.Text = "Cancela la tarea de IA en curso antes de cambiar la configuración.";
            return;
        }
        if (ShowAiProviderDialog()) await RefreshAiModelsAsync(useDefaults: true);
    }

    private bool ShowAiProviderDialog()
    {
        var dialog = new AiProviderSelectionWindow(_aiProvider) { Owner = this };
        var chosen = dialog.ShowDialog() == true;
        UpdateAiProviderLabels();
        return chosen;
    }

    private void UpdateAiProviderLabels()
    {
        AiDirectorProviderLabel.Text = "Modelo:";
        AssetVisionProviderLabel.Text = "Visión:";
        AiDirectorIntroLabel.Text = "Selecciona una escena. El modelo elegido preparará un borrador revisable con los recursos catalogados.";
        AiDirectorModelBox.ToolTip = AssetVisionModelBox.ToolTip =
            "Modelos visibles de Ollama, Gemini, Claude y ChatGPT. Elige cuáles aparecen y los predeterminados en «Configurar IA».";
        AiDirectorStatusText.Text = "Escribe la premisa y elige un modelo.";
    }

    private async void RefreshOllamaModels_Click(object sender, RoutedEventArgs e) =>
        await RefreshAiModelsAsync();

    /// <summary>Asks Ollama for its installed models (local and quick) and rebuilds both lists from the
    /// cached catalogue of every provider. Remote catalogues are refreshed in «Configurar IA».</summary>
    private async Task RefreshAiModelsAsync(bool useDefaults = false)
    {
        if (_loadingAiModels) { _refreshAiPending = true; return; }
        _loadingAiModels = true;
        string? ollamaError = null;
        try
        {
            try
            {
                var installed = await OllamaDirectorClient.ListInstalledModelsAsync();
                _aiProvider.SetKnownModels("ollama", installed);
                try { _aiProvider.Save(); } catch (Exception) { /* The list still works for this session. */ }
            }
            catch (Exception ex) { ollamaError = ex.Message; }

            var visible = _aiProvider.VisibleModels();
            var previousDirector = AiDirectorModelBox.SelectedItem as AiModelChoice;
            var previousVision = AssetVisionModelBox.SelectedItem as AiModelChoice;
            var previousDialogue = DialogueModelBox.SelectedItem as AiModelChoice;
            // Vision list: models known to read images first (all Gemini/Claude with image input, Ollama by name).
            var vision = visible.OrderByDescending(IsLikelyVisionChoice).ToArray();
            AiDirectorModelBox.ItemsSource = visible;
            AssetVisionModelBox.ItemsSource = vision;
            AiModelChoice? Pick(IReadOnlyList<AiModelChoice> list, AiModelChoice? previous, string defaultKey) =>
                (useDefaults ? null : list.FirstOrDefault(x => x == previous)) ??
                list.FirstOrDefault(x => x.Key == defaultKey) ?? list.FirstOrDefault(x => x == previous) ?? list.FirstOrDefault();
            AiDirectorModelBox.SelectedItem = Pick(visible, previousDirector, _aiProvider.DirectorModel);
            AssetVisionModelBox.SelectedItem = Pick(vision, previousVision, _aiProvider.VisionModel);
            DialogueModelBox.ItemsSource = visible;
            DialogueModelBox.SelectedItem = Pick(visible, previousDialogue, _aiProvider.DirectorModel);
            UpdateAiEffortOptions();
            var counts = visible.GroupBy(x => x.ProviderName).Select(g => $"{g.Key} {g.Count()}");
            FooterText.Text = visible.Count == 0
                ? "No hay modelos en la lista: abre «Configurar IA» para conectar Ollama, Gemini, Claude o ChatGPT." +
                  (ollamaError is null ? "" : " " + ollamaError)
                : $"Modelos en la lista: {string.Join(", ", counts)}." + (ollamaError is null ? "" : " Ollama no respondió; se usa su lista guardada.");
        }
        finally
        {
            _loadingAiModels = false;
            if (_refreshAiPending)
            {
                _refreshAiPending = false;
                await RefreshAiModelsAsync();
            }
        }
    }

    private bool IsLikelyVisionChoice(AiModelChoice choice) => choice.Provider switch
    {
        "claude" or "openai" => _aiProvider.Has(choice, "vision"),
        "gemini" => true,
        _ => IsLikelyVisionModel(choice.Model)
    };

    private void AiModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loadingAiModels) return;
        // The session choice does not replace the defaults; those are set in «Configurar IA».
        if (ReferenceEquals(sender, AiDirectorModelBox)) UpdateAiEffortOptions();
    }

    /// <summary>Shows the reasoning option only for models that have one, with that provider's levels.</summary>
    private void UpdateAiEffortOptions()
    {
        var choice = AiDirectorModelBox.SelectedItem as AiModelChoice;
        (string Label, string[] Values, string Current)? options = choice switch
        {
            { Provider: "gemini" } when choice.Model.StartsWith("gemini-3", StringComparison.Ordinal) =>
                ("Razonamiento:", ["default", "low", "medium", "high"], _aiProvider.ThinkingLevel),
            { Provider: "claude" } when _aiProvider.Has(choice, "effort") =>
                ("Esfuerzo:", _aiProvider.Has(choice, "effort-max")
                    ? ["default", "low", "medium", "high", "max"] : ["default", "low", "medium", "high"], _aiProvider.ClaudeEffort),
            { Provider: "openai" } when OpenAiDirectorClient.IsReasoning(choice.Model) =>
                ("Razonamiento:", ["default", "low", "medium", "high"], _aiProvider.OpenAiEffort),
            _ => null
        };
        AiEffortLabel.Visibility = AiEffortCombo.Visibility = options is null ? Visibility.Collapsed : Visibility.Visible;
        if (options is not { } value) return;
        AiEffortLabel.Text = value.Label;
        AiEffortCombo.ItemsSource = value.Values.Select(x => new ComboBoxItem { Content = EffortName(x), Tag = x }).ToArray();
        AiEffortCombo.SelectedItem = AiEffortCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => x.Tag as string == value.Current)
            ?? AiEffortCombo.Items[0];
    }

    internal static string EffortName(string value) => value switch
    {
        "low" => "Bajo", "medium" => "Medio", "high" => "Alto", "max" => "Máximo", _ => "Predeterminado"
    };

    /// <summary>The effort for a request: the Director combo for the Director model, the default otherwise.</summary>
    private string EffortFor(AiModelChoice choice)
    {
        if (AiEffortCombo.Visibility == Visibility.Visible && AiDirectorModelBox.SelectedItem is AiModelChoice director &&
            director == choice && AiEffortCombo.SelectedItem is ComboBoxItem { Tag: string selected })
            return selected;
        return choice.Provider switch
        {
            "claude" => _aiProvider.ClaudeEffort, "openai" => _aiProvider.OpenAiEffort, _ => _aiProvider.ThinkingLevel
        };
    }

    private Task<string> GenerateAiResponseAsync(string provider, string model, string system,
        string prompt, object schema, string? image, CancellationToken token,
        IProgress<string>? progress = null, OllamaChatOptions? ollamaOptions = null)
    {
        var choice = new AiModelChoice(provider, model);
        var purpose = ollamaOptions?.Purpose ?? (image is null ? "director" : "imagen");
        return provider switch
        {
            "gemini" => GeminiDirectorClient.ChatAsync(model, system, prompt, schema, image,
                EffortFor(choice), token, progress, purpose),
            "claude" => ClaudeDirectorClient.ChatAsync(model, system, prompt, schema, image,
                _aiProvider.Has(choice, "effort") ? EffortFor(choice) : null,
                // Thinking tokens count toward max_tokens: leave room beyond the JSON itself.
                image is null ? Math.Max(16_384, (ollamaOptions?.MaxOutputTokens ?? 0) * 3) : 8_192,
                token, progress, purpose),
            "openai" => OpenAiDirectorClient.ChatAsync(model, system, prompt, schema, image, EffortFor(choice),
                // Reasoning tokens count toward max_completion_tokens, as in Claude.
                image is null ? Math.Max(16_384, (ollamaOptions?.MaxOutputTokens ?? 0) * 3) : 8_192,
                token, progress, purpose),
            _ => OllamaDirectorClient.ChatAsync(model, system, prompt, schema, image, token, progress, ollamaOptions)
        };
    }

    private static bool IsLikelyVisionModel(string model) =>
        new[] { "gemma3", "gemma4", "llava", "qwen2.5vl", "qwen3-vl", "llama3.2-vision",
            "moondream", "minicpm-v", "bakllava", "mistral-small3", "granite3.2-vision" }
            .Any(prefix => model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
