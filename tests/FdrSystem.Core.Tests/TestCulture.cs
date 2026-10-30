using System.Globalization;
using System.Runtime.CompilerServices;

namespace FdrSystem.Core.Tests;

/// <summary>A UI é pt-PT; fixa a cultura para os testes de formatação serem estáveis.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var pt = new CultureInfo("pt-PT");
        CultureInfo.DefaultThreadCurrentCulture = pt;
        CultureInfo.DefaultThreadCurrentUICulture = pt;
        CultureInfo.CurrentCulture = pt;
        CultureInfo.CurrentUICulture = pt;
    }
}
