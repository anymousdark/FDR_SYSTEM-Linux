using FdrSystem.Core.Tools;

namespace FdrSystem.Core.Usb;

/// <summary>
/// Estado do acesso USB no Linux para a página Driver.
/// Tudo é só de leitura: não instala pacotes, não altera regras udev e não
/// pede privilégios. Os comandos de correção são mostrados na UI para o
/// utilizador os executar conscientemente.
/// </summary>
public sealed record DriverEnvironment(
    bool IsLinux,
    string Description,
    bool UsbmuxdSocket,
    string? UsbmuxdService,
    string? UsbmuxdEnabled,
    string? UdevRulesPath,
    string? UserGroups,
    bool UserInPlugdev,
    IReadOnlyList<UsbDevice> Devices)
{
    public static async Task<DriverEnvironment> CollectAsync(
        ToolRunner tools,
        CancellationToken ct = default)
    {
        var isLinux = OperatingSystem.IsLinux();
        var description = UsbScanner.DescribeEnvironment();
        var devices = UsbScanner.FindAppleDevices();

        if (!isLinux)
        {
            return new DriverEnvironment(
                false,
                description,
                false,
                null,
                null,
                null,
                null,
                false,
                devices);
        }

        var socket = File.Exists("/run/usbmuxd") || File.Exists("/var/run/usbmuxd");
        var udev = FindUdevRules();
        var groupsTask = RunTextAsync(tools, "id", ["-nG"], TimeSpan.FromSeconds(3), ct);
        var activeTask = RunTextAsync(tools, "systemctl", ["is-active", "usbmuxd"], TimeSpan.FromSeconds(4), ct);
        var enabledTask = RunTextAsync(tools, "systemctl", ["is-enabled", "usbmuxd"], TimeSpan.FromSeconds(4), ct);

        await Task.WhenAll(groupsTask, activeTask, enabledTask).ConfigureAwait(false);

        var groups = await groupsTask.ConfigureAwait(false);
        var inPlugdev = groups?
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("plugdev", StringComparer.Ordinal) == true;

        return new DriverEnvironment(
            true,
            description,
            socket,
            await activeTask.ConfigureAwait(false),
            await enabledTask.ConfigureAwait(false),
            udev,
            groups,
            inPlugdev,
            devices);
    }

    private static async Task<string?> RunTextAsync(
        ToolRunner tools,
        string command,
        IEnumerable<string> args,
        TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            var run = await tools.RunAsync(command, args, timeout, ct).ConfigureAwait(false);
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

        string[] directories =
        [
            "/etc/udev/rules.d",
            "/usr/lib/udev/rules.d",
            "/lib/udev/rules.d",
        ];

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
