using ITBees.DzielnikIntegration.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.DzielnikIntegration.Setup;

public class DzielnikIntegrationSetup
{
    /// <summary>
    /// Rejestruje klienta publicznego API Dzielnika (nazwany HttpClient), serwis ustawień
    /// (przechowywanych w bazie hosta - patrz <see cref="DbModelBuilder.Register"/>) i test
    /// połączenia. Kontrolery /DzielnikIntegrationSettings i /DzielnikConnectionTest są
    /// wykrywane automatycznie przez ASP.NET.
    /// </summary>
    public void Register(IServiceCollection services)
    {
        // 90 s, bo jawna wysyłka do KSeF (sesja KSeF po stronie Dzielnika) potrafi trwać
        // kilkanaście-kilkadziesiąt sekund.
        services.AddHttpClient(DzielnikApiClient.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(90))
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddTransient<IDzielnikIntegrationSettingsService, DzielnikIntegrationSettingsService>();
        services.AddTransient<IDzielnikApiClient, DzielnikApiClient>();
        services.AddTransient<IDzielnikConnectionTestService, DzielnikConnectionTestService>();
    }
}
