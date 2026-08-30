namespace ITBees.DzielnikIntegration.Services;

/// <summary>
/// Klient publicznego API dzielnik.com (/api/v1, uwierzytelnianie nagłówkiem X-Api-Key).
/// Klucz i adres przychodzą per wywołanie, bo pochodzą z bazy (panel administracyjny).
/// </summary>
public interface IDzielnikApiClient
{
    /// <summary>GET /api/v1/status - sprawdzenie klucza i nadanych zakresów.</summary>
    Task<DzielnikApiStatus> GetStatusAsync(string baseUrl, string apiKey, CancellationToken ct = default);

    /// <summary>POST /api/v1/sales-invoices - wystawienie faktury sprzedażowej (bez wysyłki do KSeF).</summary>
    Task<DzielnikApiSalesInvoice> CreateSalesInvoiceAsync(string baseUrl, string apiKey,
        DzielnikApiSalesInvoiceRequest request, CancellationToken ct = default);

    /// <summary>
    /// POST /api/v1/sales-invoices/{guid}/send-to-ksef - jawna wysyłka do KSeF. Odpowiedź niesie
    /// stan faktury po próbie: status "sent-to-ksef" z numerem w KsefNumber albo "ksef-rejected"
    /// z przyczyną w KsefError. Wywołanie dla dokumentu już przyjętego przez KSeF zwraca jego stan
    /// bez błędu, więc ponowienia są bezpieczne.
    /// </summary>
    Task<DzielnikApiSalesInvoice> SendInvoiceToKsefAsync(string baseUrl, string apiKey,
        string invoiceGuid, CancellationToken ct = default);
}

/// <summary>Odpowiedź GET /api/v1/status.</summary>
public class DzielnikApiStatus
{
    public string CompanyName { get; set; } = "";
    public string CompanyGuid { get; set; } = "";
    public string KeyName { get; set; } = "";
    public string? ClientIpAddress { get; set; }
    public List<string> Scopes { get; set; } = new();
    public string ApiVersion { get; set; } = "";
}

public class DzielnikApiBuyer
{
    public string? Name { get; set; }
    public string? Nip { get; set; }
    public string? Street { get; set; }
    public string? PostCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public string? Email { get; set; }
}

public class DzielnikApiInvoiceLine
{
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitNetPrice { get; set; }
    public int VatRate { get; set; }
}

/// <summary>Żądanie POST /api/v1/sales-invoices - kwoty pozycji i sumy liczy serwer Dzielnika.</summary>
public class DzielnikApiSalesInvoiceRequest
{
    /// <summary>Jawny numer dokumentu; pusty = numer nada Dzielnik w trybie numeracji firmy.</summary>
    public string? Number { get; set; }

    public DateTime? IssueDate { get; set; }
    public DateTime? SaleDate { get; set; }
    public string? Currency { get; set; }
    public DzielnikApiBuyer Buyer { get; set; } = new();
    public List<DzielnikApiInvoiceLine> Lines { get; set; } = new();
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Faktura zwrócona przez Dzielnika (interesujący nas podzbiór kontraktu).</summary>
public class DzielnikApiSalesInvoice
{
    public string Guid { get; set; } = "";
    public string Number { get; set; } = "";

    /// <summary>draft / issued / sending-to-ksef / sent-to-ksef / ksef-rejected / imported.</summary>
    public string Status { get; set; } = "";

    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public string? KsefNumber { get; set; }
    public string? KsefError { get; set; }
}

/// <summary>Kształt błędu publicznego API Dzielnika - jeden dla wszystkich odpowiedzi innych niż sukces.</summary>
public class DzielnikApiError
{
    public string Error { get; set; } = "";
    public string Message { get; set; } = "";
    public int? RetryAfterSeconds { get; set; }
}

/// <summary>Błąd zwrócony przez API Dzielnika (albo brak możliwości połączenia).</summary>
public class DzielnikApiException : Exception
{
    public DzielnikApiException(string errorCode, string message, int statusCode, int? retryAfterSeconds)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>Maszynowy kod błędu z kontraktu, np. unauthorized, ip_not_allowed, scope_not_granted.</summary>
    public string ErrorCode { get; }

    public int StatusCode { get; }

    /// <summary>Ile sekund odczekać przed ponowieniem; wypełnione tylko przy 429.</summary>
    public int? RetryAfterSeconds { get; }
}
