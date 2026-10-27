using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FdrSystem.Core.Interop;

/// <summary>
/// Resolve as bibliotecas nativas do projeto libimobiledevice por plataforma.
/// No Windows a app original trazia as DLLs em tools\libimobiledevice; no Linux
/// as mesmas bibliotecas vÃªm dos pacotes do sistema (libimobiledevice-1.0-6,
/// libplist-2.0-3/4) e o nome tem versÃ£o ABI no fim.
/// </summary>
public static class InteropBootstrap
{
    private const string IDeviceEnv = "FDR_LIB_IMOBILEDEVICE";
    private const string PlistEnv = "FDR_LIB_PLIST";
    private const string IRecoveryEnv = "FDR_LIB_IRECOVERY";

    private static readonly Dictionary<string, string[]> Candidates = new(StringComparer.Ordinal)
    {
        ["libimobiledevice-1.0"] = OperatingSystem.IsWindows()
            ? ["libimobiledevice-1.0.dll"]
            : OperatingSystem.IsMacOS()
                ? ["libimobiledevice-1.0.6.dylib", "libimobiledevice-1.0.dylib"]
                : ["libimobiledevice-1.0.so.6", "libimobiledevice-1.0.so.0", "libimobiledevice-1.0.so"],

        ["libplist-2.0"] = OperatingSystem.IsWindows()
            ? ["libplist-2.0.dll"]
            : OperatingSystem.IsMacOS()
                ? ["libplist-2.0.3.dylib", "libplist-2.0.dylib"]
                : ["libplist-2.0.so.3", "libplist-2.0.so.4", "libplist-2.0.so"],

        ["libirecovery-1.0"] = OperatingSystem.IsWindows()
            ? ["libirecovery-1.0.dll"]
            : OperatingSystem.IsMacOS()
                ? ["libirecovery-1.0.5.dylib", "libirecovery-1.0.dylib"]
                : ["libirecovery-1.0.so.3", "libirecovery-1.0.so.0", "libirecovery-1.0.so"],
    };

    private static readonly Dictionary<string, string> EnvVars = new(StringComparer.Ordinal)
    {
        ["libimobiledevice-1.0"] = IDeviceEnv,
        ["libplist-2.0"] = PlistEnv,
        ["libirecovery-1.0"] = IRecoveryEnv,
    };

    private static int _installed;

    [ModuleInitializer]
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(InteropBootstrap).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // JÃ¡ existe um resolver para esta assembly (ex.: hot reload).
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!Candidates.TryGetValue(libraryName, out var names)) return IntPtr.Zero;

        // 1) caminho explÃ­cito por variÃ¡vel de ambiente (Ãºtil em AppImage/Flatpak)
        if (EnvVars.TryGetValue(libraryName, out var envVar))
        {
            var custom = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(custom) && NativeLibrary.TryLoad(custom, out var handle)) return handle;
        }

        // 2) nomes por plataforma, com o diretÃ³rio da app Ã  frente
        foreach (var name in names)
        {
            var local = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(local) && NativeLibrary.TryLoad(local, out var fromApp)) return fromApp;
        }

        foreach (var name in names)
        {
            if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle)) return handle;
        }

        // 3) Ãºltimo recurso: carregar pelo nome sem versÃ£o
        var fallback = Path.GetFileNameWithoutExtension(names[^1]);
        return NativeLibrary.TryLoad(fallback, assembly, searchPath, out var any) ? any : IntPtr.Zero;
    }

    /// <summary>ForÃ§a a instalaÃ§Ã£o do resolver (chamado no arranque do core).</summary>
    public static void EnsureInitialized() => Install();
}

