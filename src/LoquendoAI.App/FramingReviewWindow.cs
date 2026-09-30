using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.App;

/// <summary>
/// «Revisar encuadre» (1.4.4): one row per block with a framing warning, a remembered fix or a «es a propósito» mark.
/// Each row offers what can be done with it (FramingAudit fixes, «Deshacer», «Es a propósito»); size fixes show an
/// editable percentage. The window only collects the choices (<see cref="Choices"/>); MainWindow applies them through
/// FramingMemory.
/// </summary>
internal sealed class FramingReviewWindow : Window
{
    private const string Keep = "";

    private sealed record Option(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record Row(Guid BlockId, ComboBox Options, TextBox? Scale, string Current, bool Pending);

    private readonly List<Row> _rows = [];

    /// <summary>What to do with each block: a fix id, FramingMemory.Intended or FramingMemory.Undone, and the size percentage.</summary>
    public List<(Guid BlockId, string Choice, double? Scale)> Choices { get; } = [];

    /// <summary>A block to open in the editor after closing («Ver en el editor»).</summary>
    public Guid? OpenBlockId { get; private set; }

    public FramingReviewWindow(IReadOnlyList<FramingIssue> issues, FramingMemory memory,
        IReadOnlyDictionary<Guid, SceneScriptBlock> blocks, Func<SceneScriptBlock, string> name)
    {
        Title = "Revisar encuadre";
        Width = 920; Height = 560; MinWidth = 700; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var layout = new DockPanel { Margin = new Thickness(14) };
        var intro = Muted("Renders que terminan fuera del cuadro o se ven muy pequeños. «Limitar al cuadro» conserva el movimiento " +
            "(un empujón, una pelea) y solo lo acorta; «Era una entrada» lo hace entrar desde fuera. Cada corrección se recuerda: " +
            "al volver aquí puedes cambiarla o deshacerla. Si editas el bloque a mano, se revisa de nuevo.");
        intro.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(intro, Dock.Top);
        layout.Children.Add(intro);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);

        var list = new StackPanel();
        var pending = memory.Pending(issues);
        foreach (var issue in pending.Where(x => blocks.ContainsKey(x.BlockId)))
            AddRow(list, blocks[issue.BlockId], name, issue.Message, issue.Fixes.Select(x => new Option(x.Id, x.Label)),
                issue.Problem != FramingProblem.OutOfFrame ? issue.Scale : null, current: Keep, pendingRow: true,
                memory.Of(issue.BlockId) is { Choice: not FramingMemory.Undone } previous ? previous : null);
        // Corrected before (still as they were left) and marked «es a propósito»: can switch, undo or be warned again.
        foreach (var saved in memory.Records.Where(x => x.Choice != FramingMemory.Undone && blocks.ContainsKey(x.BlockId) &&
                     !pending.Any(p => p.BlockId == x.BlockId)).OrderBy(x => blocks[x.BlockId].OrderIndex))
        {
            var chosen = saved.Choice == FramingMemory.Intended ? "marcado «es a propósito»"
                : "corregido: " + (saved.Fixes.FirstOrDefault(x => x.Id == saved.Choice)?.Label ?? saved.Choice).ToLowerInvariant();
            AddRow(list, blocks[saved.BlockId], name, $"{saved.Message} ({chosen})",
                saved.Fixes.Select(x => new Option(x.Id, x.Label)),
                saved.Problem != FramingProblem.OutOfFrame ? saved.Scale : null, current: saved.Choice, pendingRow: false, saved);
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        layout.Children.Add(scroll);
        if (_rows.Count == 0)
            list.Children.Add(Muted("No hay avisos de encuadre en esta escena."));

        var fixAll = new Button { Content = "Corregir todo lo pendiente", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        fixAll.Click += (_, _) =>
        {
            foreach (var row in _rows.Where(x => x.Pending))
                row.Options.SelectedItem = row.Options.Items.OfType<Option>().FirstOrDefault(x => x.Id is not (Keep or FramingMemory.Intended));
        };
        fixAll.IsEnabled = _rows.Any(x => x.Pending);
        var apply = new Button { Content = "Aplicar", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        apply.Click += (_, _) => Accept();
        var close = new Button { Content = "Cerrar", Padding = new Thickness(12, 5, 12, 5), IsCancel = true };
        buttons.Children.Add(fixAll);
        buttons.Children.Add(apply);
        buttons.Children.Add(close);
        Content = layout;
    }

    private void AddRow(StackPanel list, SceneScriptBlock block, Func<SceneScriptBlock, string> name, string message,
        IEnumerable<Option> fixes, double? scale, string current, bool pendingRow, FramingRecord? remembered)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock
        {
            Text = $"#{block.OrderIndex + 1} · {message}", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = name(block)
        };
        row.Children.Add(text);

        var options = new ComboBox { Width = 230, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        options.Items.Add(new Option(Keep, pendingRow ? "Dejar como está (por ahora)" : "Dejar como está"));
        foreach (var fix in fixes)
            if (fix.Id != current) options.Items.Add(fix);
        if (current != FramingMemory.Intended) options.Items.Add(new Option(FramingMemory.Intended, "Es a propósito (no avisar)"));
        if (remembered is not null) options.Items.Add(new Option(FramingMemory.Undone,
            current == FramingMemory.Intended ? "Volver a avisar" : "Deshacer (volver a como estaba)"));
        options.SelectedIndex = 0;
        Grid.SetColumn(options, 1);
        row.Children.Add(options);

        TextBox? percent = null;
        if (scale is double proposal)
        {
            var box = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            percent = new TextBox
            {
                Width = 52, Text = Math.Round(proposal * 100).ToString(CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Tamaño final respecto al actual (140 = 40 % más grande). Cámbialo si el primer ajuste no convence."
            };
            box.Children.Add(percent);
            box.Children.Add(new TextBlock { Text = " %", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(box, 2);
            row.Children.Add(box);
        }

        var open = new Button { Content = "Ver en el editor", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 0) };
        open.Click += (_, _) =>
        {
            OpenBlockId = block.Id;
            DialogResult = false;
        };
        Grid.SetColumn(open, 3);
        row.Children.Add(open);
        list.Children.Add(row);
        _rows.Add(new Row(block.Id, options, percent, current, pendingRow));
    }

    private void Accept()
    {
        Choices.Clear();
        foreach (var row in _rows)
        {
            if (row.Options.SelectedItem is not Option { Id: var choice } || choice == Keep) continue;
            double? scale = null;
            if (row.Scale is not null)
            {
                if (!double.TryParse(row.Scale.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ||
                    percent is < 20 or > 500)
                {
                    MessageBox.Show(this, "El tamaño va en porcentaje, entre 20 y 500 (140 = 40 % más grande).", "Revisar encuadre",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                scale = percent / 100;
            }
            Choices.Add((row.BlockId, choice, scale));
        }
        DialogResult = true;
    }

    private static TextBlock Muted(string text) => ThemeManager.Themed(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap
    }, TextBlock.ForegroundProperty, "Theme.TextMuted");
}
