using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FdrSystem.App.Services;
using FdrSystem.Core;
using FdrSystem.Core.Devices;
using FdrSystem.Core.Tools;

namespace FdrSystem.App.Views.Pages;

public sealed record AfcEntryRow(string Name, bool IsDirectory, long Size, string Ifmt)
{
    public string TypeText => IsDirectory ? "Pasta" : "Ficheiro";
    public string SizeText => IsDirectory ? "—" : FormatSize(Size);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";

        string[] units = ["KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = -1;
        do
        {
            value /= 1024;
            index++;
        }
        while (value >= 1024 && index < units.Length - 1);

        return $"{value:0.#} {units[index]}";
    }
}

public partial class AcessibilidadeView : UserControl
{
    private readonly MainWindow _owner;
    private readonly ToolRunner _tools;

    private AfcSession? _afc;
    private string _path = "/";
    private bool _connecting;
    private bool _loading;
    private bool _busy;
    private CancellationTokenSource? _cts;

    // Construtor público sem dependências para o carregador XAML/designer.
    // A aplicação usa sempre o construtor com owner e ToolRunner.
    public AcessibilidadeView() : this(null!, new ToolRunner())
    {
    }

    public AcessibilidadeView(MainWindow owner, ToolRunner tools)
    {
        InitializeComponent();
        _owner = owner;
        _tools = tools;
    }

    /// <summary>Chamado ao entrar na página; não repete a ligação se já estiver aberta.</summary>
    public void EnsureConnected()
    {
        if (_connecting || _afc is { IsOpen: true }) return;
        _ = ConnectAsync(force: false);
    }

    /// <summary>Fecha a sessão nativa quando a janela principal fecha.</summary>
    public void Shutdown()
    {
        try { _cts?.Cancel(); } catch { }
        try { _afc?.Dispose(); } catch { }
        _afc = null;
    }

    // ------------------------------------------------------------ ligação AFC

    private async Task ConnectAsync(bool force)
    {
        if (_connecting) return;
        if (!force && _afc is { IsOpen: true })
        {
            await LoadDirAsync();
            return;
        }

        _connecting = true;
        SetBrowserEnabled(false);
        AfcStatusText.Text = "A ligar ao iPhone…";

        try
        {
            if (_afc is not null)
            {
                try { _afc.Dispose(); } catch { }
                _afc = null;
            }

            var idRun = await _tools.RunAsync("idevice_id", ["-l"], TimeSpan.FromSeconds(6));
            if (idRun is null)
            {
                AfcStatusText.Text = "Ferramenta em falta: idevice_id. Instala libimobiledevice-utils.";
                return;
            }

            var devices = idRun.StdOut
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            if (!idRun.Ok || devices.Count == 0)
            {
                AfcStatusText.Text =
                    "Nenhum iPhone ligado. Liga o iPhone por USB, desbloqueia e aceita «Confiar neste computador» para ver fotos e ficheiros.";
                return;
            }

            var infoRun = await _tools.RunAsync("ideviceinfo", ["-k", "ProductType"], TimeSpan.FromSeconds(8));
            if (infoRun is null)
            {
                AfcStatusText.Text = "Ferramenta em falta: ideviceinfo. Instala libimobiledevice-utils.";
                return;
            }

            if (!infoRun.Ok)
            {
                AfcStatusText.Text =
                    "O iPhone está ligado mas não respondeu. Desbloqueia o ecrã e aceita «Confiar neste computador», depois tenta outra vez.";
                return;
            }

            var openTask = Task.Run(() => AfcSession.Open());
            var completed = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(45)));
            if (completed != openTask)
            {
                _ = openTask.ContinueWith(task =>
                {
                    try { task.Result.Dispose(); } catch { }
                }, TaskScheduler.Default);

                AfcStatusText.Text =
                    "O iPhone não respondeu em 45 segundos. Desbloqueia o ecrã e aceita «Confiar neste computador», depois tenta outra vez.";
                AppLog.Warn("AFC: tempo excedido a abrir a ligação (45 s).", "Acessibilidade");
                return;
            }

            _afc = await openTask;
            _path = "/";
            AfcPathBox.Text = "/";
            AfcStatusText.Text = "Ligado. A listar a área Media…";
            AppLog.Info("AFC ligado.", "Acessibilidade");
            await LoadDirAsync();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            AfcStatusText.Text =
                "Biblioteca nativa em falta: libimobiledevice-1.0. Instala libimobiledevice-1.0-6.";
            AppLog.Error("AFC: " + ex.Message, "Acessibilidade");
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = ex.Message +
                " Confirma que o iPhone está ligado, desbloqueado e com «Confiar neste computador» aceite.";
            AppLog.Warn("AFC: " + ex.Message, "Acessibilidade");
        }
        finally
        {
            _connecting = false;
            SetBrowserEnabled(true);
        }
    }

    private async Task<bool> EnsureAfcAsync()
    {
        if (_afc is { IsOpen: true }) return true;
        await ConnectAsync(force: true);
        return _afc is { IsOpen: true };
    }

    // ------------------------------------------------------------- navegação

    private async Task LoadDirAsync()
    {
        if (_loading) return;

        if (_afc is null || !_afc.IsOpen)
        {
            if (!_connecting) await ConnectAsync(force: true);
            return;
        }

        _loading = true;
        var session = _afc;
        var path = _path;
        AfcStatusText.Text = "A listar " + path + "…";

        try
        {
            var entries = await Task.Run(() => session.ListEntries(path));
            if (!ReferenceEquals(_afc, session)) return;

            AfcList.ItemsSource = entries
                .Select(entry => new AfcEntryRow(entry.Name, entry.IsDirectory, entry.Size, entry.Ifmt))
                .ToList();
            AfcPathBox.Text = path;
            AfcStatusText.Text = $"{entries.Count} item(ns) em {path}.";
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = $"Não foi possível listar «{path}»: {ex.Message}";
            AppLog.Warn("AFC listar: " + ex.Message, "Acessibilidade");
        }
        finally
        {
            _loading = false;
        }
    }

    private void AfcGo_Click(object? sender, RoutedEventArgs e)
    {
        if (_connecting || _loading || _busy) return;
        _path = NormalizeRemote(AfcPathBox.Text);
        _ = LoadDirAsync();
    }

    private void AfcUp_Click(object? sender, RoutedEventArgs e)
    {
        if (_connecting || _loading || _busy) return;
        _path = AfcSession.Parent(_path);
        _ = LoadDirAsync();
    }

    private async void AfcRefresh_Click(object? sender, RoutedEventArgs e)
    {
        if (_connecting || _loading || _busy) return;
        if (_afc is null || !_afc.IsOpen) await ConnectAsync(force: true);
        else await LoadDirAsync();
    }

    private async void AfcList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_connecting || _loading || _busy) return;
        if (AfcList.SelectedItem is not AfcEntryRow row || !row.IsDirectory) return;

        _path = AfcSession.Join(_path, row.Name);
        await LoadDirAsync();
    }

    // --------------------------------------------------------------- operações

    private async void AfcNewFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureAfcAsync()) return;

        var name = await Dialogs.PromptAsync(
            _owner,
            "Nova pasta",
            $"Nome da pasta a criar em {_path}:",
            "Nova Pasta");
        if (string.IsNullOrWhiteSpace(name)) return;

        name = name.Trim().Replace('/', '_');
        var remote = AfcSession.Join(_path, name);

        try
        {
            SetBrowserEnabled(false);
            AfcProgress.Value = 0;
            AfcStatusText.Text = "A criar a pasta " + remote + "…";
            await Task.Run(() => _afc!.MakeDirectory(remote));
            AppLog.Info("AFC: pasta criada " + remote, "Acessibilidade");
            await LoadDirAsync();
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Não foi possível criar a pasta: " + ex.Message;
            AppLog.Error("AFC criar pasta: " + ex.Message, "Acessibilidade");
        }
        finally
        {
            SetBrowserEnabled(true);
        }
    }

    private async void AfcDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureAfcAsync()) return;

        var selected = SelectedRows();
        if (selected.Count == 0)
        {
            AfcStatusText.Text = "Seleciona primeiro na lista o que queres eliminar.";
            return;
        }

        var names = string.Join(", ", selected.Take(5).Select(item => item.Name));
        var message = $"Eliminar {selected.Count} item(ns) do iPhone?\n\n{names}" +
                      (selected.Count > 5 ? "…" : string.Empty) +
                      "\n\nEsta ação não pode ser desfeita.";
        if (!await Dialogs.ConfirmAsync(_owner, "Eliminar do iPhone", message, "Eliminar"))
            return;

        var basePath = _path;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            AfcStatusText.Text = "A eliminar do iPhone…";
            foreach (var entry in selected)
            {
                token.ThrowIfCancellationRequested();
                var remote = AfcSession.Join(basePath, entry.Name);
                await Task.Run(() => _afc!.Remove(remote, entry.IsDirectory), token);
                AppLog.Info("AFC: eliminado " + remote, "Acessibilidade");
            }

            AfcStatusText.Text = $"Concluído: {selected.Count} item(ns) eliminado(s).";
            await LoadDirAsync();
        }
        catch (OperationCanceledException)
        {
            AfcStatusText.Text = "Eliminação cancelada.";
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Não foi possível eliminar: " + ex.Message +
                " Pastas protegidas pelo iOS, como /PhotoData, não podem ser apagadas por USB.";
            AppLog.Error("AFC eliminar: " + ex.Message, "Acessibilidade");
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void AfcImport_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureAfcAsync()) return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            AfcStatusText.Text = "Não foi possível abrir o seletor de ficheiros.";
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar ficheiros para o iPhone",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Todos os ficheiros") { Patterns = ["*"] }
                ],
            });
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Não foi possível abrir o seletor de ficheiros: " + ex.Message;
            return;
        }

        var locals = files
            .Select(file => file.Path.LocalPath)
            .Where(File.Exists)
            .ToList();
        if (locals.Count == 0) return;

        var destination = _path == "/" ? "/Importados" : _path;
        long totalBytes;
        try
        {
            totalBytes = locals.Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Não foi possível ler os ficheiros escolhidos: " + ex.Message;
            return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var doneBytes = 0L;
        var doneFiles = 0;

        try
        {
            AfcStatusText.Text = $"A importar {locals.Count} ficheiro(s) para o iPhone…";

            foreach (var local in locals)
            {
                token.ThrowIfCancellationRequested();
                var remote = AfcSession.Join(destination, Path.GetFileName(local));
                var fileSize = new FileInfo(local).Length;
                var baseDone = doneBytes;

                await Task.Run(() => _afc!.UploadFile(local, remote, copied =>
                {
                    if (token.IsCancellationRequested) return false;

                    var value = totalBytes > 0 ? (baseDone + copied) * 100.0 / totalBytes : 0;
                    Dispatcher.UIThread.Post(() =>
                    {
                        AfcProgress.Value = Math.Min(100, value);
                        AfcStatusText.Text =
                            $"A importar {doneFiles + 1}/{locals.Count} · {AfcEntryRow.FormatSize(baseDone + copied)} de {AfcEntryRow.FormatSize(totalBytes)}";
                    });
                    return true;
                }), token);

                doneBytes += fileSize;
                doneFiles++;
                AppLog.Info($"AFC: importado {local} -> {remote}", "Acessibilidade");
            }

            AfcProgress.Value = 100;
            _path = destination;
            AfcStatusText.Text = $"Concluído: {doneFiles} ficheiro(s) copiado(s) para {destination} no iPhone.";
            await LoadDirAsync();
        }
        catch (OperationCanceledException)
        {
            AfcStatusText.Text = "Importação cancelada.";
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Falha na importação: " + ex.Message;
            AppLog.Error("AFC importar: " + ex.Message, "Acessibilidade");
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void AfcExport_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureAfcAsync()) return;

        var selected = SelectedRows();
        if (selected.Count == 0)
        {
            AfcStatusText.Text = "Seleciona primeiro o que queres exportar (duplo clique abre pastas).";
            return;
        }

        var root = await PickFolderAsync("Escolher a pasta de destino no PC");
        if (root is null) return;

        await ExportRemoteEntriesAsync(selected, root);
    }

    private async void AfcExportPhotos_Click(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureAfcAsync()) return;

        var root = await PickFolderAsync("Escolher a pasta para as fotos (DCIM)");
        if (root is null) return;

        await ExportRemoteEntriesAsync([new AfcEntryRow("DCIM", true, 0, "S_IFDIR")], root, "/");
    }

    private async Task ExportRemoteEntriesAsync(
        List<AfcEntryRow> entries,
        string localRoot,
        string? sourcePath = null)
    {
        if (_afc is null) return;

        var basePath = sourcePath ?? _path;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            AfcStatusText.Text = "A preparar a cópia (a contar ficheiros)…";
            var files = await Task.Run(() =>
            {
                var list = new List<(string RemotePath, long Size)>();

                void Walk(string path)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var entry in _afc!.ListEntries(path))
                    {
                        var full = AfcSession.Join(path, entry.Name);
                        if (entry.IsDirectory) Walk(full);
                        else list.Add((full, entry.Size));
                    }
                }

                foreach (var entry in entries)
                {
                    var remote = AfcSession.Join(basePath, entry.Name);
                    if (entry.IsDirectory) Walk(remote);
                    else
                    {
                        var stat = _afc!.Stat(remote);
                        list.Add((remote, stat.Size));
                    }
                }

                return list;
            }, token);

            if (files.Count == 0)
            {
                AfcStatusText.Text = "Nada para copiar (a pasta está vazia).";
                return;
            }

            var totalBytes = files.Sum(file => file.Size);
            var doneBytes = 0L;
            var doneFiles = 0;

            AfcStatusText.Text =
                $"A copiar {files.Count} ficheiro(s) ({AfcEntryRow.FormatSize(totalBytes)})…";
            AppLog.Info(
                $"AFC: a copiar {files.Count} ficheiro(s) ({AfcEntryRow.FormatSize(totalBytes)}) para {localRoot}",
                "Acessibilidade");

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var local = SafeLocalPath(localRoot, file.RemotePath);
                var fileRef = file;
                var baseDone = doneBytes;

                await Task.Run(() => _afc!.DownloadFile(fileRef.RemotePath, local, fileRef.Size, copied =>
                {
                    if (token.IsCancellationRequested) return false;

                    var value = totalBytes > 0 ? (baseDone + copied) * 100.0 / totalBytes : 0;
                    Dispatcher.UIThread.Post(() =>
                    {
                        AfcProgress.Value = Math.Min(100, value);
                        AfcStatusText.Text = $"({doneFiles + 1}/{files.Count}) {fileRef.RemotePath}";
                    });
                    return true;
                }), token);

                doneBytes += file.Size;
                doneFiles++;
            }

            AfcProgress.Value = 100;
            AfcStatusText.Text = $"Concluído: {doneFiles} ficheiro(s) copiado(s) para {localRoot}.";
            AppLog.Info($"AFC: cópia concluída ({doneFiles} ficheiro(s) -> {localRoot}).", "Acessibilidade");
        }
        catch (OperationCanceledException)
        {
            AfcStatusText.Text = "Cópia cancelada.";
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Falha na cópia: " + ex.Message;
            AppLog.Error("AFC exportar: " + ex.Message, "Acessibilidade");
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void AfcScreenshot_Click(object? sender, RoutedEventArgs e)
    {
        if (!_tools.Has("idevicescreenshot"))
        {
            AfcStatusText.Text = "Ferramenta em falta: idevicescreenshot. Instala libimobiledevice-utils.";
            return;
        }

        var folder = await PickFolderAsync("Escolher a pasta para guardar a captura de ecrã");
        if (folder is null) return;

        var file = Path.Combine(folder, $"ecra-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        AfcStatusText.Text = "A capturar o ecrã do iPhone…";

        try
        {
            var run = await _tools.RunAsync("idevicescreenshot", [file], TimeSpan.FromSeconds(30));
            if (run is null)
            {
                AfcStatusText.Text = "Ferramenta em falta: idevicescreenshot.";
                return;
            }

            if (run.Ok && File.Exists(file))
            {
                AfcStatusText.Text = "Captura guardada em " + file;
                AppLog.Info("AFC: captura guardada em " + file, "Acessibilidade");
                return;
            }

            var raw = (run.StdOut + run.StdErr).Trim();
            var lower = raw.ToLowerInvariant();
            var reason = lower.Contains("screenshot service") ||
                         lower.Contains("developer") ||
                         lower.Contains("disk image") ||
                         lower.Contains("mounter")
                ? "O iOS 17 ou superior precisa da imagem de programador montada no iPhone (Xcode ou 3uTools, por exemplo)."
                : lower.Contains("no device") || lower.Contains("not found") || lower.Contains("usbmux")
                    ? "O iPhone não foi encontrado. Liga-o por USB, desbloqueia-o e aceita «Confiar neste computador»."
                    : "Confirma que o iPhone está desbloqueado e tenta outra vez.";

            AfcStatusText.Text = "Não foi possível tirar a captura. " + reason;
            AppLog.Warn("AFC captura: " + FirstLine(raw), "Acessibilidade");
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Não foi possível tirar a captura: " + ex.Message;
            AppLog.Error("AFC captura: " + ex.Message, "Acessibilidade");
        }
    }

    private void AfcCancel_Click(object? sender, RoutedEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        AfcStatusText.Text = "A cancelar…";
    }

    // --------------------------------------------------------------- auxiliares

    private async Task<string?> PickFolderAsync(string title)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is null)
            {
                AfcStatusText.Text = "Não foi possível abrir o seletor de pastas.";
                return null;
            }

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });

            return folders.FirstOrDefault()?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            AfcStatusText.Text = "Não foi possível abrir o seletor de pastas: " + ex.Message;
            AppLog.Error("AFC seletor de pastas: " + ex.Message, "Acessibilidade");
            return null;
        }
    }

    private List<AfcEntryRow> SelectedRows() =>
        AfcList.SelectedItems?.OfType<AfcEntryRow>().ToList() ?? [];

    private static string NormalizeRemote(string? path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (normalized.Length == 0) return "/";
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        while (normalized.Length > 1 && normalized.EndsWith('/')) normalized = normalized[..^1];
        return normalized;
    }

    private static string SafeLocalPath(string localRoot, string remotePath)
    {
        var relative = remotePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(localRoot, relative));
        var root = Path.GetFullPath(localRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new IOException("Caminho de destino inválido.");

        return full;
    }

    private static string FirstLine(string text)
    {
        var line = (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(line) ? "sem detalhes" : line;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy) AfcProgress.Value = 0;
        AfcCancelButton.IsVisible = busy;
        SetBrowserEnabled(!busy);
    }

    private void SetBrowserEnabled(bool enabled)
    {
        var canUse = enabled && !_connecting && !_busy;
        AfcUpButton.IsEnabled = canUse;
        AfcRefreshButton.IsEnabled = canUse;
        AfcGoButton.IsEnabled = canUse;
        AfcImportButton.IsEnabled = canUse;
        AfcExportButton.IsEnabled = canUse;
        AfcNewFolderButton.IsEnabled = canUse;
        AfcDeleteButton.IsEnabled = canUse;
        AfcExportPhotosButton.IsEnabled = canUse;
        AfcScreenshotButton.IsEnabled = canUse;
        AfcList.IsEnabled = canUse;
    }
}
