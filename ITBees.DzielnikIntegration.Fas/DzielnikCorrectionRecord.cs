namespace ITBees.DzielnikIntegration.Fas;

/// <summary>
/// Faktura korygująca wystawiona w dzielnik.com do faktury płatności FAS (np. przy zwrocie pieniędzy).
/// Unikalny indeks na CorrectedDzielnikInvoiceGuid: do jednej faktury najwyżej jedna korekta z integracji -
/// Dzielnik liczy korektę względem faktury pierwotnej i nie wystawia korekty korekty, więc kolejną zmianę
/// trzeba opisać ręcznie w Dzielniku.
/// </summary>
public class DzielnikCorrectionRecord
{
    public int Id { get; set; }

    /// <summary>Sesja płatności, której faktura jest korygowana.</summary>
    public Guid PaymentSessionGuid { get; set; }

    public string CorrectedDzielnikInvoiceGuid { get; set; } = "";
    public string CorrectedInvoiceNumber { get; set; } = "";

    /// <summary>Guid faktury korygującej po stronie Dzielnika.</summary>
    public string DzielnikInvoiceGuid { get; set; } = "";

    public string InvoiceNumber { get; set; } = "";

    /// <summary>Różnica brutto wprowadzona korektą - ujemna przy zwrocie.</summary>
    public decimal GrossTotal { get; set; }

    public string Reason { get; set; } = "";

    /// <summary>Odnośnik po stronie hosta, np. identyfikator zwrotu w Stripe.</summary>
    public string? ExternalReference { get; set; }

    /// <summary>Numer KSeF korekty; pusty, gdy jeszcze nie przyjęta (patrz <see cref="KsefError"/>).</summary>
    public string? KsefNumber { get; set; }

    public string? KsefError { get; set; }
    public DateTime Created { get; set; }
}
