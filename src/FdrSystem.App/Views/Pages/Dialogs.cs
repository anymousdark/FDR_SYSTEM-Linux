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

    /// <summary>Confirmação modal simples, usada antes de apagar ficheiros do iPhone.</summary>
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmText = "Confirmar")
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimary"),
            FontSize = 11.5,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var confirm = new Button { Content = confirmText, Padding = new Thickness(12, 6) };
        confirm.Classes.Add("primary");
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12, 6) };
        cancel.Classes.Add("tool");

        var window = CreateShell(owner, title, 480, 220, panel);
        window.CanResize = false;

        confirm.Click += (_, _) => window.Close(true);
        cancel.Click += (_, _) => window.Close(false);
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        return await window.ShowDialog<bool>(owner);
    }

    /// <summary>Pede um nome (por exemplo, uma pasta nova) sem usar APIs específicas do Windows.</summary>
    public static async Task<string?> PromptAsync(
        Window owner,
        string title,
        string message,
        string initial = "")
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondary"),
            FontSize = 11.5,
        });

        var box = new TextBox { Text = initial, Padding = new Thickness(7, 5) };
        panel.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var ok = new Button { Content = "OK", Padding = new Thickness(12, 6) };
        ok.Classes.Add("primary");
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12, 6) };
        cancel.Classes.Add("tool");

        var window = CreateShell(owner, title, 480, 230, panel);
        window.CanResize = false;
        window.Loaded += (_, _) => box.Focus();

        ok.Click += (_, _) => window.Close(box.Text?.Trim() ?? string.Empty);
        cancel.Click += (_, _) => window.Close(null);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        return await window.ShowDialog<string?>(owner);
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
