namespace ITBees.DzielnikIntegration.Entities;

/// <summary>
/// Ustawienia integracji z dzielnik.com (publiczne API /api/v1) przechowywane w bazie aplikacji
/// hosta - konfigurowane w panelu administracyjnym (jeden wiersz). Rejestracja encji:
/// <see cref="Setup.DbModelBuilder.Register"/> w OnModelCreating hosta.
/// </summary>
public class DzielnikIntegrationSettings
{
    public int Id { get; set; }

    /// <summary>Główny przełącznik integracji.</summary>
    public bool Enabled { get; set; }

    /// <summary>Klucz API wygenerowany w Dzielniku (Ustawienia -> Dostęp dla systemów zewnętrznych).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Adres API Dzielnika, bez końcowego ukośnika.</summary>
    public string BaseUrl { get; set; } = "https://dzielnik.com";

    /// <summary>
    /// Gdy true, faktura utworzona w Dzielniku jest od razu wysyłana do KSeF
    /// (POST /api/v1/sales-invoices/{guid}/send-to-ksef). Wymaga skonfigurowanych
    /// poświadczeń KSeF firmy po stronie Dzielnika.
    /// </summary>
    public bool SendToKsef { get; set; }

    public DateTime? Modified { get; set; }
    public Guid? ModifiedByGuid { get; set; }
}
