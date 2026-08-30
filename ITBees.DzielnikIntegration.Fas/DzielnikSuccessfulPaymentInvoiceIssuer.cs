using System.Threading.Channels;
using ITBees.FAS.Payments.Interfaces;
using ITBees.FAS.Payments.Interfaces.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.DzielnikIntegration.Fas;

/// <summary>
/// Kolejka sesji płatności czekających na automatyczną fakturę w Dzielniku. Oddziela szybki
/// hook wołany z pipeline'u webhooka płatności od faktycznej (wolnej, sieciowej) pracy
/// wykonywanej w tle przez <see cref="DzielnikInvoiceIssuerBackgroundService"/>.
/// </summary>
public class DzielnikInvoiceQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public bool TryEnqueue(Guid paymentSessionGuid) => _channel.Writer.TryWrite(paymentSessionGuid);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Automat "płatność -> faktura w Dzielniku": implementacja hooka FAS wołanego po domknięciu
/// opłaconej sesji (webhooki checkout.session.completed i invoice.payment_succeeded).
/// Zgodnie z kontraktem hooka tylko kolejkuje pracę i natychmiast wraca - fakturę tworzy
/// worker w tle. Idempotencję gwarantuje DzielnikInvoiceRecord (unikalny indeks na
/// PaymentSessionGuid), więc zdublowane webhooki i ręczny przebieg miesięczny nie robią duplikatów.
/// </summary>
public class DzielnikSuccessfulPaymentInvoiceIssuer : ISuccessfulPaymentInvoiceIssuer
{
    private readonly DzielnikInvoiceQueue _queue;
    private readonly ILogger<DzielnikSuccessfulPaymentInvoiceIssuer> _logger;

    public DzielnikSuccessfulPaymentInvoiceIssuer(DzielnikInvoiceQueue queue,
        ILogger<DzielnikSuccessfulPaymentInvoiceIssuer> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public void IssueInvoiceForPaidSession(PaymentSession paymentSession)
    {
        if (_queue.TryEnqueue(paymentSession.Guid))
        {
            _logger.LogDebug("Dzielnik: sesja płatności {SessionGuid} zakolejkowana do zafakturowania.",
                paymentSession.Guid);
        }
        else
        {
            // Nieograniczony kanał odmawia zapisu tylko po zamknięciu (shutdown aplikacji);
            // płatność zafakturuje wtedy ręczny przebieg miesięczny.
            _logger.LogWarning(
                "Dzielnik: nie udało się zakolejkować sesji {SessionGuid} do zafakturowania - kolejka zamknięta.",
                paymentSession.Guid);
        }
    }
}

/// <summary>
/// Worker w tle zdejmujący z kolejki opłacone sesje i tworzący dla nich faktury w Dzielniku
/// (z opcjonalną wysyłką do KSeF). Każda sesja dostaje świeży scope DI, więc czyta już
/// zatwierdzony stan bazy. Błąd pojedynczej sesji jest logowany i nie zatrzymuje kolejki -
/// taką płatność dosyła ręczny przebieg "Utwórz faktury z bieżącego miesiąca".
/// </summary>
public class DzielnikInvoiceIssuerBackgroundService : BackgroundService
{
    private readonly DzielnikInvoiceQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DzielnikInvoiceIssuerBackgroundService> _logger;

    public DzielnikInvoiceIssuerBackgroundService(DzielnikInvoiceQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DzielnikInvoiceIssuerBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var paymentSessionGuid in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var invoiceService = scope.ServiceProvider.GetRequiredService<IDzielnikPaymentInvoiceService>();
                var result = await invoiceService.CreateInvoiceForPaymentSessionAsync(paymentSessionGuid,
                    stoppingToken);

                foreach (var error in result?.Errors ?? [])
                {
                    _logger.LogWarning(
                        "Dzielnik: automatyczna faktura dla sesji {SessionGuid} nie powstała: {Error}",
                        error.PaymentSessionGuid, error.Error);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Dzielnik: nieoczekiwany błąd automatycznego fakturowania sesji {SessionGuid}.",
                    paymentSessionGuid);
            }
        }
    }
}
