namespace ITBees.DzielnikIntegration.Models;

/// <summary>
/// Klucz/adres do przetestowania; puste pola oznaczają wartości zapisane w ustawieniach -
/// dzięki temu można sprawdzić klucz przed zapisaniem.
/// </summary>
public class DzielnikConnectionTestIm
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
}

public class DzielnikConnectionTestVm
{
    public bool Success { get; set; }

    /// <summary>Komunikat do pokazania operatorowi (przy błędzie - treść błędu z Dzielnika).</summary>
    public string Message { get; set; } = "";

    public string? CompanyName { get; set; }

    /// <summary>Nazwa integracji nadana kluczowi w ustawieniach firmy w Dzielniku.</summary>
    public string? KeyName { get; set; }

    /// <summary>Adres IP, z którego Dzielnik widzi nasze wywołania - po nim uzupełnia się tam listę dozwolonych.</summary>
    public string? ClientIpAddress { get; set; }

    public string? ApiVersion { get; set; }

    /// <summary>Nadane zakresy dostępu wraz z polskim opisem.</summary>
    public List<DzielnikScopeVm> Scopes { get; set; } = new();
}

public class DzielnikScopeVm
{
    /// <summary>Maszynowy kod zakresu z kontraktu API, np. sales-invoices.</summary>
    public string Code { get; set; } = "";

    /// <summary>Opis po polsku, np. "Wystawianie faktur sprzedażowych".</summary>
    public string Name { get; set; } = "";
}
