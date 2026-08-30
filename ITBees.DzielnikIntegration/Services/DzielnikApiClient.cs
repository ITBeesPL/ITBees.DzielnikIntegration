using System.Net.Http.Json;
using System.Text.Json;

namespace ITBees.DzielnikIntegration.Services;

public class DzielnikApiClient : IDzielnikApiClient
{
    public const string HttpClientName = "DzielnikApi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;

    // Celowo jeden konstruktor: hosty rejestrują "goły" HttpClient w DI, więc drugi konstruktor
    // przyjmujący HttpClient kończyłby się błędem "ambiguous constructors" (tak jak w ITBees.Inpost).
    public DzielnikApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DzielnikApiStatus> GetStatusAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, baseUrl, "/api/v1/status", apiKey);
        return await SendAsync<DzielnikApiStatus>(request, ct);
    }

    public async Task<DzielnikApiSalesInvoice> CreateSalesInvoiceAsync(string baseUrl, string apiKey,
        DzielnikApiSalesInvoiceRequest invoiceRequest, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, baseUrl, "/api/v1/sales-invoices", apiKey);
        request.Content = JsonContent.Create(invoiceRequest, options: JsonOptions);
        return await SendAsync<DzielnikApiSalesInvoice>(request, ct);
    }

    public async Task<DzielnikApiSalesInvoice> SendInvoiceToKsefAsync(string baseUrl, string apiKey,
        string invoiceGuid, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, baseUrl,
            $"/api/v1/sales-invoices/{invoiceGuid}/send-to-ksef", apiKey);
        return await SendAsync<DzielnikApiSalesInvoice>(request, ct);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string baseUrl, string path, string apiKey)
    {
        var request = new HttpRequestMessage(method, $"{baseUrl.TrimEnd('/')}{path}");
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new DzielnikApiException("connection_failed",
                $"Nie udało się połączyć z API Dzielnika ({request.RequestUri?.Host}): {e.Message}", 0, null);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested == false)
        {
            throw new DzielnikApiException("timeout",
                "API Dzielnika nie odpowiedziało w wyznaczonym czasie.", 0, null);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var result = Deserialize<T>(body);
                if (result == null)
                {
                    throw new DzielnikApiException("empty_response", "Pusta odpowiedź z API Dzielnika.",
                        (int)response.StatusCode, null);
                }

                return result;
            }

            var error = Deserialize<DzielnikApiError>(body);
            throw new DzielnikApiException(
                string.IsNullOrWhiteSpace(error?.Error) ? $"http_{(int)response.StatusCode}" : error.Error,
                string.IsNullOrWhiteSpace(error?.Message)
                    ? $"API Dzielnika odpowiedziało kodem {(int)response.StatusCode}."
                    : error.Message,
                (int)response.StatusCode,
                error?.RetryAfterSeconds);
        }
    }

    private static T? Deserialize<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
