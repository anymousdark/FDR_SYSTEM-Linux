using System.Runtime.InteropServices;

namespace FdrSystem.Core.Devices;

public sealed record AfcEntry(string Name, bool IsDirectory, long Size, string Ifmt);

/// <summary>
/// Sessão AFC (área Media) sobre a API nativa do libimobiledevice.
/// No Windows a app original carregava a DLL ao lado do executável; no Linux
/// o <see cref="Interop.InteropBootstrap"/> resolve libimobiledevice-1.0.so.
/// </summary>
public sealed class AfcSession : IDisposable
{
    private const string Lib = "libimobiledevice-1.0";
    private const int ModeReadOnly = 1;
    private const int ModeWrite = 4;
    private const int MaxListEntries = 1 << 16;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int idevice_new(
        out IntPtr device,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? udid);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int idevice_free(IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int lockdownd_client_new_with_handshake(
        IntPtr device,
        out IntPtr client,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int lockdownd_client_free(IntPtr client);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int lockdownd_start_service(
        IntPtr client,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string identifier,
        out IntPtr service);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void lockdownd_service_descriptor_free(IntPtr service);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_client_new(IntPtr device, IntPtr service, out IntPtr afc);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_client_free(IntPtr afc);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_read_directory(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_get_file_info(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_dictionary_free(IntPtr dictionary);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_file_open(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int mode,
        out ulong handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_file_read(
        IntPtr afc,
        ulong handle,
        byte[] data,
        uint length,
        out uint bytesRead);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_file_write(
        IntPtr afc,
        ulong handle,
        byte[] data,
        uint length,
        out uint bytesWritten);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_file_close(IntPtr afc, ulong handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_make_directory(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_remove_path(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_remove_path_and_contents(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int afc_rename_path(
        IntPtr afc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string from,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string to);

    private readonly object _gate = new();
    private IntPtr _device;
    private IntPtr _lockdown;
    private IntPtr _afc;
    private bool _open;
    private bool _broken;

    public bool IsOpen
    {
        get { lock (_gate) return _open && !_broken; }
    }

    public static AfcSession Open(string? udid = null)
    {
        var session = new AfcSession();
        try
        {
            lock (session._gate)
            {
                session.Guard(() =>
                {
                    if (idevice_new(out session._device, string.IsNullOrWhiteSpace(udid) ? null : udid) != 0 ||
                        session._device == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "Dispositivo não encontrado. Liga o iPhone por USB e desbloqueia-o.");
                    }

                    if (lockdownd_client_new_with_handshake(
                            session._device, out session._lockdown, "FDR SYSTEM") != 0 ||
                        session._lockdown == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "O emparelhamento falhou. Desbloqueia o iPhone e aceita «Confiar neste computador».");
                    }

                    if (lockdownd_start_service(session._lockdown, "com.apple.afc", out var service) != 0 ||
                        service == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "O serviço de ficheiros (AFC) não está disponível neste estado do aparelho.");
                    }

                    try
                    {
                        if (afc_client_new(session._device, service, out session._afc) != 0 ||
                            session._afc == IntPtr.Zero)
                        {
                            throw new InvalidOperationException(
                                "Não foi possível abrir a área de ficheiros do iPhone.");
                        }
                    }
                    finally
                    {
                        lockdownd_service_descriptor_free(service);
                    }

                    session._open = true;
                });
            }

            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void RequireOpen()
    {
        if (_broken)
        {
            throw new IOException(
                "A ligação de ficheiros foi reiniciada depois de uma falha da biblioteca nativa. Usa «Atualizar» para ligar outra vez.");
        }

        if (!_open || _afc == IntPtr.Zero)
        {
            throw new IOException(
                "A sessão de ficheiros não está aberta. Usa «Atualizar» para ligar ao iPhone.");
        }
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (AccessViolationException ex)
        {
            _broken = true;
            throw new IOException(
                "A biblioteca nativa de ficheiros falhou (memória). Usa «Atualizar» para tentar de novo.", ex);
        }
        catch (SEHException ex)
        {
            _broken = true;
            throw new IOException(
                "A biblioteca nativa de ficheiros falhou. Usa «Atualizar» para tentar de novo.", ex);
        }
    }

    private T Guard<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (AccessViolationException ex)
        {
            _broken = true;
            throw new IOException(
                "A biblioteca nativa de ficheiros falhou (memória). Usa «Atualizar» para tentar de novo.", ex);
        }
        catch (SEHException ex)
        {
            _broken = true;
            throw new IOException(
                "A biblioteca nativa de ficheiros falhou. Usa «Atualizar» para tentar de novo.", ex);
        }
    }

    public List<string> ListNames(string path)
    {
        lock (_gate)
        {
            RequireOpen();
            return Guard(() =>
            {
                if (afc_read_directory(_afc, path, out var info) != 0 || info == IntPtr.Zero)
                    throw new IOException($"Não foi possível listar «{path}».");

                try
                {
                    var result = new List<string>();
                    for (var count = 0; count < MaxListEntries; count++)
                    {
                        var pointer = Marshal.ReadIntPtr(info, count * IntPtr.Size);
                        if (pointer == IntPtr.Zero) break;

                        var name = Marshal.PtrToStringUTF8(pointer);
                        if (!string.IsNullOrEmpty(name) && name != "." && name != "..")
                            result.Add(name);
                    }

                    return result;
                }
                finally
                {
                    afc_dictionary_free(info);
                }
            });
        }
    }

    public List<AfcEntry> ListEntries(string path)
    {
        lock (_gate)
        {
            RequireOpen();

            var entries = new List<AfcEntry>();
            foreach (var name in ListNames(path))
            {
                var stat = Stat(Join(path, name));
                entries.Add(new AfcEntry(name, stat.IsDirectory, stat.Size, stat.Ifmt));
            }

            entries.Sort((a, b) => a.IsDirectory != b.IsDirectory
                ? (a.IsDirectory ? -1 : 1)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }
    }

    public (bool IsDirectory, long Size, string Ifmt) Stat(string path)
    {
        lock (_gate)
        {
            RequireOpen();
            return Guard(() =>
            {
                if (afc_get_file_info(_afc, path, out var info) != 0 || info == IntPtr.Zero)
                    throw new IOException($"Não foi possível obter informações de «{path}».");

                try
                {
                    var ifmt = string.Empty;
                    long size = 0;

                    for (var index = 0; index < MaxListEntries; index += 2)
                    {
                        var keyPointer = Marshal.ReadIntPtr(info, index * IntPtr.Size);
                        if (keyPointer == IntPtr.Zero) break;

                        var valuePointer = Marshal.ReadIntPtr(info, (index + 1) * IntPtr.Size);
                        if (valuePointer == IntPtr.Zero) break;

                        var key = Marshal.PtrToStringUTF8(keyPointer);
                        if (string.IsNullOrEmpty(key)) break;

                        var value = Marshal.PtrToStringUTF8(valuePointer) ?? string.Empty;
                        if (key == "st_ifmt") ifmt = value;
                        else if (key == "st_size" && long.TryParse(value, out var parsed)) size = parsed;
                    }

                    return (ifmt == "S_IFDIR", size, ifmt);
                }
                finally
                {
                    afc_dictionary_free(info);
                }
            });
        }
    }

    public bool Exists(string path)
    {
        lock (_gate)
        {
            RequireOpen();
            return Guard(() =>
            {
                if (afc_get_file_info(_afc, path, out var info) != 0 || info == IntPtr.Zero)
                    return false;

                afc_dictionary_free(info);
                return true;
            });
        }
    }

    public void DownloadFile(string remotePath, string localPath, long size, Func<long, bool> onProgress)
    {
        lock (_gate)
        {
            RequireOpen();
            Guard(() =>
            {
                if (afc_file_open(_afc, remotePath, ModeReadOnly, out var handle) != 0)
                    throw new IOException($"Não foi possível abrir «{remotePath}» no iPhone.");

                try
                {
                    var directory = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                    using var target = new FileStream(
                        localPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
                    var buffer = new byte[1 << 20];
                    long done = 0;

                    while (true)
                    {
                        if (afc_file_read(_afc, handle, buffer, (uint)buffer.Length, out var read) != 0)
                            throw new IOException($"Falha de leitura em «{remotePath}».");

                        if (read == 0) break;

                        target.Write(buffer, 0, (int)read);
                        done += read;
                        if (!onProgress(done))
                            throw new OperationCanceledException("Exportação cancelada.");
                    }

                    if (size > 0 && done != size)
                        throw new IOException($"«{remotePath}» ficou com tamanho diferente do esperado.");
                }
                finally
                {
                    afc_file_close(_afc, handle);
                }
            });
        }
    }

    public void UploadFile(string localPath, string remotePath, Func<long, bool> onProgress)
    {
        lock (_gate)
        {
            RequireOpen();
            Guard(() =>
            {
                EnsureDirectory(Parent(remotePath));

                if (afc_file_open(_afc, remotePath, ModeWrite, out var handle) != 0)
                    throw new IOException($"Não foi possível criar «{remotePath}» no iPhone.");

                try
                {
                    using var source = new FileStream(
                        localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                    var buffer = new byte[1 << 20];
                    long done = 0;
                    int read;

                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (afc_file_write(_afc, handle, buffer, (uint)read, out var written) != 0 ||
                            written != read)
                        {
                            throw new IOException($"Falha de escrita em «{remotePath}».");
                        }

                        done += read;
                        if (!onProgress(done))
                            throw new OperationCanceledException("Importação cancelada.");
                    }
                }
                finally
                {
                    afc_file_close(_afc, handle);
                }
            });
        }
    }

    public void MakeDirectory(string path)
    {
        lock (_gate)
        {
            RequireOpen();
            Guard(() =>
            {
                if (afc_make_directory(_afc, path) != 0 && !Stat(path).IsDirectory)
                    throw new IOException($"Não foi possível criar a pasta «{path}».");
            });
        }
    }

    public void EnsureDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var part in parts)
        {
            current += "/" + part;
            if (Exists(current)) continue;
            afc_make_directory(_afc, current);
        }
    }

    public void Remove(string path, bool recursive)
    {
        lock (_gate)
        {
            RequireOpen();
            Guard(() =>
            {
                var code = recursive
                    ? afc_remove_path_and_contents(_afc, path)
                    : afc_remove_path(_afc, path);
                if (code != 0)
                    throw new IOException($"Não foi possível eliminar «{path}».");
            });
        }
    }

    public void Rename(string from, string to)
    {
        lock (_gate)
        {
            RequireOpen();
            Guard(() =>
            {
                if (afc_rename_path(_afc, from, to) != 0)
                    throw new IOException($"Não foi possível renomear «{from}».");
            });
        }
    }

    public static string Join(string path, string name) =>
        (string.IsNullOrEmpty(path) || path == "/" ? "/" : path.TrimEnd('/') + "/") + name;

    public static string Parent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index <= 0 ? "/" : trimmed[..index];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_open && _afc == IntPtr.Zero && _lockdown == IntPtr.Zero && _device == IntPtr.Zero)
                return;

            _open = false;
            if (_afc != IntPtr.Zero)
            {
                try { afc_client_free(_afc); } catch { }
                _afc = IntPtr.Zero;
            }

            if (_lockdown != IntPtr.Zero)
            {
                try { lockdownd_client_free(_lockdown); } catch { }
                _lockdown = IntPtr.Zero;
            }

            if (_device != IntPtr.Zero)
            {
                try { idevice_free(_device); } catch { }
                _device = IntPtr.Zero;
            }
        }
    }
}
