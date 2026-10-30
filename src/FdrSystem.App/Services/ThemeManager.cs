using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace FdrSystem.App.Services;

/// <summary>
/// Troca o dicionÃ¡rio de cores em tempo real (os cinco temas do WPF).
/// </summary>
public static class ThemeManager
{
    public static readonly string[] Names = ["Escuro", "Claro", "Meia-noite", "Floresta", "Ametista"];

    private static readonly string[] Files = ["Dark", "Light", "Midnight", "Forest", "Amethyst"];

    public static int CurrentIndex { get; private set; }

    public static void Apply(int index)
    {
        if (index < 0 || index >= Files.Length) index = 0;
        CurrentIndex = index;
        AppSettings.Current.Theme = Files[index];
        AppSettings.Save();

        if (Application.Current is { } app)
        {
            var uri = new Uri($"avares://FDR-SYSTEM/Themes/{Files[index]}.axaml");
            var dictionaries = app.Resources.MergedDictionaries;

            for (var i = dictionaries.Count - 1; i >= 0; i--)
            {
                if (dictionaries[i] is ResourceInclude include && include.Source?.ToString().Contains("/Themes/") == true)
                    dictionaries.RemoveAt(i);
            }

            dictionaries.Add(new ResourceInclude(uri) { Source = uri });

            // O tema Claro exige variante Light na FluentTheme para os controlos base.
            app.RequestedThemeVariant = Files[index] == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }

    public static void Cycle() => Apply((CurrentIndex + 1) % Files.Length);
}

