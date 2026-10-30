using System.Text;
using FdrSystem.Core.Tools;

namespace FdrSystem.Core.Setup;

/// <summary>Gestor de pacotes detetado na distribuição.</summary>
public enum PackageManager
{
    Unknown,
    Apt,
    Dnf,
    Pacman,
}

/// <summary>Pacote que a página Instalação sabe instalar, com o estado atual.</summary>
public sealed record SetupPackage(string Name, string Label, bool Required, bool Installed);

/// <summary>
/// Estado do ambiente Linux relevante para o FDR SYSTEM: distribuição, gestor de
/// pacotes, elevação por polkit e as bibliotecas de que o USB depende.
/// A recolha é só de leitura; nada é instalado sem uma ação explícita do utilizador.
/// </summary>
public sealed record SetupEnvironment(
    bool IsLinux,
    string Distribution,
    PackageManager Manager,
    bool HasPkexec,
    IReadOnlyList<SetupPackage> Packages,
    bool UsbmuxdSocket,
    string? UsbmuxdService,
    string? UdevRulesPath)
{
    public string ManagerLabel => Manager switch
    {
        PackageManager.Apt => "apt (Debian/Ubuntu/Kali)",
        PackageManager.Dnf => "dnf (Fedora)",
        PackageManager.Pacman => "pacman (Arch)",
        _ => "não reconhecido",
    };

    public IReadOnlyList<SetupPackage> Missing => Packages.Where(p => !p.Installed).ToList();

    public IReadOnlyList<SetupPackage> MissingEssential =>
        Packages.Where(p => p.Required && !p.Installed).ToList();

    /// <summary>Há gestor de pacotes e polkit: a instalação automática é possível.</summary>
    public bool CanInstall => IsLinux && HasPkexec && Manager != PackageManager.Unknown && Missing.Count > 0;

    /// <summary>Verdadeiro quando todas as dependências essenciais já estão presentes.</summary>
    public bool EssentialsInstalled => Packages.Where(p => p.Required).All(p => p.Installed);

    /// <summary>Comando de uma linha que o utilizador pode copiar se preferir o terminal.</summary>
    public string ManualCommand => Manager switch
    {
        PackageManager.Apt =>
            "sudo apt update && sudo apt install -y usbmuxd libimobiledevice-utils libimobiledevice-1.0-6 "
            + "&& sudo systemctl enable --now usbmuxd",
        PackageManager.Dnf =>
            "sudo dnf install -y usbmuxd libimobiledevice-utils libimobiledevice "
            + "&& sudo systemctl enable --now usbmuxd",
        PackageManager.Pacman =>
            "sudo pacman -S --needed usbmuxd libimobiledevice && sudo systemctl enable --now usbmuxd",
        _ => "Instala o usbmuxd e o libimobiledevice com o gestor de pacotes da tua distribuição.",
    };

    /// <summary>Script completo mostrado/copiado na página (sem executar).</summary>
    public string ManualScript => BuildInstallScript(Environment.UserName, "manual");

    /// <summary>
    /// Gera o script que a página corre por <c>pkexec</c>. Não recebe input livre:
    /// só o nome de utilizador, validado, entra no texto.
    /// </summary>
    public string BuildInstallScript(string? user, string mode = "auto")
    {
        var name = SafeUser(user);
        var sb = new StringBuilder();

        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("# FDR SYSTEM — instalação de dependências Linux (" + mode + ")");
        sb.AppendLine("# Gerado pela aplicação. Revê antes de correr fora da app.");
        sb.AppendLine("set -u");
        sb.AppendLine("fail=0");
        sb.AppendLine("step() { printf '\\n==> %s\\n' \"$*\"; }");
        sb.AppendLine("missing=\"\"");
        sb.AppendLine();

        switch (Manager)
        {
            case PackageManager.Apt:
                AppendApt(sb, name);
                break;
            case PackageManager.Dnf:
                AppendDnf(sb, name);
                break;
            case PackageManager.Pacman:
                AppendPacman(sb, name);
                break;
            default:
                sb.AppendLine("echo 'ERRO: gestor de pacotes desconhecido.'");
                sb.AppendLine("exit 2");
                return sb.ToString();
        }

        AppendVerification(sb);
        return sb.ToString();
    }

    private static void AppendApt(StringBuilder sb, string? name)
    {
        sb.AppendLine("export DEBIAN_FRONTEND=noninteractive");
        sb.AppendLine();
        sb.AppendLine("step 'Atualizar índices (apt-get update)'");
        sb.AppendLine("apt-get update || { echo 'AVISO: apt-get update falhou; a tentar com os índices existentes.'; fail=1; }");
        sb.AppendLine();
        sb.AppendLine("step 'Dependências essenciais'");
        sb.AppendLine("install_essentials() {");
        sb.AppendLine("  local lib");
        sb.AppendLine("  for lib in libimobiledevice-1.0-6 libimobiledevice-1.0-6t64; do");
        sb.AppendLine("    if apt-get install -y --no-install-recommends usbmuxd libimobiledevice-utils \"$lib\"; then return 0; fi");
        sb.AppendLine("  done");
        sb.AppendLine("  apt-get install -y --no-install-recommends usbmuxd libimobiledevice-utils");
        sb.AppendLine("}");
        sb.AppendLine("install_essentials || { echo 'ERRO: não foi possível instalar usbmuxd/libimobiledevice.'; fail=1; }");
        sb.AppendLine();
        sb.AppendLine("step 'Motores opcionais'");
        sb.AppendLine("for pkg in idevicerestore libirecovery-utils; do");
        sb.AppendLine("  apt-get install -y --no-install-recommends \"$pkg\" || echo \"AVISO: $pkg indisponível neste repositório (opcional).\"");
        sb.AppendLine("done");
        sb.AppendLine();
        AppendServiceAndGroups(sb, name);
    }

    private static void AppendDnf(StringBuilder sb, string? name)
    {
        sb.AppendLine("step 'Dependências essenciais'");
        sb.AppendLine("dnf install -y usbmuxd libimobiledevice-utils libimobiledevice || { echo 'ERRO: falha do dnf.'; fail=1; }");
        sb.AppendLine();
        sb.AppendLine("step 'Motores opcionais'");
        sb.AppendLine("dnf install -y idevicerestore libirecovery || echo 'AVISO: motores opcionais indisponíveis.'");
        sb.AppendLine();
        AppendServiceAndGroups(sb, name);
    }

    private static void AppendPacman(StringBuilder sb, string? name)
    {
        sb.AppendLine("step 'Dependências essenciais'");
        sb.AppendLine("pacman -S --needed --noconfirm usbmuxd libimobiledevice || { echo 'ERRO: falha do pacman.'; fail=1; }");
        sb.AppendLine();
        sb.AppendLine("step 'Motores opcionais'");
        sb.AppendLine("pacman -S --needed --noconfirm libirecovery || echo 'AVISO: libirecovery indisponível.'");
        sb.AppendLine();
        AppendServiceAndGroups(sb, name);
    }

    private static void AppendServiceAndGroups(StringBuilder sb, string? name)
    {
        sb.AppendLine("step 'Serviço usbmuxd'");
        sb.AppendLine("if command -v systemctl >/dev/null 2>&1; then");
        sb.AppendLine("  systemctl enable --now usbmuxd || { echo 'AVISO: não foi possível ativar o usbmuxd.'; fail=1; }");
        sb.AppendLine("else");
        sb.AppendLine("  echo 'AVISO: systemctl indisponível; num container usa o socket /run/usbmuxd.'");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("if command -v udevadm >/dev/null 2>&1; then");
        sb.AppendLine("  udevadm control --reload-rules || true");
        sb.AppendLine("  udevadm trigger || true");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("step 'Permissões do utilizador'");
        if (name is null)
        {
            sb.AppendLine("echo 'AVISO: não foi possível validar o nome de utilizador; adiciona-o a plugdev à mão.'");
        }
        else
        {
            sb.AppendLine($"if getent group plugdev >/dev/null 2>&1; then");
            sb.AppendLine($"  usermod -aG plugdev '{name}' || echo 'AVISO: usermod falhou; adiciona {name} ao grupo plugdev à mão.'");
            sb.AppendLine("  echo 'Nota: termina a sessão e volta a entrar para o grupo plugdev passar a valer.'");
            sb.AppendLine("else");
            sb.AppendLine("  echo 'AVISO: o grupo plugdev não existe nesta distribuição.'");
            sb.AppendLine("fi");
        }
        sb.AppendLine();
    }

    private static void AppendVerification(StringBuilder sb)
    {
        sb.AppendLine("step 'Verificação'");
        sb.AppendLine("for tool in idevice_id ideviceinfo idevicepair idevicesyslog idevicecrashreport ideviceprovision idevicescreenshot; do");
        sb.AppendLine("  if command -v \"$tool\" >/dev/null 2>&1; then");
        sb.AppendLine("    printf '  OK    %s\\n' \"$tool\"");
        sb.AppendLine("  else");
        sb.AppendLine("    printf '  FALTA %s\\n' \"$tool\"");
        sb.AppendLine("    missing=\"$missing $tool\"");
        sb.AppendLine("  fi");
        sb.AppendLine("done");
        sb.AppendLine("for tool in ideviceenterrecovery irecovery idevicerestore ideviceactivation; do");
        sb.AppendLine("  if command -v \"$tool\" >/dev/null 2>&1; then");
        sb.AppendLine("    printf '  OK    %s (opcional)\\n' \"$tool\"");
        sb.AppendLine("  else");
        sb.AppendLine("    printf '  ----  %s (opcional, em falta)\\n' \"$tool\"");
        sb.AppendLine("  fi");
        sb.AppendLine("done");
        sb.AppendLine();
        sb.AppendLine("if [ -n \"$missing\" ]; then");
        sb.AppendLine("  echo \"FDR_FALHA: ferramentas em falta:$missing\"");
        sb.AppendLine("  exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("if [ \"$fail\" -ne 0 ]; then");
        sb.AppendLine("  echo 'FDR_FALHA: houve passos com aviso; vê o registo acima.'");
        sb.AppendLine("  exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("echo 'FDR_OK: dependências essenciais instaladas.'");
        sb.AppendLine("exit 0");
    }

    /// <summary>Só nomes de utilizador POSIX entram no script.</summary>
    public static string? SafeUser(string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return null;
        var value = user.Trim();
        if (value.Length is 0 or > 32) return null;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')) return null;
        }

        return char.IsAsciiLetter(value[0]) || value[0] == '_' ? value : null;
    }

    // ------------------------------------------------------------------ deteção

    private sealed record Spec(string Label, bool Required, string[] Apt, string[] Dnf, string[] Arch);

    private static readonly Spec[] Specs =
    [
        new("usbmuxd (serviço USB)", true, ["usbmuxd"], ["usbmuxd"], ["usbmuxd"]),
        new("libimobiledevice-utils (ferramentas)", true, ["libimobiledevice-utils"], ["libimobiledevice-utils"], ["libimobiledevice"]),
        new("libimobiledevice (biblioteca AFC)", true, ["libimobiledevice-1.0-6", "libimobiledevice-1.0-6t64"], ["libimobiledevice"], ["libimobiledevice"]),
        new("idevicerestore (restauro)", false, ["idevicerestore"], ["idevicerestore"], ["idevicerestore"]),
        new("libirecovery (recovery/DFU)", false, ["libirecovery-utils"], ["libirecovery"], ["libirecovery"]),
    ];

    public static async Task<SetupEnvironment> CollectAsync(ToolRunner tools, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new SetupEnvironment(
                false,
                OperatingSystem.IsWindows() ? "Windows (ambiente de desenvolvimento)" : "não Linux",
                PackageManager.Unknown,
                false,
                [],
                false,
                null,
                null);
        }

        var manager = DetectManager(tools);
        var packages = new List<SetupPackage>();

        foreach (var spec in Specs)
        {
            ct.ThrowIfCancellationRequested();
            var names = manager switch
            {
                PackageManager.Apt => spec.Apt,
                PackageManager.Dnf => spec.Dnf,
                PackageManager.Pacman => spec.Arch,
                _ => spec.Apt,
            };

            packages.Add(new SetupPackage(
                string.Join(" | ", names),
                spec.Label,
                spec.Required,
                await AnyInstalledAsync(tools, manager, names, ct).ConfigureAwait(false)));
        }

        return new SetupEnvironment(
            true,
            ReadDistribution(),
            manager,
            tools.Has("pkexec") || File.Exists("/usr/bin/pkexec"),
            packages,
            File.Exists("/run/usbmuxd") || File.Exists("/var/run/usbmuxd"),
            await RunTextAsync(tools, "systemctl", ["is-active", "usbmuxd"], ct).ConfigureAwait(false),
            FindUdevRules());
    }

    private static PackageManager DetectManager(ToolRunner tools)
    {
        if (tools.Has("apt-get") || File.Exists("/usr/bin/apt-get")) return PackageManager.Apt;
        if (tools.Has("dnf") || File.Exists("/usr/bin/dnf")) return PackageManager.Dnf;
        if (tools.Has("pacman") || File.Exists("/usr/bin/pacman")) return PackageManager.Pacman;
        return PackageManager.Unknown;
    }

    private static async Task<bool> AnyInstalledAsync(
        ToolRunner tools,
        PackageManager manager,
        IEnumerable<string> names,
        CancellationToken ct)
    {
        foreach (var name in names)
        {
            var run = manager switch
            {
                PackageManager.Apt => await tools.RunAsync(
                    "dpkg-query", ["-W", "-f=${Status}", name], TimeSpan.FromSeconds(5), ct).ConfigureAwait(false),
                PackageManager.Dnf => await tools.RunAsync(
                    "rpm", ["-q", name], TimeSpan.FromSeconds(5), ct).ConfigureAwait(false),
                PackageManager.Pacman => await tools.RunAsync(
                    "pacman", ["-Q", name], TimeSpan.FromSeconds(5), ct).ConfigureAwait(false),
                _ => null,
            };

            if (run is null)
            {
                // Ferramenta de consulta ausente: assume em falta e deixa o script decidir.
                return false;
            }

            if (!run.Ok) continue;

            if (manager != PackageManager.Apt) return true;
            if (run.StdOut.Contains("install ok installed", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static async Task<string?> RunTextAsync(
        ToolRunner tools,
        string command,
        string[] args,
        CancellationToken ct)
    {
        try
        {
            var run = await tools.RunAsync(command, args, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            if (run is null) return null;
            var text = (run.StdOut + "\n" + run.StdErr).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException
                                   or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return null;
        }
    }

    private static string ReadDistribution()
    {
        try
        {
            const string path = "/etc/os-release";
            if (!File.Exists(path)) return "Linux (distribuição desconhecida)";

            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal)) continue;
                var value = line["PRETTY_NAME=".Length..].Trim().Trim('"');
                if (value.Length > 0) return value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // sem /etc/os-release legível
        }

        return "Linux (distribuição desconhecida)";
    }

    private static string? FindUdevRules()
    {
        string[] known =
        [
            "/etc/udev/rules.d/39-usbmuxd.rules",
            "/usr/lib/udev/rules.d/39-usbmuxd.rules",
            "/lib/udev/rules.d/39-usbmuxd.rules",
        ];

        foreach (var path in known)
        {
            try
            {
                if (File.Exists(path)) return path;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        string[] directories = ["/etc/udev/rules.d", "/usr/lib/udev/rules.d", "/lib/udev/rules.d"];
        foreach (var directory in directories)
        {
            try
            {
                if (!Directory.Exists(directory)) continue;
                var match = Directory
                    .EnumerateFiles(directory, "*usbmuxd*.rules", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (match is not null) return match;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }
}
