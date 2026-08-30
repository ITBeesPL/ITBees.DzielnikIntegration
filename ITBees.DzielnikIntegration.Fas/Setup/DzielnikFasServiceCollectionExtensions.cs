using ITBees.FAS.Payments.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ITBees.DzielnikIntegration.Fas.Setup;

public static class DzielnikFasServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje serwis fakturowania płatności FAS przez dzielnik.com (kontroler
    /// /DzielnikMonthlyInvoices jest wykrywany automatycznie) razem z automatem
    /// "płatność -> faktura": hookiem ISuccessfulPaymentInvoiceIssuer (TryAdd - własna
    /// implementacja hosta zarejestrowana wcześniej ma pierwszeństwo) i workerem w tle.
    /// Wymaga wcześniejszej rejestracji rdzenia: new DzielnikIntegrationSetup().Register(services),
    /// oraz wywołania <see cref="DzielnikFasDbModelBuilder.RegisterDbModels"/> w OnModelCreating
    /// hosta (plus migracja dla tabeli DzielnikInvoiceRecord).
    /// </summary>
    /// <typeparam name="TContext">DbContext hosta (musi zawierać PaymentSession i DzielnikInvoiceRecord).</typeparam>
    public static IServiceCollection AddDzielnikFasInvoicing<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IDzielnikPaymentInvoiceService, DzielnikPaymentInvoiceService<TContext>>();
        services.AddSingleton<DzielnikInvoiceQueue>();
        services.TryAddSingleton<ISuccessfulPaymentInvoiceIssuer, DzielnikSuccessfulPaymentInvoiceIssuer>();
        services.AddHostedService<DzielnikInvoiceIssuerBackgroundService>();
        return services;
    }
}
