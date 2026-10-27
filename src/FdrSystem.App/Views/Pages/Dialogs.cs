using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FdrSystem.Core.Devices;

namespace FdrSystem.App.Views.Pages;

/// <summary>Janelas simples de detalhe (tabela de campos e painel da bateria).</summary>
public static class Dialogs
{
    public static void ShowTable(Window owner, string title, IReadOnlyList<DisplayRow> rows)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Single,
        };

        grid.Columns.Add(new DataGridTextColumn { Header = "Grupo", Binding = new Avalonia.Data.Binding("Group"), Width = new DataGridLength(130) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Campo", Binding = new Avalonia.Data.Binding("Label"), Width = new DataGridLength(260) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Valor", Binding = new Avalonia.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var window = CreateShell(owner, title, 900, 620, grid);
        window.ShowDialog(owner);
    }

    public static void ShowBattery(Window owner, BatteryReport battery, bool reveal)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(18) };

        panel.Children.Add(new TextBlock
        {
            Text = battery.Level is { } level ? $"{level:0}%" : "—",
            FontSize = 30,
            FontWeight = FontWeight.Bold,
        });

        panel.Children.Add(new TextBlock
        {
            Text = BatteryParser.InfoList(battery, reveal).Count == 0
                ? "Sem dados de bateria."
                : BatteryParser.ChargingSummary(battery),
            Foreground = Brush("TextSecondary"),
            FontSize = 11,
        });

        panel.Children.Add(new Border { Height = 1, Background = Brush("CardBorder"), Margin = new Thickness(0, 8) });

        foreach (var (label, value) in BatteryParser.InfoList(battery, reveal))
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
            var caption = new TextBlock { Text = label, Foreground = Brush("TextMuted"), FontSize = 11 };
            var text = new TextBlock { Text = value, Foreground = Brush("TextPrimary"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            line.Children.Add(caption);
            line.Children.Add(text);
            panel.Children.Add(line);
        }

        var scroll = new ScrollViewer { Content = panel };
        var window = CreateShell(owner, "Detalhes da bateria", 640, 560, scroll);
        window.ShowDialog(owner);
    }

    private static Window CreateShell(Window owner, string title, double width, double height, Control content)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 480,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("WindowBg"),
            Content = content,
        };

        if (owner.Resources.TryGetValue("TextPrimary", out var foreground) && foreground is IBrush brush)
            window.Foreground = brush;

        return window;
    }

    private static IBrush? Brush(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as IBrush : null;
}
