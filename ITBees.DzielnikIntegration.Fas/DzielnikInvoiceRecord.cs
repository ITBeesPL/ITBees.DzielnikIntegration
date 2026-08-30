namespace ITBees.DzielnikIntegration.Fas;

/// <summary>
/// Zapis faktury utworzonej w dzielnik.com dla opłaconej sesji płatności (FAS).
/// Unikalny indeks na PaymentSessionGuid gwarantuje najwyżej jedną fakturę na płatność,
/// niezależnie od tego, ile razy operator kliknie "Utwórz faktury z bieżącego miesiąca".
/// </summary>
public class DzielnikInvoiceRecord
{
    public int Id { get; set; }

    public Guid PaymentSessionGuid { get; set; }

    /// <summary>Guid faktury po stronie Dzielnika (z odpowiedzi POST /api/v1/sales-invoices).</summary>
    public string DzielnikInvoiceGuid { get; set; } = "";

    /// <summary>Numer nadany przez Dzielnika.</summary>
    public string InvoiceNumber { get; set; } = "";

    public decimal GrossTotal { get; set; }

    /// <summary>
    /// Numer KSeF po udanej wysyłce przez Dzielnika; pusty, gdy dokument nie został (jeszcze)
    /// wysłany - kolejne przebiegi z włączonym "Wysyłaj od razu do KSeF" ponawiają wysyłkę.
    /// </summary>
    public string? KsefNumber { get; set; }

    public DateTime Created { get; set; }
}
