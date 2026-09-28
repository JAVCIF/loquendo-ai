using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

/// <summary>
/// Voice Lab › «Voces en los selectores…» (1.4.0): which TTS voices the Editor («Voz»), Diálogos and Voces grabadas
/// offer. Lists every voice the engines report (Loquendo TTS7, SAPI4 through BALCON, SAPI5) with a check box, lets a
/// voice be heard, and adds by hand a voice the engine did not list. Nothing changes until «Guardar».
/// </summary>
internal sealed class VoiceSelectorWindow : Window
{
    private readonly VoiceSelectorSettings _settings;
    private Dictionary<string, IReadOnlyList<string>> _catalog;
    private readonly Func<Task<Dictionary<string, IReadOnlyList<string>>>> _rescan;
    private readonly Func<DirectVoiceRef, Task> _preview;
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Width = 220 };
    private readonly ComboBox _manualEngine = new() { Width = 150, DisplayMemberPath = "Name" };
    private readonly TextBox _manualName = new() { Width = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _rescanButton;

    public VoiceSelectorSettings Result => _settings;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Catalog => _catalog;

    public VoiceSelectorWindow(VoiceSelectorSettings current, IReadOnlyDictionary<string, IReadOnlyList<string>> catalog,
        Func<Task<Dictionary<string, IReadOnlyList<string>>>> rescan, Func<DirectVoiceRef, Task> preview)
    {
        _settings = new VoiceSelectorSettings { Voices = current.Voices.ToList() };
        _catalog = new Dictionary<string, IReadOnlyList<string>>(catalog, StringComparer.OrdinalIgnoreCase);
        _rescan = rescan;
        _preview = preview;
        Title = "Voces en los selectores · Loquendo AI";
        Width = 640; Height = 720; MinWidth = 520; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock
        {
            Text = "Marca las voces que quieres ver en «Voz» del Editor, en Diálogos y en Voces grabadas. " +
                   "Sin tocar nada: todas las de Loquendo TTS7, Juan y Antonio de SAPI4 (vía BALCON), y Juan y las IVONA de SAPI5. " +
                   "Si instalas voces nuevas, pulsa «Buscar voces instaladas» y márcalas.",
            TextWrapping = TextWrapping.Wrap
        });
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        tools.Children.Add(new TextBlock { Text = "Buscar:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _search.TextChanged += (_, _) => Rebuild();
        tools.Children.Add(_search);
        _rescanButton = SmallButton("Buscar voces instaladas", async () => await RescanAsync());
        tools.Children.Add(_rescanButton);
        tools.Children.Add(SmallButton("Predeterminadas", () =>
        {
            _settings.ResetToDefaults();
            Rebuild();
            _status.Text = "Vuelve a la lista predeterminada (se aplica al guardar).";
        }));
        top.Children.Add(tools);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new StackPanel();
        var manual = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        manual.Children.Add(new TextBlock { Text = "Añadir a mano:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _manualEngine.ItemsSource = VoiceSelectorSettings.Providers.Select(x => new EngineChoice(x, VoiceSelectorSettings.EngineName(x))).ToArray();
        _manualEngine.SelectedIndex = 0;
        manual.Children.Add(_manualEngine);
        _manualName.Margin = new Thickness(6, 0, 0, 0);
        _manualName.ToolTip = "Nombre exacto de la voz, como la lista el motor (p. ej. «IVONA 2 Enrique»).";
        manual.Children.Add(_manualName);
        manual.Children.Add(SmallButton("Añadir", () =>
        {
            if (_manualEngine.SelectedItem is not EngineChoice engine || _manualName.Text.Trim().Length == 0) return;
            _settings.AddManual(engine.Key, _manualName.Text);
            _status.Text = $"«{_manualName.Text.Trim()}» añadida a {engine.Name}.";
            _manualName.Clear();
            Rebuild();
        }));
        bottom.Children.Add(manual);
        bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12, 4, 12, 4), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = "Guardar", Padding = new Thickness(14, 4, 14, 4), IsDefault = true };
        save.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        root.Children.Add(ThemeManager.Themed(new Border
        {
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) }
        }, Border.BorderBrushProperty, "Theme.Border"));
        Content = root;
        Rebuild();
        Loaded += async (_, _) =>
        {
            // Nothing known yet (the project has not asked the engines): ask now.
            if (_catalog.Values.All(x => x.Count == 0)) await RescanAsync();
        };
    }

    private async Task RescanAsync()
    {
        _rescanButton.IsEnabled = false;
        _status.Text = "Preguntando a los motores qué voces hay instaladas…";
        try
        {
            _catalog = await _rescan();
            var counts = VoiceSelectorSettings.Providers.Select(x =>
                $"{VoiceSelectorSettings.EngineLabel(x)}: {(_catalog.TryGetValue(x, out var list) ? list.Count : 0)}");
            _status.Text = "Voces instaladas · " + string.Join(" · ", counts) +
                (_catalog.TryGetValue(VoiceSelectorSettings.Sapi4, out var sapi4) && sapi4.Count == 0
                    ? ". SAPI4 necesita BALCON en tools\\balcon (ver su README)." : ".");
            Rebuild();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _rescanButton.IsEnabled = true; }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var filter = _search.Text.Trim();
        foreach (var provider in VoiceSelectorSettings.Providers)
        {
            var installed = _catalog.TryGetValue(provider, out var list) ? list : [];
            var voices = _settings.Known(provider, installed)
                .Where(x => filter.Length == 0 || x.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            var shown = voices.Count(x => _settings.IsShown(provider, x) &&
                (installed.Contains(x, StringComparer.OrdinalIgnoreCase) || _settings.Choice(provider, x) == true));
            _list.Children.Add(new TextBlock
            {
                Text = $"{VoiceSelectorSettings.EngineName(provider)} · {shown} de {voices.Length} en los selectores",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, _list.Children.Count == 0 ? 0 : 12, 0, 4)
            });
            if (voices.Length == 0)
            {
                _list.Children.Add(Hint(installed.Count == 0 && filter.Length == 0
                    ? "Sin voces: el motor no respondió o no hay voces instaladas." : "Ninguna coincide con la búsqueda."));
                continue;
            }
            foreach (var voice in voices) _list.Children.Add(VoiceRow(provider, voice, installed.Contains(voice, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private FrameworkElement VoiceRow(string provider, string voice, bool installed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var play = new Button { Content = "▶", Width = 28, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Escuchar esta voz" };
        play.Click += async (_, _) =>
        {
            play.IsEnabled = false;
            try { await _preview(new DirectVoiceRef(provider, voice)); _status.Text = $"Sonando {voice}."; }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { play.IsEnabled = true; }
        };
        DockPanel.SetDock(play, Dock.Right);
        row.Children.Add(play);
        if (!installed)
        {
            var remove = new Button { Content = "Quitar", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Quitar de la lista esta voz añadida a mano" };
            remove.Click += (_, _) =>
            {
                _settings.Voices.RemoveAll(x => VoiceSelectorSettings.Canonical(x.Provider) == provider &&
                    string.Equals(x.Voice, voice, StringComparison.OrdinalIgnoreCase));
                Rebuild();
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
        }
        var check = new CheckBox
        {
            Content = installed ? voice : voice + "  (añadida a mano · no la listó el motor)",
            IsChecked = _settings.IsShown(provider, voice), VerticalAlignment = VerticalAlignment.Center
        };
        check.Checked += (_, _) => _settings.Set(provider, voice, true);
        check.Unchecked += (_, _) => _settings.Set(provider, voice, false);
        row.Children.Add(check);
        return row;
    }

    private static TextBlock Hint(string text) => ThemeManager.Themed(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 0)
    }, TextBlock.ForegroundProperty, "Theme.TextMuted");

    private static Button SmallButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private sealed record EngineChoice(string Key, string Name);
}
