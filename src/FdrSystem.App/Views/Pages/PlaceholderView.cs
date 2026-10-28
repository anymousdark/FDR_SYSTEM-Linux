using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FdrSystem.App.Views.Pages;

/// <summary>
/// Páginas ainda não portadas: mostram o que a versão Windows faz e o estado do
/// port, para a navegação ficar completa desde o início.
/// </summary>
public sealed class PlaceholderView : UserControl
{
    private sealed record PageInfo(string Title, string Subtitle, string Description, string Todo);

    private static readonly Dictionary<string, PageInfo> Pages = new(StringComparer.Ordinal)
    {
        ["online"] = new("Online", "Firmware e restauro (usa a rede)",
            "Consulta o catálogo da Apple, descarrega IPSW com motor segmentado, verifica SHA-256 e restaura via idevicerestore.",
            "Falta: catálogo api.ipsw.me, DownloadManager e execução do idevicerestore do sistema."),
        ["restauro"] = new("Restauro", "Progresso em direto",
            "Duas barras (preparação do IPSW e gravação no iPhone), fase atual em português e saída completa do motor.",
            "Falta: correr idevicerestore e ler o progresso. Depende da página Online."),
        ["reparo"] = new("Reparo", "Estado do aparelho",
            "Verificar modo, entrar/sair de recovery, reiniciar, ativação/iCloud (deteção), MDM (só deteção) e pular configuração inicial.",
            "Falta: portar os comandos e os painéis. Base USB já existe no core."),
        ["firmware"] = new("Firmware", "Todos os iPhones",
            "Catálogo de todas as versões por modelo, assinadas e expiradas, com download por linha.",
            "Falta: catálogo e a lista de downloads partilhada com a página Online."),
        ["driver"] = new("Driver", "Acesso USB no Linux",
            "No Linux não há drivers Apple a instalar: o acesso é feito pelo usbmuxd + regras udev do pacote libimobiledevice-utils.",
            "Falta: mostrar o estado do usbmuxd/udev e o botão de regras de permissão."),
        ["jailbreak"] = new("Jailbreak", "checkm8 (opcional)",
            "No lugar do WinRa1n (Windows-only), a versão Linux abre o palera1n ou o checkra1n instalados no sistema.",
            "Falta: detetar a ferramenta e mostrar avisos de compatibilidade (A7–A11, iOS 12–17)."),
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
