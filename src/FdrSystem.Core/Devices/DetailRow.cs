namespace FdrSystem.Core.Devices;

/// <summary>Uma linha da tabela de detalhes (Equivalente ao DetailRow do WPF).</summary>
public sealed record DetailRow(string Key, string Group, string Label, string Value);
