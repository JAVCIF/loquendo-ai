using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LoquendoAI.App;

/// <summary>
/// Messages and questions with the app's theme (1.3.0). The Windows MessageBox is always light and white; this
/// class has the same name and the same Show(...) calls, and inside the LoquendoAI.App namespace it takes precedence
/// over System.Windows.MessageBox, so every existing call uses it without changes. It follows the chosen theme
/// (colours, dark title bar), has Spanish buttons, Esc/Enter as usual and Ctrl+C to copy the text.
/// </summary>
internal static class MessageBox
{
    public static MessageBoxResult Show(string text) => Show((Window?)null, text, "Loquendo AI");

    public static MessageBoxResult Show(string text, string caption, MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show((Window?)null, text, caption, button, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string text, string caption = "Loquendo AI",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        try
        {
            if (Application.Current is not { } app || !app.Dispatcher.CheckAccess())
                return System.Windows.MessageBox.Show(text, caption, button, icon, defaultResult);
            return ShowThemed(app, owner, text ?? "", caption ?? "", button, icon, defaultResult);
        }
        catch (Exception)
        {
            // Never lose a message (e.g. while the app is shutting down): the Windows one still works.
            return owner is not null
                ? System.Windows.MessageBox.Show(owner, text, caption, button, icon, defaultResult)
                : System.Windows.MessageBox.Show(text, caption, button, icon, defaultResult);
        }
    }

    private static MessageBoxResult ShowThemed(Application app, Window? owner, string text, string caption,
        MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        owner ??= app.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? app.MainWindow;
        if (owner is not null && !owner.IsVisible) owner = null;
        var dialog = new Window
        {
            Title = caption.Length > 0 ? caption : "Loquendo AI",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 380, MaxWidth = 700,
            ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };
        if (owner is not null) dialog.Owner = owner;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new DockPanel { Margin = new Thickness(20, 20, 22, 18) };
        if (Glyph(icon) is { } glyph)
        {
            var badge = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = glyph.Text, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            badge.SetResourceReference(Border.BackgroundProperty, glyph.Background);
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "Theme.AccentText");
            DockPanel.SetDock(badge, Dock.Left);
            body.Children.Add(badge);
        }
        body.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13.5, LineHeight = 20
        });
        layout.Children.Add(body);

        var bar = new Border { Padding = new Thickness(16, 10, 16, 10), BorderThickness = new Thickness(0, 1, 0, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, "Theme.SurfaceAlt");
        bar.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        Grid.SetRow(bar, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bar.Child = buttons;
        layout.Children.Add(bar);

        var choices = button switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.OK }
        };
        var preferred = choices.Contains(defaultResult) ? defaultResult : choices[0];
        // Closing with the window's X or Esc: Cancel when there is one, otherwise No, otherwise OK.
        var closed = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : choices.Contains(MessageBoxResult.No) ? MessageBoxResult.No : MessageBoxResult.OK;
        var result = closed;
        Button? focus = null;
        foreach (var choice in choices)
        {
            var item = new Button
            {
                Content = choice switch
                {
                    MessageBoxResult.Yes => "Sí", MessageBoxResult.No => "No", MessageBoxResult.Cancel => "Cancelar", _ => "Aceptar"
                },
                MinWidth = 92, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0),
                IsDefault = choice == preferred, IsCancel = choice == closed
            };
            if (choice == preferred && app.TryFindResource("AccentButton") is Style accent) item.Style = accent;
            item.Click += (_, _) =>
            {
                result = choice;
                dialog.Close();
            };
            if (choice == preferred) focus = item;
            buttons.Children.Add(item);
        }
        dialog.Content = layout;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                try { Clipboard.SetText((caption.Length > 0 ? caption + Environment.NewLine + Environment.NewLine : "") + text); }
                catch (Exception) { /* clipboard busy */ }
                e.Handled = true;
            }
        };
        dialog.Loaded += (_, _) => focus?.Focus();
        PlaySound(icon);
        dialog.ShowDialog();
        return result;
    }

    private static (string Text, string Background)? Glyph(MessageBoxImage icon) => icon switch
    {
        MessageBoxImage.Error => ("✕", "Theme.Accent"),
        MessageBoxImage.Warning => ("!", "Theme.Warning"),
        MessageBoxImage.Question => ("?", "Theme.Accent"),
        MessageBoxImage.Information => ("i", "Theme.Info"),
        _ => null
    };

    private static void PlaySound(MessageBoxImage icon)
    {
        try
        {
            switch (icon)
            {
                case MessageBoxImage.Error: System.Media.SystemSounds.Hand.Play(); break;
                case MessageBoxImage.Warning: System.Media.SystemSounds.Exclamation.Play(); break;
                case MessageBoxImage.Question: System.Media.SystemSounds.Question.Play(); break;
                case MessageBoxImage.Information: System.Media.SystemSounds.Asterisk.Play(); break;
            }
        }
        catch (Exception) { /* Cosmetic only. */ }
    }
}
