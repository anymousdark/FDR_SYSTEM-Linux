using System.Globalization;
using System.Text.Json;

namespace FdrSystem.Core.Firmware;

/// <summary>Firmware publicado no catálogo público da Apple (api.ipsw.me).</summary>
public sealed record IpswFirmware(
    string Version,
    string Build,
    string Url,
    long Size,
    string? Sha256,
    bool Signed,
    DateTimeOffset? Released)
{
    public string DateText => Released is { } d ? d.ToLocalTime().ToString("dd/MM/yyyy") : "—";

    public string SizeText => Size > 0 ? DownloadItem.Fmt(Size) : "—";

    public string SignedText => Signed ? "Assinado" : "Expirado";
}

/// <summary>Modelo com a lista completa de firmwares (assinados e expirados).</summary>
public sealed record IpswDeviceInfo(string Identifier, string Name, IReadOnlyList<IpswFirmware> Firmwares)
{
    /// <summary>Firmware mais recente que a Apple ainda assina.</summary>
    public IpswFirmware? LatestSigned => Firmwares.FirstOrDefault(f => f.Signed);

    /// <summary>Firmware mais recente de todos (assinado ou não).</summary>
    public IpswFirmware? Latest => Firmwares.FirstOrDefault();
}

/// <summary>Entrada da lista de aparelhos suportados.</summary>
public sealed record IpswDevice(string Identifier, string Name);

/// <summary>
/// Cliente do catálogo público <c>api.ipsw.me</c>. Não usa a API privada da Apple,
/// não pede credenciais e não envia dados do aparelho.
/// </summary>
public static class IpswCatalog
{
    public const string BaseUrl = "https://api.ipsw.me/v4";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(20),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>GET com até 4 tentativas para falhas transitórias de rede/5xx.</summary>
    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < 3 && (ex.StatusCode is null || (int)ex.StatusCode >= 500))
            {
                await Task.Delay(350 * (attempt + 1), ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                await Task.Delay(350 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Catálogo de um modelo, ex.: <c>iPhone13,2</c>.</summary>
    public static async Task<IpswDeviceInfo> GetDeviceAsync(string identifier, CancellationToken ct = default)
    {
        var json = await GetStringAsync($"{BaseUrl}/device/{Uri.EscapeDataString(identifier)}?type=ipsw", ct)
            .ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = Str(root, "name") ?? identifier;
        var list = new List<IpswFirmware>();

        if (root.TryGetProperty("firmwares", out var firmwares) && firmwares.ValueKind == JsonValueKind.Array)
        {
            foreach (var fw in firmwares.EnumerateArray())
            {
                var version = Str(fw, "version");
                if (string.IsNullOrWhiteSpace(version)) continue;

                var build = Str(fw, "buildid") ?? "";
                var url = Str(fw, "url") ?? "";
                var size = Num(fw, "filesize") ?? Num(fw, "size") ?? 0;
                var sha = Str(fw, "sha256sum");
                var signed = fw.TryGetProperty("signed", out var s) && s.ValueKind == JsonValueKind.True;

                DateTimeOffset? released = null;
                foreach (var key in new[] { "releasedate", "released" })
                {
                    var raw = Str(fw, key);
                    if (raw is null) continue;
                    if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        released = parsed;
                        break;
                    }
                }

                list.Add(new IpswFirmware(version, build, url, size, sha, signed, released));
            }
        }

        return new IpswDeviceInfo(identifier, name, list);
    }

    /// <summary>Todos os modelos conhecidos pelo catálogo.</summary>
    public static async Task<IReadOnlyList<IpswDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var json = await GetStringAsync($"{BaseUrl}/devices", ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var list = new List<IpswDevice>();

        foreach (var dev in doc.RootElement.EnumerateArray())
        {
            var id = Str(dev, "identifier");
            if (string.IsNullOrWhiteSpace(id)) continue;
            list.Add(new IpswDevice(id, Str(dev, "name") ?? id));
        }

        return list;
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : null;
}
