using System.Xml.Linq;

namespace FdrSystem.Core.Devices;

/// <summary>Leitura de plists XML devolvidos por <c>ideviceinfo -x</c>.</summary>
public static class PlistReader
{
    /// <summary>Chaves/valores do dicionário de topo (valores simples).</summary>
    public static Dictionary<string, string> ToDictionary(XDocument doc)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var dict = doc.Root?.Element("dict") ?? doc.Root;
        if (dict is null) return result;

        foreach (var key in dict.Elements("key"))
        {
            var value = key.ElementsAfterSelf().FirstOrDefault();
            if (value is not null && !value.HasElements) result[key.Value] = value.Value;
        }

        return result;
    }

    public static Dictionary<string, string> ToDictionary(string xml) =>
        string.IsNullOrWhiteSpace(xml) ? [] : ToDictionary(XDocument.Parse(xml));
}
