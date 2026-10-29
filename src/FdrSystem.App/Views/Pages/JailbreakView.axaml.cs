using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FdrSystem.App.Services;
using FdrSystem.Core.Tools;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Página Jailbreak: deteta <c>palera1n</c> e <c>checkra1n</c>, mostra caminho,
/// versão e comandos. Nunca executa as ferramentas, nunca corre <c>sudo</c> e não
/// serve para contornar iCloud/Activation Lock/MDM.
/// </summary>
public partial class JailbreakView : UserControl
{
    private readonly ToolRunner _tools;

    private bool _busy;

    // Construtor público sem dependências para o carregador XAML/designer.
    public JailbreakView() : this(new ToolRunner())
    {
    }

    public JailbreakView(ToolRunner tools)
    {
        InitializeComponent();
        _tools = tools;

        _ = RefreshToolsAsync();
        _ = RefreshDeviceAsync();
    }

    /// <summary>Chamado pelo MainWindow ao abrir a página.</summary>
    public void EnsureRefreshed()
    {
        if (_busy) return;
        _ = RefreshToolsAsync();
        _ = RefreshDeviceAsync();
    }

    // ------------------------------------------------------------------ ferramentas

    private async void RefreshTools_Click(object? sender, RoutedEventArgs e) => await RefreshToolsAsync();

    private async Task RefreshToolsAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshToolsButton.IsEnabled = false;

        try
        {
            var palera1n = _tools.Locate("palera1n");
            var checkra1n = _tools.Locate("checkra1n");

            if (palera1n is null)
            {
                Palera1nText.Text = "Não instalado. Instala a partir de palera1n.com e volta a procurar.";
                Palera1nText.Foreground = Brush("TextFaint");
            }
            else
            {
                var version = await TryVersionAsync("palera1n");
                Palera1nText.Text = palera1n + (version is null ? "" : $"\nversão: {version}");
                Palera1nText.Foreground = Brush("GreenText");
            }

            if (checkra1n is null)
            {
                Checkra1nText.Text = "Não instalado. Instala a partir de checkra.in e volta a procurar.";
                Checkra1nText.Foreground = Brush("TextFaint");
            }
            else
            {
                // O checkra1n é uma app gráfica: não o executamos para ler a versão.
                Checkra1nText.Text = checkra1n + "\nversão: — (app gráfica; executa no terminal)";
                Checkra1nText.Foreground = Brush("GreenText");
            }

            HeaderStatusText.Text = palera1n is null && checkra1n is null
                ? "Nenhuma ferramenta de jailbreak encontrada"
                : "Ferramentas encontradas: "
                  + string.Join(" · ", new[] { palera1n is null ? null : "palera1n", checkra1n is null ? null : "checkra1n" }
                      .Where(x => x is not null));

            AppLog.Info(
                $"deteção: palera1n={(palera1n ?? "não instalado")} checkra1n={(checkra1n ?? "não instalado")}",
                "Jailbreak");
        }
        catch (Exception ex)
        {
            HeaderStatusText.Text = "Falha ao procurar ferramentas";
            AppLog.Error("jailbreak: procurar ferramentas: " + ex.Message, "Jailbreak");
        }
        finally
        {
            _busy = false;
            RefreshToolsButton.IsEnabled = true;
        }
    }

    /// <summary>Lê <c>--version</c>; devolve null se não responder ou falhar.</summary>
    private async Task<string?> TryVersionAsync(string tool)
    {
        try
        {
            var run = await _tools.RunAsync(tool, ["--version"], TimeSpan.FromSeconds(4));
            if (run is null) return null;

            var output = (run.StdOut + "\n" + run.StdErr).Trim();
            if (output.Length == 0) return null;

            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return line is { Length: > 0 and < 120 } ? line : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"{tool} --version: {ex.Message}", "Jailbreak");
            return null;
        }
    }

    // ------------------------------------------------------------------ aparelho

    private async void RefreshDevice_Click(object? sender, RoutedEventArgs e) => await RefreshDeviceAsync();

    private async Task RefreshDeviceAsync()
    {
        try
        {
            var type = await _tools.RunAsync("ideviceinfo", ["-k", "ProductType"], TimeSpan.FromSeconds(6));
            var version = await _tools.RunAsync("ideviceinfo", ["-k", "ProductVersion"], TimeSpan.FromSeconds(6));

            var productType = CleanValue(type?.StdOut);
            var productVersion = CleanValue(version?.StdOut);

            if (productType is null)
            {
                DeviceText.Text = "Nenhum iPhone detetado (ou desbloqueado e confiado)";
                DeviceHintText.Text = "Liga o iPhone, desbloqueia e aceita «Confiar neste computador». "
                                      + "Em recovery/DFU não é possível ler o modelo por aqui.";
                return;
            }

            DeviceText.Text = productType + (productVersion is null ? "" : $" · iOS {productVersion}");
            DeviceHintText.Text = "Só informação. Confirma no site oficial se este modelo e versão são "
                                  + "suportados — a app não garante compatibilidade nem executa o jailbreak.";
        }
        catch (Exception ex)
        {
            DeviceText.Text = "Falha ao ler o aparelho";
            AppLog.Warn("jailbreak: ler aparelho: " + ex.Message, "Jailbreak");
        }
    }

    private static string? CleanValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();
        if (text.Length is > 0 and < 40 && !text.Contains('<') && !text.Contains('\n')) return text;
        return null;
    }

    // ------------------------------------------------------------------ ações

    private async void CopyPalera1n_Click(object? sender, RoutedEventArgs e)
        => await CopyAsync(Palera1nCommandBox.Text, "sudo palera1n -l");

    private async void CopyPalera1nRootful_Click(object? sender, RoutedEventArgs e)
        => await CopyAsync(Palera1nRootfulBox.Text, "sudo palera1n -c -f");

    private async void CopyCheckra1n_Click(object? sender, RoutedEventArgs e)
        => await CopyAsync(Checkra1nCommandBox.Text, "sudo checkra1n");

    private async Task CopyAsync(string? boxText, string fallback)
    {
        var command = string.IsNullOrWhiteSpace(boxText) ? fallback : boxText.Trim();

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            CopyStatusText.Text = "Sem acesso à área de transferência. Comando: " + command;
            return;
        }

        await clipboard.SetTextAsync(command);
        CopyStatusText.Text = "Copiado: " + command + "\nExecuta-o tu no terminal (a app não corre sudo).";
        AppLog.Info("comando de jailbreak copiado: " + command, "Jailbreak");
    }

    private void OpenPalera1nSite_Click(object? sender, RoutedEventArgs e)
        => OpenUrl("https://palera.in/", "https://palera1n.com/");

    private void OpenCheckra1nSite_Click(object? sender, RoutedEventArgs e)
        => OpenUrl("https://checkra.in/");

    private void OpenUrl(params string[] candidates)
    {
        foreach (var url in candidates)
        {
            try
            {
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
                CopyStatusText.Text = "A abrir " + url;
                AppLog.Info("abrir ligação oficial: " + url, "Jailbreak");
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"abrir {url}: {ex.Message}", "Jailbreak");
            }
        }

        CopyStatusText.Text = "Não foi possível abrir o navegador: " + candidates[0];
    }

    private static Avalonia.Media.IBrush? Brush(string key) =>
        Avalonia.Application.Current?.FindResource(key) as Avalonia.Media.IBrush;
}
