using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FdrSystem.App.Services;
using FdrSystem.Core.Setup;
using FdrSystem.Core.Tools;

namespace FdrSystem.App.Views.Pages;

/// <summary>Linha da lista de pacotes da página Instalação.</summary>
public sealed record SetupPackageRow(string Label, string Detail, bool Required, bool Installed, IBrush? StatusBrush)
{
    public string StatusText => Installed ? "instalado" : Required ? "em falta" : "opcional";
}

/// <summary>
/// Página Instalação: verifica o ambiente Linux e instala usbmuxd,
/// libimobiledevice e os motores opcionais com uma ação explícita.
/// A elevação passa pelo pkexec; a app não corre sudo nem guarda credenciais.
/// </summary>
public partial class InstalacaoView : UserControl
{
    private readonly MainWindow? _owner;
    private readonly ToolRunner _tools;

    private SetupEnvironment? _environment;
    private CancellationTokenSource? _cts;
    private bool _refreshing;
    private bool _running;

    // Construtor público sem dependências para o carregador XAML/designer.
    public InstalacaoView() : this(null, new ToolRunner())
    {
    }

    public InstalacaoView(MainWindow? owner, ToolRunner tools)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;
        _ = RefreshAsync();
    }

    /// <summary>Chamado pelo MainWindow ao abrir a página.</summary>
    public void EnsureRefreshed()
    {
        if (_running || _refreshing) return;
        _ = RefreshAsync();
    }

    // ------------------------------------------------------------------ ambiente

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        SetupRefreshButton.IsEnabled = false;

        try
        {
            _environment = await SetupEnvironment.CollectAsync(_tools);
            ApplyEnvironment(_environment);

            AppLog.Info(
                $"ambiente: {_environment.Distribution}; gestor={_environment.ManagerLabel}; "
                + $"pkexec={(_environment.HasPkexec ? "sim" : "não")}; "
                + $"em falta={(_environment.Missing.Count == 0 ? "nada" : string.Join(", ", _environment.Missing.Select(p => p.Name)))}",
                "Instalação");
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler o ambiente: " + ex.Message, "RedBoxBg", "RedText");
            AppLog.Error("Instalação: ler ambiente: " + ex.Message, "Instalação");
        }
        finally
        {
            _refreshing = false;
            SetupRefreshButton.IsEnabled = !_running;
        }
    }

    private void ApplyEnvironment(SetupEnvironment environment)
    {
        EnvironmentText.Text = environment.IsLinux
            ? $"Linux · {environment.Distribution}"
            : "Fora do Linux: esta página só instala dependências em Linux.";

        DistroText.Text = environment.Distribution;
        ManagerText.Text = environment.ManagerLabel;
        PkexecText.Text = environment.HasPkexec
            ? "presente — o pedido de administrador aparece numa janela do sistema"
            : "ausente — instala o polkit ou usa o terminal";

        UsbmuxdText.Text = environment.IsLinux
            ? $"socket {(environment.UsbmuxdSocket ? "presente" : "ausente")}"
              + $" · serviço {environment.UsbmuxdService ?? "indisponível"}"
            : "—";
        UdevText.Text = environment.UdevRulesPath ?? "não encontradas (o usbmuxd instala-as)";

        ManagerText.Foreground = environment.Manager != PackageManager.Unknown ? Brush("GreenText") : Brush("AmberText");
        PkexecText.Foreground = environment.HasPkexec ? Brush("GreenText") : Brush("AmberText");
        UsbmuxdText.Foreground = environment.UsbmuxdSocket ? Brush("GreenText") : Brush("AmberText");
        UdevText.Foreground = environment.UdevRulesPath is null ? Brush("AmberText") : Brush("GreenText");

        var rows = environment.Packages
            .Select(package => new SetupPackageRow(
                package.Label,
                package.Name,
                package.Required,
                package.Installed,
                package.Installed ? Brush("GreenText") : package.Required ? Brush("AmberText") : Brush("TextMuted")))
            .ToList();

        PackagesList.ItemsSource = rows;

        var missingEssential = environment.MissingEssential;
        PackagesHintText.Text = missingEssential.Count == 0
            ? "Dependências essenciais presentes. As opcionais podem faltar sem impedir o USB."
            : "Em falta: " + string.Join(", ", missingEssential.Select(p => p.Label)) + ".";
        PackagesHintText.Foreground = missingEssential.Count == 0 ? Brush("GreenText") : Brush("AmberText");

        if (!environment.IsLinux)
        {
            SetStatus("Ambiente Windows: usa o instalador do projeto Windows. Esta página é só para Linux.",
                "AmberBoxBg", "AmberText");
            InstallButton.IsEnabled = false;
            InstallButton.Content = BuildInstallContent("Instalar tudo");
            return;
        }

        if (environment.EssentialsInstalled && environment.Missing.Count == 0)
        {
            SetStatus("Tudo instalado: usbmuxd, libimobiledevice e os motores opcionais estão presentes.",
                "GreenBoxBg", "GreenText");
            InstallButton.IsEnabled = false;
        }
        else if (environment.EssentialsInstalled)
        {
            SetStatus("As dependências essenciais estão presentes. Ainda podes instalar os motores opcionais.",
                "GreenBoxBg", "GreenText");
            InstallButton.IsEnabled = environment.CanInstall && !_running;
        }
        else if (!environment.HasPkexec)
        {
            SetStatus("Falta o pkexec (polkit): instala o polkit ou copia o comando sudo ao lado.",
                "AmberBoxBg", "AmberText");
            InstallButton.IsEnabled = false;
        }
        else if (environment.Manager == PackageManager.Unknown)
        {
            SetStatus("Gestor de pacotes não reconhecido: copia o script e instala à mão.",
                "AmberBoxBg", "AmberText");
            InstallButton.IsEnabled = false;
        }
        else
        {
            SetStatus("Faltam dependências essenciais. «Instalar tudo» pede a palavra-passe de administrador "
                      + "e instala os pacotes em falta.", "AmberBoxBg", "AmberText");
            InstallButton.IsEnabled = environment.CanInstall && !_running;
        }

        InstallButton.Content = BuildInstallContent(environment.MissingEssential.Count > 0
            ? "Instalar dependências"
            : "Instalar opcionais");

        if (!_running) RunStatusText.Text = "Ainda não foi executada nenhuma instalação.";
    }

    private static StackPanel BuildInstallContent(string text)
    {
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7 };
        panel.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = (Geometry?)Application.Current?.FindResource("IconDownload"),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            Width = 13,
            Height = 13,
            Stretch = Stretch.Uniform,
        });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        return panel;
    }

    // ------------------------------------------------------------------ instalação

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (_environment is null || _running) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var missing = _environment.Missing;
        var list = missing.Count == 0
            ? "Não há pacotes em falta."
            : string.Join("\n", missing.Select(p => "· " + p.Label + (p.Required ? " (essencial)" : " (opcional)")));

        var ok = await Dialogs.ConfirmAsync(
            owner,
            "Instalar dependências",
            "O sistema vai pedir a palavra-passe de administrador numa janela própria (pkexec).\n\n"
            + list
            + "\n\nA instalação atualiza os índices do apt e pode usar a rede do repositório. Continuar?",
            "Instalar agora");
        if (!ok) return;

        await RunInstallAsync();
    }

    private async Task RunInstallAsync()
    {
        if (_environment is null || _running) return;

        _running = true;
        _cts = new CancellationTokenSource();

        LogText.Text = string.Empty;
        RunBar.IsIndeterminate = true;
        RunBar.Value = 0;
        RunStatusText.Text = "A pedir autorização e a instalar…";
        RunStatusText.Foreground = Brush("TextSecondary");
        InstallButton.IsEnabled = false;
        SetupRefreshButton.IsEnabled = false;
        CancelButton.IsEnabled = true;

        AppendLog("=== instalação iniciada ===");
        AppendLog("$ pkexec /bin/bash <script gerado pela app>");
        _owner?.SetStatus("Instalação: a pedir autorização…");
        AppLog.Info("instalação: início (" + _environment.ManagerLabel + ")", "Instalação");

        var script = _environment.BuildInstallScript(Environment.UserName);

        try
        {
            var run = await SetupRunner.RunAsync(script, AppendLog, _cts.Token);

            AppendLog(run.Summary);
            RunStatusText.Text = run.Summary;

            if (run.Ok)
            {
                RunBar.IsIndeterminate = false;
                RunBar.Value = 100;
                RunStatusText.Foreground = Brush("GreenText");
                SetStatus("Instalação concluída. Termina a sessão e volta a entrar para o grupo plugdev valer.",
                    "GreenBoxBg", "GreenText");
                _owner?.SetStatus("Instalação: concluída.");
                AppLog.Info("instalação: concluída (código 0) — " + run.ScriptPath, "Instalação");
            }
            else
            {
                RunBar.IsIndeterminate = false;
                RunBar.Value = 0;
                RunStatusText.Foreground = Brush("AmberText");
                SetStatus(run.Summary + " O script ficou em " + run.ScriptPath + ".",
                    "AmberBoxBg", "AmberText");
                _owner?.SetStatus("Instalação: " + run.Summary);
                AppLog.Warn($"instalação: código {run.ExitCode} (cancelada={run.Cancelled}) — {run.ScriptPath}", "Instalação");
            }
        }
        catch (Exception ex)
        {
            RunBar.IsIndeterminate = false;
            RunStatusText.Foreground = Brush("RedText");
            RunStatusText.Text = "Falha ao executar: " + ex.Message;
            SetStatus("Não foi possível executar o pkexec: " + ex.Message
                      + " Copia o comando sudo e corre-o no terminal.", "RedBoxBg", "RedText");
            AppendLog("ERRO: " + ex.Message);
            AppLog.Error("instalação: " + ex.Message, "Instalação");
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            CancelButton.IsEnabled = false;
            SetupRefreshButton.IsEnabled = true;
            await RefreshAsync();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!_running) return;
        AppendLog("=== cancelamento pedido ===");
        RunStatusText.Text = "A cancelar…";
        AppLog.Warn("instalação: cancelamento pedido", "Instalação");
        _cts?.Cancel();
    }

    private void SetupRefresh_Click(object? sender, RoutedEventArgs e) => EnsureRefreshed();

    // ------------------------------------------------------------------ área de transferência

    private async void CopyCommands_Click(object? sender, RoutedEventArgs e)
    {
        if (_environment is null) return;
        await CopyTextAsync(_environment.ManualScript, "Script completo copiado.");
    }

    private async void CopyTerminal_Click(object? sender, RoutedEventArgs e)
    {
        if (_environment is null) return;
        await CopyTextAsync(_environment.ManualCommand, "Comando sudo copiado.");
    }

    private async Task CopyTextAsync(string text, string message)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetStatus("Não foi possível aceder à área de transferência.", "AmberBoxBg", "AmberText");
                return;
            }

            await clipboard.SetTextAsync(text);
            _owner?.SetStatus("Instalação: " + message);
            AppLog.Info("instalação: " + message, "Instalação");
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível copiar: " + ex.Message, "RedBoxBg", "RedText");
            AppLog.Warn("instalação: copiar: " + ex.Message, "Instalação");
        }
    }

    // ------------------------------------------------------------------ registo

    private void AppendLog(string line)
    {
        void Do()
        {
            LogText.Text = (LogText.Text ?? string.Empty) + line + Environment.NewLine;
            LogText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
        }

        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }

    private void SetStatus(string text, string boxKey, string textKey)
    {
        SetupStatusText.Text = text;
        SetupStatusText.Foreground = Brush(textKey);
        SetupStatusBox.Background = Brush(boxKey);
    }

    private static IBrush? Brush(string key) =>
        Application.Current?.FindResource(key) as IBrush;
}
