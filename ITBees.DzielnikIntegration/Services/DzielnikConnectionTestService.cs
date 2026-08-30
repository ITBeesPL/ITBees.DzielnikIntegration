using ITBees.DzielnikIntegration.Models;

namespace ITBees.DzielnikIntegration.Services;

public interface IDzielnikConnectionTestService
{
    Task<DzielnikConnectionTestVm> TestAsync(DzielnikConnectionTestIm im, CancellationToken ct = default);
}

/// <summary>
/// Testuje połączenie z publicznym API Dzielnika wywołaniem GET /api/v1/status i tłumaczy
/// nadane zakresy na polskie opisy pokazywane w panelu.
/// </summary>
public class DzielnikConnectionTestService : IDzielnikConnectionTestService
{
    /// <summary>Kody zakresów z kontraktu publicznego API Dzielnika -> opis po polsku.</summary>
    private static readonly Dictionary<string, string> ScopeNames = new()
    {
        ["sales-invoices"] = "Wystawianie i odczyt faktur sprzedażowych (w tym wysyłka do KSeF)",
        ["counterparties"] = "Odczyt i zakładanie kontrahentów",
        ["bank-history"] = "Odczyt historii rachunków bankowych"
    };

    private readonly IDzielnikIntegrationSettingsService _settingsService;
    private readonly IDzielnikApiClient _dzielnikApiClient;

    public DzielnikConnectionTestService(IDzielnikIntegrationSettingsService settingsService,
        IDzielnikApiClient dzielnikApiClient)
    {
        _settingsService = settingsService;
        _dzielnikApiClient = dzielnikApiClient;
    }

    public async Task<DzielnikConnectionTestVm> TestAsync(DzielnikConnectionTestIm im, CancellationToken ct = default)
    {
        // Puste pola oznaczają wartości zapisane - dzięki temu klucz można sprawdzić przed zapisem.
        var saved = _settingsService.Get();
        var apiKey = string.IsNullOrWhiteSpace(im.ApiKey) ? saved.ApiKey : im.ApiKey.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(im.BaseUrl) ? saved.BaseUrl : im.BaseUrl.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new DzielnikConnectionTestVm
            {
                Success = false,
                Message = "Podaj klucz API wygenerowany w Dzielniku (Ustawienia -> Dostęp dla systemów zewnętrznych)."
            };
        }

        try
        {
            var status = await _dzielnikApiClient.GetStatusAsync(baseUrl, apiKey, ct);
            return new DzielnikConnectionTestVm
            {
                Success = true,
                Message = status.Scopes.Count == 0
                    ? "Połączenie działa, ale klucz nie ma nadanego żadnego zakresu - włącz obszary w ustawieniach firmy w Dzielniku."
                    : $"Połączenie działa. Klucz \"{status.KeyName}\" firmy {status.CompanyName}.",
                CompanyName = status.CompanyName,
                KeyName = status.KeyName,
                ClientIpAddress = status.ClientIpAddress,
                ApiVersion = status.ApiVersion,
                Scopes = status.Scopes
                    .Select(code => new DzielnikScopeVm
                    {
                        Code = code,
                        Name = ScopeNames.TryGetValue(code, out var name) ? name : code
                    })
                    .ToList()
            };
        }
        catch (DzielnikApiException e)
        {
            return new DzielnikConnectionTestVm
            {
                Success = false,
                Message = e.ErrorCode switch
                {
                    "unauthorized" => "Dzielnik odrzucił klucz API (brak klucza, klucz nieprawidłowy albo unieważniony).",
                    "access_disabled" => "Firma w Dzielniku ma wyłączony główny przełącznik dostępu dla systemów zewnętrznych.",
                    "ip_not_allowed" => $"Adres IP serwera nie jest na liście dozwolonych w Dzielniku. {e.Message}",
                    _ => e.Message
                }
            };
        }
    }
}
