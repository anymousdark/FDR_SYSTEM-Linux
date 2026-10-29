using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace FdrSystem.Core.Firmware;

public enum DownloadState { Queued, Downloading, Verifying, Paused, Completed, Error }

/// <summary>Pedaço contíguo de um download segmentado.</summary>
public sealed class Segment
{
    public long Start { get; init; }
    public long End { get; init; }
    public long Done;
    public long Length => End - Start + 1;
}

/// <summary>
/// Um IPSW em fila, a descarregar ou já na pasta. Portado do DownloadManager do
/// projeto Windows; aqui sem WPF (o estado é lido pela UI através de <see cref="Status"/>).
/// </summary>
public sealed class DownloadItem
{
    public string Url { get; set; } = "";
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Title { get; set; } = "";
    public string SubText { get; set; } = "";
    public long Total { get; set; }
    public string? Sha256 { get; set; }
    public string? Model { get; set; }
    public string? Version { get; set; }
    public string? Build { get; set; }
    public bool Signed { get; set; } = true;

    public string PartialPath => FilePath + ".download";
    public string MetaPath => FilePath + ".download.meta";

    public DownloadState State { get; set; } = DownloadState.Queued;
    public long Done { get; set; }
    public double SpeedBps { get; set; }
    public bool VerifyOk { get; set; }

    /// <summary>Item vindo da pasta (não pertence à fila viva).</summary>
    public bool OnDiskOnly { get; init; }
    public bool CanResumeDisk { get; init; }
    public bool Deleted { get; set; }

    internal CancellationTokenSource? Cts;
    internal List<Segment> Segments = new();
    internal DateTime LastMeta = DateTime.MinValue;

    public bool HasProgress => !OnDiskOnly;
    public double ProgressMax => Total > 0 ? Total : 1;
    public double ProgressValue => Math.Min(Done, ProgressMax);

    public string PauseLabel => State switch
    {
        DownloadState.Downloading or DownloadState.Queued => "Pausar",
        DownloadState.Paused or DownloadState.Error => "Retomar",
        DownloadState.Verifying => "A verificar…",
        _ => OnDiskOnly && CanResumeDisk ? "Retomar" : "—",
    };

    public bool CanPause => State is DownloadState.Downloading or DownloadState.Queued
        or DownloadState.Paused or DownloadState.Error || (OnDiskOnly && CanResumeDisk);

    private string _status = "";
    private long _lastRaisedDone = -1;
    private string _lastError = "";

    public string Status => string.IsNullOrEmpty(_status) ? "—" : _status;

    internal void SetError(string message) => _lastError = message;

    /// <summary>Recalcula o texto de estado. Chamado pelo gestor a cada tick.</summary>
    internal void RefreshStatus()
    {
        string status;
        if (OnDiskOnly)
        {
            status = State == DownloadState.Completed
                ? $"Completo · {Fmt(Total)}" + (SubText == "" ? "" : " · " + SubText)
                : CanResumeDisk
                    ? $"Incompleto · {Fmt(Done)} de {Fmt(Total)} · pode retomar" + (SubText == "" ? "" : " · " + SubText)
                    : $"Incompleto · {Fmt(Done)} · sem dados de retoma (só eliminar)";
        }
        else
        {
            status = State switch
            {
                DownloadState.Queued => "Na fila — começa em instantes…",
                DownloadState.Downloading => Total > 0
                    ? $"{FmtSpeed(SpeedBps)} · {Fmt(Done)} de {Fmt(Total)}{EtaText()}"
                    : $"{FmtSpeed(SpeedBps)} · {Fmt(Done)}",
                DownloadState.Verifying => $"A verificar a integridade (SHA-256)… {Pct()}",
                DownloadState.Paused => $"Pausado · {Fmt(Done)} de {Fmt(Total)} — prima «Retomar» para continuar.",
                DownloadState.Completed => $"Concluído ✓ {Fmt(Total)}" + (VerifyOk ? " · integridade verificada" : ""),
                DownloadState.Error => "Falha: " + _lastError,
                _ => "",
            };
        }

        if (status == _status && Done == _lastRaisedDone) return;
        _status = status;
        _lastRaisedDone = Done;
    }

    private string Pct() => Total > 0 ? $"{Done * 100.0 / Total:0}%" : "";

    private string EtaText()
    {
        if (Total <= 0 || SpeedBps <= 0 || Done >= Total) return "";
        var secs = (Total - Done) / SpeedBps;
        if (secs < 1) return "";
        var t = TimeSpan.FromSeconds(secs);
        return t.TotalHours >= 1 ? $" · faltam ~{t:h\\:mm\\:ss}" : $" · faltam ~{t.Minutes}m{t.Seconds:00}s";
    }

    public static string Fmt(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        if (bytes >= 1L << 10) return $"{bytes / 1024.0:0} KB";
        return bytes + " B";
    }

    public static string FmtSpeed(double bps)
    {
        if (bps >= 1L << 20) return $"{bps / (1L << 20):0.0} MB/s";
        if (bps >= 1024) return $"{bps / 1024:0} KB/s";
        return bps <= 0 ? "a medir…" : $"{bps:0} B/s";
    }
}

/// <summary>
/// Motor de descarga segmentado (HTTP Range), com retoma, metadados em
/// <c>.download.meta</c> e verificação SHA-256 no fim.
/// Port do <c>DownloadManager.cs</c> do projeto Windows, sem dependências de UI.
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private readonly string _folder;
    private readonly HttpClient _http;
    private readonly List<DownloadItem> _live = new();
    private readonly List<DownloadItem> _disk = new();
    private readonly Queue<DownloadItem> _queue = new();
    private readonly Dictionary<DownloadItem, (long LastDone, DateTime LastTick, double Speed)> _speedTrack = new();
    private readonly object _gate = new();
    private readonly Timer _timer;
    private int _running;
    private volatile bool _disposed;

    /// <summary>Disparado quando o estado muda (a UI deve reler <see cref="Snapshot"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>Registo: (canal, mensagem, nível).</summary>
    public Action<string, string, string>? Log { get; set; }

    public int MaxConcurrent { get; set; } = 2;
    public int SegmentsPerDownload { get; set; } = 8;

    public DownloadManager(string firmwareFolder)
    {
        _folder = firmwareFolder;
        _http = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _timer = new Timer(_ => Tick(), null, 650, 650);
    }

    public string Folder => _folder;

    public bool HasActive
    {
        get
        {
            lock (_gate)
                return _live.Any(i => i.State is DownloadState.Downloading or DownloadState.Queued or DownloadState.Verifying);
        }
    }

    /// <summary>Cópia da lista atual (Downloads + ficheiros na pasta).</summary>
    public IReadOnlyList<DownloadItem> Snapshot()
    {
        lock (_gate) return _live.Concat(_disk).ToList();
    }

    public DownloadItem? Find(string filePath)
    {
        lock (_gate)
            return _live.Concat(_disk)
                .FirstOrDefault(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    public string Enqueue(DownloadItem item)
    {
        lock (_gate)
        {
            var existing = _live.Concat(_disk)
                .FirstOrDefault(i => string.Equals(i.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null && existing != item)
            {
                if (existing.State == DownloadState.Completed) return "Já existe na pasta: " + existing.FileName;
                if (existing.State is DownloadState.Downloading or DownloadState.Queued)
                    return "Já está a ser descarregado: " + existing.FileName;
                if (existing.State is DownloadState.Paused or DownloadState.Error)
                {
                    ResumeCore(existing);
                    return "Retomado: " + existing.FileName;
                }
            }

            item.State = DownloadState.Queued;
            if (string.IsNullOrWhiteSpace(item.Title)) item.Title = item.FileName;
            _live.Insert(0, item);
            _queue.Enqueue(item);
        }

        Rebuild();
        Pump();
        return "Adicionado aos downloads: " + item.FileName;
    }

    public void Pause(DownloadItem item)
    {
        try { item.Cts?.Cancel(); } catch { /* já terminou */ }
    }

    public void Resume(DownloadItem item)
    {
        lock (_gate) ResumeCore(item);
        Rebuild();
        Pump();
    }

    private void ResumeCore(DownloadItem item)
    {
        if (item.State is DownloadState.Downloading or DownloadState.Queued or DownloadState.Verifying) return;

        if (item.OnDiskOnly)
        {
            _disk.Remove(item);
            item = new DownloadItem
            {
                Url = item.Url,
                FileName = item.FileName,
                FilePath = item.FilePath,
                Title = item.Title,
                Total = item.Total,
                Sha256 = item.Sha256,
                Model = item.Model,
                Version = item.Version,
                Build = item.Build,
                Signed = item.Signed,
                Done = item.Done,
            };
            _live.Insert(0, item);
        }

        item.State = DownloadState.Queued;
        item.SetError("");
        _queue.Enqueue(item);
    }

    public void Delete(DownloadItem item)
    {
        item.Deleted = true;
        try { item.Cts?.Cancel(); } catch { }

        var removeFiles = item.State is not (DownloadState.Downloading or DownloadState.Queued or DownloadState.Verifying);
        lock (_gate)
        {
            _live.Remove(item);
            _disk.Remove(item);
        }

        if (removeFiles) DeleteFilesNow(item);
        Rebuild();
    }

    private void DeleteFilesNow(DownloadItem item)
    {
        foreach (var f in new[] { item.PartialPath, item.MetaPath, item.FilePath })
        {
            try
            {
                if (File.Exists(f)) File.Delete(f);
            }
            catch (Exception ex)
            {
                Log?.Invoke("Descargas", $"eliminar {f}: {ex.Message}", "ERRO");
            }
        }
    }

    /// <summary>Relê a pasta de firmware: IPSW completos e `.download` retomáveis.</summary>
    public void RefreshDisk()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                _disk.Clear();

                var files = Directory.EnumerateFiles(_folder)
                    .Where(f => f.EndsWith(".ipsw", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTime);

                foreach (var file in files)
                {
                    if (file.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
                    {
                        var target = file[..^".download".Length];
                        if (_live.Concat(_disk).Any(i => string.Equals(i.FilePath, target, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var meta = LoadMeta(file + ".meta");
                        var len = new FileInfo(file).Length;
                        var diskItem = new DownloadItem
                        {
                            FileName = Path.GetFileName(target),
                            FilePath = target,
                            OnDiskOnly = true,
                            Url = meta?.url ?? "",
                            Total = meta is { total: > 0 } ? meta.total : 0,
                            Sha256 = meta?.sha256,
                            Title = !string.IsNullOrWhiteSpace(meta?.title) ? meta!.title! : Path.GetFileName(target),
                            Done = len,
                            CanResumeDisk = meta is not null && meta.url is { Length: > 0 },
                        };
                        diskItem.SubText = File.GetLastWriteTime(file).ToString("dd/MM/yyyy HH:mm");
                        diskItem.State = DownloadState.Paused;
                        diskItem.RefreshStatus();
                        _disk.Add(diskItem);
                    }
                    else
                    {
                        if (_live.Concat(_disk).Any(i => string.Equals(i.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var len = new FileInfo(file).Length;
                        if (len == 0) continue;

                        var diskItem = new DownloadItem
                        {
                            FileName = Path.GetFileName(file),
                            FilePath = file,
                            OnDiskOnly = true,
                            Title = Path.GetFileName(file),
                            Done = len,
                            Total = len,
                            CanResumeDisk = false,
                        };
                        diskItem.SubText = File.GetLastWriteTime(file).ToString("dd/MM/yyyy HH:mm");
                        diskItem.State = DownloadState.Completed;
                        diskItem.RefreshStatus();
                        _disk.Add(diskItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("Descargas", "lista da pasta: " + ex.Message, "ERRO");
            }
        }

        Rebuild();
    }

    public void ClearFinished()
    {
        lock (_gate)
        {
            _live.RemoveAll(i => i.State == DownloadState.Completed);
        }

        RefreshDisk();
    }

    private void Rebuild()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        DownloadItem[] items;
        lock (_gate) items = _live.ToArray();

        foreach (var item in items)
        {
            if (item.State == DownloadState.Downloading)
            {
                if (_speedTrack.TryGetValue(item, out var prev))
                {
                    var secs = Math.Max(0.001, (now - prev.LastTick).TotalSeconds);
                    var inst = (item.Done - prev.LastDone) / secs;
                    var smoothed = prev.Speed <= 0 ? inst : prev.Speed * 0.7 + inst * 0.3;
                    item.SpeedBps = Math.Max(0, smoothed);
                    _speedTrack[item] = (item.Done, now, item.SpeedBps);
                }
                else
                {
                    _speedTrack[item] = (item.Done, now, 0);
                }
            }
            else
            {
                _speedTrack.Remove(item);
                item.SpeedBps = 0;
            }

            item.RefreshStatus();
        }

        lock (_gate)
        {
            foreach (var item in _disk) item.RefreshStatus();
        }

        Rebuild();
    }

    private void Pump()
    {
        while (true)
        {
            DownloadItem item;
            lock (_gate)
            {
                if (_running >= MaxConcurrent || _queue.Count == 0) return;
                item = _queue.Dequeue();
                if (item.Deleted || item.State != DownloadState.Queued) continue;
                _running++;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadAsync(item).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log?.Invoke("Descargas", "Erro inesperado num download: " + ex.Message, "ERRO");
                }
                finally
                {
                    lock (_gate) _running--;
                    RefreshDisk();
                    Pump();
                }
            });
        }
    }

    private async Task DownloadAsync(DownloadItem item)
    {
        item.Cts?.Dispose();
        var cts = item.Cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            Directory.CreateDirectory(_folder);

            if (File.Exists(item.FilePath) && (item.Total <= 0 || new FileInfo(item.FilePath).Length == item.Total))
            {
                item.State = DownloadState.Completed;
                item.Done = item.Total > 0 ? item.Total : new FileInfo(item.FilePath).Length;
                item.SpeedBps = 0;
                return;
            }

            // Retoma a partir dos metadados, se o ficheiro parcial ainda for coerente.
            var meta = LoadMeta(item.MetaPath);
            List<Segment>? segments = null;
            if (meta is not null && meta.url == item.Url && meta is { total: > 0, segments.Count: > 0 })
            {
                var candidate = meta.segments
                    .Select(s => new Segment { Start = s.start, End = s.end, Done = s.done })
                    .ToList();
                var maxWritten = candidate.Max(s => s.Start + s.Done);
                var fileLen = File.Exists(item.PartialPath) ? new FileInfo(item.PartialPath).Length : -1;
                if (fileLen >= maxWritten && fileLen > 0)
                {
                    segments = candidate;
                    item.Total = meta.total;
                    if (string.IsNullOrWhiteSpace(item.Sha256) && !string.IsNullOrWhiteSpace(meta.sha256))
                        item.Sha256 = meta.sha256;
                }
            }

            if (segments is null)
            {
                try { if (File.Exists(item.PartialPath)) File.Delete(item.PartialPath); } catch { }

                var (probedTotal, rangeOk) = await ProbeAsync(item.Url, token).ConfigureAwait(false);
                if (item.Total <= 0 && probedTotal > 0) item.Total = probedTotal;

                if (!rangeOk || item.Total <= 0)
                {
                    await SingleStreamAsync(item, token).ConfigureAwait(false);
                    await FinishAsync(item).ConfigureAwait(false);
                    return;
                }

                var count = item.Total < 128L << 20 ? 4 : SegmentsPerDownload;
                segments = BuildSegments(item.Total, count);
            }

            item.Segments = segments;
            item.Done = segments.Sum(s => s.Done);
            item.State = DownloadState.Downloading;
            item.SpeedBps = 0;
            _speedTrack.Remove(item);

            using (var fs = new FileStream(item.PartialPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                fs.SetLength(item.Total);

            await Task.WhenAll(segments.Select(s => DownloadSegmentAsync(item, s, token))).ConfigureAwait(false);

            var length = new FileInfo(item.PartialPath).Length;
            if (item.Total > 0 && length != item.Total)
                throw new InvalidDataException("O ficheiro ficou com um tamanho inesperado.");

            await VerifyAsync(item, token).ConfigureAwait(false);
            await FinishAsync(item).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!item.Deleted)
            {
                SaveMeta(item, force: true);
                item.State = DownloadState.Paused;
                item.SpeedBps = 0;
            }
        }
        catch (Exception ex)
        {
            if (!item.Deleted)
            {
                item.SetError(ex.Message);
                item.State = DownloadState.Error;
                item.SpeedBps = 0;
                SaveMeta(item, force: true);
                Log?.Invoke("Descargas", $"Falha no download {item.FileName}: {ex.Message}", "ERRO");
            }
        }
        finally
        {
            if (item.Deleted) DeleteFilesNow(item);
            item.Cts?.Dispose();
            item.Cts = null;
            item.RefreshStatus();
        }
    }

    private Task FinishAsync(DownloadItem item)
    {
        File.Move(item.PartialPath, item.FilePath, true);
        try { File.Delete(item.MetaPath); } catch { }

        var length = new FileInfo(item.FilePath).Length;

        // Servidores sem Content-Length deixam Total a -1; usar o tamanho real.
        if (item.Total <= 0) item.Total = length;
        item.Total = length;

        item.State = DownloadState.Completed;
        item.Done = length;
        item.SpeedBps = 0;
        Log?.Invoke("Descargas", $"Download concluído: {item.FileName} ({item.Done} bytes)", "INFO");
        return Task.CompletedTask;
    }

    private async Task<(long total, bool rangeOk)> ProbeAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.PartialContent)
            return (response.Content.Headers.ContentRange?.Length ?? -1, true);
        if (response.StatusCode == HttpStatusCode.OK)
            return (response.Content.Headers.ContentLength ?? -1, false);

        response.EnsureSuccessStatusCode();
        return (-1, false);
    }

    private static List<Segment> BuildSegments(long total, int count)
    {
        var segments = new List<Segment>(count);
        var chunk = total / count;
        for (var i = 0; i < count; i++)
        {
            var start = chunk * i;
            var end = i == count - 1 ? total - 1 : chunk * (i + 1) - 1;
            segments.Add(new Segment { Start = start, End = end });
        }
        return segments;
    }

    private async Task DownloadSegmentAsync(DownloadItem item, Segment segment, CancellationToken token)
    {
        var attempt = 0;
        while (segment.Done < segment.Length)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
                request.Headers.Range = new RangeHeaderValue(segment.Start + segment.Done, segment.End);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && segment.Done == segment.Length)
                    return;
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    response.EnsureSuccessStatusCode();
                    throw new InvalidDataException("O servidor deixou de aceitar a descarga por partes.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var target = new FileStream(item.PartialPath, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite, 1 << 20, FileOptions.Asynchronous);
                target.Position = segment.Start + segment.Done;

                var buffer = new byte[1 << 20];
                int n;
                while ((n = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), token).ConfigureAwait(false);
                    var before = segment.Done;
                    segment.Done += n;
                    item.Done += n;
                    if (before >> 22 != segment.Done >> 22) SaveMeta(item, force: false);
                }

                if (segment.Done < segment.Length) throw new IOException("O pedaço terminou cedo.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 4)
            {
                attempt++;
                Log?.Invoke("Descargas",
                    $"Segmento {segment.Start}: tentativa {attempt} falhou ({ex.Message}); a repetir…", "AVISO");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt * attempt)), token)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task SingleStreamAsync(DownloadItem item, CancellationToken token)
    {
        try { if (File.Exists(item.PartialPath)) File.Delete(item.PartialPath); } catch { }

        using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (item.Total <= 0) item.Total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var target = new FileStream(item.PartialPath, FileMode.Create, FileAccess.Write,
            FileShare.Read, 1 << 20, FileOptions.Asynchronous);

        var segment = new Segment { Start = 0, End = item.Total > 0 ? item.Total - 1 : long.MaxValue - 1 };
        item.Segments = new List<Segment> { segment };
        item.State = DownloadState.Downloading;

        var buffer = new byte[1 << 20];
        int n;
        while ((n = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), token).ConfigureAwait(false);
            segment.Done += n;
            item.Done = segment.Done;
        }

        if (item.Total > 0 && segment.Done != item.Total)
            throw new InvalidDataException("O download terminou com tamanho incompleto.");
    }

    private async Task VerifyAsync(DownloadItem item, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(item.Sha256))
        {
            item.VerifyOk = false;
            return;
        }

        item.State = DownloadState.Verifying;
        item.Done = 0;

        var expected = item.Sha256!;
        await using var stream = File.OpenRead(item.PartialPath);
        using var sha = SHA256.Create();

        var buffer = new byte[1 << 20];
        int n;
        while ((n = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buffer, 0, n, null, 0);
            item.Done += n;
        }
        sha.TransformFinalBlock([], 0, 0);

        var hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        Log?.Invoke("Descargas", $"SHA-256 de {item.FileName}: {hash}", "INFO");

        if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
        {
            DeleteFilesNow(item);
            throw new InvalidDataException("O SHA-256 não coincide com o catálogo — ficheiro removido por segurança.");
        }

        item.VerifyOk = true;
    }

    /// <summary>Calcula o SHA-256 de um IPSW já na pasta (botão «Verificar»).</summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var buffer = new byte[1 << 20];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            sha.TransformBlock(buffer, 0, n, null, 0);
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private void SaveMeta(DownloadItem item, bool force)
    {
        if (item.Deleted || string.IsNullOrWhiteSpace(item.Url) || item.Segments.Count == 0) return;
        if (!force && (DateTime.UtcNow - item.LastMeta).TotalSeconds < 2) return;
        item.LastMeta = DateTime.UtcNow;

        try
        {
            var model = new MetaModel
            {
                url = item.Url,
                total = item.Total,
                sha256 = item.Sha256,
                title = item.Title,
                segments = item.Segments.Select(s => new SegModel
                {
                    start = s.Start,
                    end = s.End,
                    done = s.Done,
                }).ToList(),
            };

            var tmp = item.MetaPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(model, MetaJson));
            File.Move(tmp, item.MetaPath, true);
        }
        catch (Exception ex)
        {
            Log?.Invoke("Descargas", "meta do download: " + ex.Message, "ERRO");
        }
    }

    private static MetaModel? LoadMeta(string metaPath)
    {
        try
        {
            if (!File.Exists(metaPath)) return null;
            return JsonSerializer.Deserialize<MetaModel>(File.ReadAllText(metaPath), MetaJson);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions MetaJson = new() { WriteIndented = false };

    private sealed class MetaModel
    {
        public string? url { get; set; }
        public long total { get; set; }
        public string? sha256 { get; set; }
        public string? title { get; set; }
        public List<SegModel> segments { get; set; } = new();
    }

    private sealed class SegModel
    {
        public long start { get; set; }
        public long end { get; set; }
        public long done { get; set; }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        _http.Dispose();

        DownloadItem[] items;
        lock (_gate) items = _live.ToArray();
        foreach (var item in items)
        {
            try { item.Cts?.Cancel(); } catch { }
        }
    }
}
