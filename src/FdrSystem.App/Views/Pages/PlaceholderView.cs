using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Fallback para uma tag de navegação desconhecida. Já não há páginas por portar —
/// todas as rotas do <see cref="MainWindow"/> têm vista própria.
/// </summary>
public sealed class PlaceholderView : UserControl
{
    private sealed record PageInfo(string Title, string Subtitle, string Description, string Todo);

    private static readonly Dictionary<string, PageInfo> Pages = new(StringComparer.Ordinal)
    {
    };

    public static PlaceholderView For(string tag, MainWindow owner) =>
        new(Pages.TryGetValue(tag, out var info) ? info : new PageInfo(tag, "", "Página desconhecida.", ""));

    private PlaceholderView(PageInfo info)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 620,
        };

        panel.Children.Add(new TextBlock
        {
            Text = info.Title,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        panel.Children.Add(new TextBlock
        {
            Text = info.Subtitle,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Application.Current?.FindResource("TextMuted") as IBrush,
        });

        panel.Children.Add(new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = Application.Current?.FindResource("CardBg") as IBrush,
            BorderBrush = Application.Current?.FindResource("CardBorder") as IBrush,
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = info.Description, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 },
                    new TextBlock
                    {
                        Text = info.Todo,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Foreground = Application.Current?.FindResource("AmberText") as IBrush,
                    },
                },
            },
        });

        Content = panel;
    }
}
