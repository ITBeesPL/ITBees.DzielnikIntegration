using ITBees.DzielnikIntegration.Entities;
using ITBees.DzielnikIntegration.Fas.Models;
using ITBees.DzielnikIntegration.Services;
using ITBees.FAS.Payments.Interfaces.Models;
using ITBees.Interfaces.Platforms;
using ITBees.Models.Payments;
using ITBees.RestfulApiControllers.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ITBees.DzielnikIntegration.Fas;

public interface IDzielnikPaymentInvoiceService
{
    /// <summary>
    /// Tworzy w Dzielniku faktury za wszystkie opłacone płatności z bieżącego miesiąca
    /// (od pierwszego dnia miesiąca do teraz), a przy włączonym "Wysyłaj od razu do KSeF"
    /// od razu wysyła je do KSeF. Płatności z już utworzoną fakturą są pomijane (a te bez
    /// numeru KSeF - dosyłane), więc wywołanie można powtarzać bez ryzyka duplikatów.
    /// </summary>
    Task<DzielnikMonthlyInvoicesVm> CreateInvoicesForCurrentMonthAsync(CancellationToken ct = default);

    /// <summary>
    /// Tworzy w Dzielniku fakturę za jedną, właśnie opłaconą sesję płatności (ścieżka
    /// automatyczna - hook ISuccessfulPaymentInvoiceIssuer). Idempotentne jak przebieg
    /// miesięczny; przy wyłączonej integracji nic nie robi i zwraca null zamiast rzucać,
    /// bo działa w tle, poza żądaniem HTTP.
    /// </summary>
    Task<DzielnikMonthlyInvoicesVm?> CreateInvoiceForPaymentSessionAsync(Guid paymentSessionGuid,
        CancellationToken ct = default);
}

/// <summary>
/// Wystawia w dzielnik.com faktury za opłacone sesje płatności (stos FAS.Payments/Stripe).
/// Data sprzedaży = data otrzymania płatności (FinishedDate), data wystawienia = dziś.
/// Do danych nabywcy zawsze trafia adres e-mail, żeby Dzielnik mógł łatwo powiązać fakturę
/// z wpłatą. Wysyłka do KSeF jest opcjonalna (ustawienie SendToKsef) i idzie przez jawny
/// endpoint Dzielnika - wystawienie samo w sobie nigdy nie wysyła.
/// </summary>
/// <typeparam name="TContext">DbContext hosta z zarejestrowanymi PaymentSession i DzielnikInvoiceRecord
/// (wywołaj <see cref="Setup.DzielnikFasDbModelBuilder.RegisterDbModels"/> w OnModelCreating).</typeparam>
public class DzielnikPaymentInvoiceService<TContext> : IDzielnikPaymentInvoiceService where TContext : DbContext
{
    private const int MaxRateLimitRetries = 3;

    /// <summary>
    /// Stary bug w FAS ustawiał FinishedDate = 0001-01-01 przy potwierdzeniu Stripe przez powrót
    /// przeglądarki; taka data jest oczywiście śmieciowa, więc wszędzie traktujemy ją jak brak
    /// FinishedDate i używamy daty utworzenia sesji.
    /// </summary>
    private static readonly DateTime MinValidFinishedDate = new(2000, 1, 1);

    private readonly TContext _context;
    private readonly IDzielnikIntegrationSettingsService _settingsService;
    private readonly IDzielnikApiClient _dzielnikApiClient;
    private readonly IPlatformSettingsService _platformSettingsService;
    private readonly ILogger<DzielnikPaymentInvoiceService<TContext>> _logger;

    public DzielnikPaymentInvoiceService(TContext context,
        IDzielnikIntegrationSettingsService settingsService,
        IDzielnikApiClient dzielnikApiClient,
        IPlatformSettingsService platformSettingsService,
        ILogger<DzielnikPaymentInvoiceService<TContext>> logger)
    {
        _context = context;
        _settingsService = settingsService;
        _dzielnikApiClient = dzielnikApiClient;
        _platformSettingsService = platformSettingsService;
        _logger = logger;
    }

    public async Task<DzielnikMonthlyInvoicesVm> CreateInvoicesForCurrentMonthAsync(CancellationToken ct = default)
    {
        var settings = _settingsService.GetEnabledSettingsOrNull()
                       ?? throw new FasApiErrorException(
                           "Integracja z dzielnik.com jest wyłączona albo nie podano klucza API. Uzupełnij ustawienia w panelu.",
                           400);

        var now = DateTime.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var sessions = await _context.Set<PaymentSession>()
            .Include(x => x.InvoiceData).ThenInclude(x => x.SubscriptionPlan)
            .Include(x => x.CreatedBy)
            .Where(x => x.Finished && x.Success && !x.Refunded && x.InvoiceDataGuid != null
                        && (x.FinishedDate >= monthStart
                            || ((x.FinishedDate == null || x.FinishedDate < MinValidFinishedDate)
                                && x.Created >= monthStart)))
            .OrderBy(x => x.FinishedDate == null || x.FinishedDate < MinValidFinishedDate
                ? x.Created
                : x.FinishedDate.Value)
            .ToListAsync(ct);

        var sessionGuids = sessions.Select(x => x.Guid).ToList();
        var records = await _context.Set<DzielnikInvoiceRecord>()
            .Where(x => sessionGuids.Contains(x.PaymentSessionGuid))
            .ToDictionaryAsync(x => x.PaymentSessionGuid, ct);

        var platformName = _platformSettingsService.GetSetting("PlatformName");
        var result = new DzielnikMonthlyInvoicesVm();

        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();

            records.TryGetValue(session.Guid, out var record);
            await ProcessPaidSessionAsync(settings, session, record, platformName, result, ct);
        }

        _logger.LogInformation(
            "Dzielnik: utworzono {Created} faktur, wysłano do KSeF {Ksef}, pominięto {Already} już istniejących, {Free} darmowych, błędów: {Errors}.",
            result.CreatedCount, result.SentToKsefCount, result.AlreadyCreatedCount, result.SkippedFreeCount,
            result.Errors.Count);

        return result;
    }

    public async Task<DzielnikMonthlyInvoicesVm?> CreateInvoiceForPaymentSessionAsync(Guid paymentSessionGuid,
        CancellationToken ct = default)
    {
        var settings = _settingsService.GetEnabledSettingsOrNull();
        if (settings == null)
        {
            _logger.LogDebug(
                "Dzielnik: integracja wyłączona - pomijam automatyczną fakturę dla sesji {SessionGuid}.",
                paymentSessionGuid);
            return null;
        }

        var session = await _context.Set<PaymentSession>()
            .Include(x => x.InvoiceData).ThenInclude(x => x.SubscriptionPlan)
            .Include(x => x.CreatedBy)
            .FirstOrDefaultAsync(x => x.Guid == paymentSessionGuid, ct);

        var result = new DzielnikMonthlyInvoicesVm();

        if (session == null || !session.Finished || !session.Success || session.Refunded ||
            session.InvoiceDataGuid == null)
        {
            _logger.LogWarning(
                "Dzielnik: sesja płatności {SessionGuid} nie kwalifikuje się do faktury (nie istnieje, nieopłacona albo zwrócona).",
                paymentSessionGuid);
            result.Errors.Add(new DzielnikInvoiceErrorVm
            {
                PaymentSessionGuid = paymentSessionGuid,
                Error = "Sesja płatności nie istnieje albo nie jest opłaconą, niezwróconą płatnością z danymi do faktury."
            });
            return result;
        }

        var record = await _context.Set<DzielnikInvoiceRecord>()
            .FirstOrDefaultAsync(x => x.PaymentSessionGuid == session.Guid, ct);

        var platformName = _platformSettingsService.GetSetting("PlatformName");
        await ProcessPaidSessionAsync(settings, session, record, platformName, result, ct);

        if (result.CreatedCount > 0)
        {
            _logger.LogInformation(
                "Dzielnik: automatycznie utworzono fakturę {Number} dla sesji płatności {SessionGuid}.",
                result.CreatedInvoices.FirstOrDefault()?.InvoiceNumber, session.Guid);
        }

        return result;
    }

    /// <summary>
    /// Obsługuje jedną opłaconą sesję: pomija już zafakturowane (dosyłając zaległy KSeF),
    /// pomija plany darmowe, dla pozostałych tworzy fakturę w Dzielniku i - przy włączonej
    /// wysyłce - przekazuje ją do KSeF. Wynik dopisuje do przekazanego akumulatora.
    /// </summary>
    private async Task ProcessPaidSessionAsync(DzielnikIntegrationSettings settings, PaymentSession session,
        DzielnikInvoiceRecord? record, string platformName, DzielnikMonthlyInvoicesVm result, CancellationToken ct)
    {
        var buyerEmail = ResolveBuyerEmail(session);

        if (record != null)
        {
            result.AlreadyCreatedCount++;

            // Faktura powstała wcześniej, ale KSeF jej jeszcze nie przyjął - dosyłamy,
            // dopóki operator ma włączoną wysyłkę. Endpoint Dzielnika jest idempotentny.
            if (settings.SendToKsef && string.IsNullOrWhiteSpace(record.KsefNumber))
            {
                await TrySendToKsefAsync(settings, record, session.Guid, buyerEmail, result, ct);
            }

            return;
        }

        var plan = session.InvoiceData?.SubscriptionPlan;
        if (plan == null)
        {
            result.Errors.Add(new DzielnikInvoiceErrorVm
            {
                PaymentSessionGuid = session.Guid,
                BuyerEmail = buyerEmail,
                Error = "Sesja płatności nie ma danych do faktury (brak planu subskrypcji)."
            });
            return;
        }

        // Plany darmowe/testowe nie tworzą sprzedaży - nie ma czego fakturować.
        if (plan.NetValue <= 0m)
        {
            result.SkippedFreeCount++;
            return;
        }

        var paymentDate = GetPaymentDate(session);
        var request = BuildInvoiceRequest(session, plan, platformName, paymentDate, buyerEmail);

        try
        {
            var invoice = await SendWithRateLimitRetryAsync(
                () => _dzielnikApiClient.CreateSalesInvoiceAsync(settings.BaseUrl, settings.ApiKey, request, ct),
                ct);
            record = await SaveRecordAsync(session.Guid, invoice, ct);

            result.CreatedCount++;
            var createdVm = new DzielnikCreatedInvoiceVm
            {
                InvoiceNumber = invoice.Number,
                BuyerName = request.Buyer.Name ?? "",
                BuyerEmail = buyerEmail,
                GrossTotal = invoice.GrossTotal,
                SaleDate = paymentDate.Date
            };
            result.CreatedInvoices.Add(createdVm);

            if (settings.SendToKsef)
            {
                var ksefNumber = await TrySendToKsefAsync(settings, record, session.Guid, buyerEmail, result, ct);
                createdVm.KsefNumber = ksefNumber;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e,
                "Nie udało się utworzyć faktury w Dzielniku dla sesji płatności {SessionGuid}.", session.Guid);
            result.Errors.Add(new DzielnikInvoiceErrorVm
            {
                PaymentSessionGuid = session.Guid,
                BuyerEmail = buyerEmail,
                Error = e.Message
            });
        }
    }

    /// <summary>Numer KSeF przy sukcesie; null przy odmowie/błędzie (dopisanym do wyniku).</summary>
    private async Task<string?> TrySendToKsefAsync(DzielnikIntegrationSettings settings, DzielnikInvoiceRecord record,
        Guid paymentSessionGuid, string? buyerEmail, DzielnikMonthlyInvoicesVm result, CancellationToken ct)
    {
        try
        {
            var sent = await SendWithRateLimitRetryAsync(
                () => _dzielnikApiClient.SendInvoiceToKsefAsync(settings.BaseUrl, settings.ApiKey,
                    record.DzielnikInvoiceGuid, ct),
                ct);

            if (!string.IsNullOrWhiteSpace(sent.KsefNumber))
            {
                record.KsefNumber = sent.KsefNumber;
                await _context.Set<DzielnikInvoiceRecord>()
                    .Where(x => x.Id == record.Id)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.KsefNumber, sent.KsefNumber), ct);
                result.SentToKsefCount++;
                return sent.KsefNumber;
            }

            result.Errors.Add(new DzielnikInvoiceErrorVm
            {
                PaymentSessionGuid = paymentSessionGuid,
                BuyerEmail = buyerEmail,
                Error = string.IsNullOrWhiteSpace(sent.KsefError)
                    ? $"KSeF nie przyjął faktury {record.InvoiceNumber} (status {sent.Status})."
                    : $"KSeF odrzucił fakturę {record.InvoiceNumber}: {sent.KsefError}"
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Wysyłka faktury {Number} do KSeF przez Dzielnika nie powiodła się.",
                record.InvoiceNumber);
            result.Errors.Add(new DzielnikInvoiceErrorVm
            {
                PaymentSessionGuid = paymentSessionGuid,
                BuyerEmail = buyerEmail,
                Error = $"Wysyłka do KSeF: {e.Message}"
            });
        }

        return null;
    }

    private DzielnikApiSalesInvoiceRequest BuildInvoiceRequest(PaymentSession session, PlatformSubscriptionPlan plan,
        string platformName, DateTime paymentDate, string? buyerEmail)
    {
        return new DzielnikApiSalesInvoiceRequest
        {
            // Data wystawienia = dziś; data sprzedaży = data otrzymania płatności.
            IssueDate = DateTime.Today,
            SaleDate = paymentDate.Date,
            Currency = string.IsNullOrWhiteSpace(plan.Currency) ? null : plan.Currency.ToUpperInvariant(),
            Buyer = BuildBuyer(session, buyerEmail),
            Lines =
            {
                new DzielnikApiInvoiceLine
                {
                    Name = $"{platformName} - {plan.PlanName}",
                    Unit = "szt.",
                    Quantity = 1m,
                    UnitNetPrice = plan.NetValue,
                    VatRate = plan.VatPercentage
                }
            },
            IsPaid = true,
            PaidAt = paymentDate,
            Notes = $"Płatność {session.PaymentOperator} z {paymentDate:yyyy-MM-dd HH:mm}, sesja {session.Guid}"
        };
    }

    private static DzielnikApiBuyer BuildBuyer(PaymentSession session, string? buyerEmail)
    {
        var invoiceData = session.InvoiceData;
        if (invoiceData.InvoiceRequested && !string.IsNullOrWhiteSpace(invoiceData.CompanyName))
        {
            return new DzielnikApiBuyer
            {
                Name = invoiceData.CompanyName,
                Nip = string.IsNullOrWhiteSpace(invoiceData.NIP) ? null : invoiceData.NIP.Trim(),
                Street = invoiceData.Street,
                PostCode = invoiceData.PostCode,
                City = invoiceData.City,
                CountryCode = NormalizeCountryCode(invoiceData.Country),
                Email = buyerEmail
            };
        }

        // Zakup bez danych do faktury ("paragon") - nabywcę identyfikuje adres e-mail,
        // po którym Dzielnik połączy fakturę z wpłatą.
        return new DzielnikApiBuyer
        {
            Name = string.IsNullOrWhiteSpace(buyerEmail) ? "Klient detaliczny" : buyerEmail,
            Email = buyerEmail
        };
    }

    private static string? ResolveBuyerEmail(PaymentSession session)
    {
        var email = session.InvoiceData?.InvoiceEmail;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = session.CreatedBy?.Email;
        }

        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    /// <summary>Data otrzymania płatności; śmieciowe FinishedDate (0001-01-01) zastępuje data utworzenia sesji.</summary>
    private static DateTime GetPaymentDate(PaymentSession session)
    {
        return session.FinishedDate is { } finished && finished >= MinValidFinishedDate
            ? finished
            : session.Created;
    }

    /// <summary>Dzielnik przyjmuje dwuliterowy kod ISO; wszystko inne zostawiamy puste (domyślnie PL).</summary>
    private static string? NormalizeCountryCode(string? country)
    {
        var trimmed = country?.Trim();
        return trimmed?.Length == 2 ? trimmed.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Publiczne API Dzielnika limituje zapisy (20/min na klucz); przy 429 czekamy tyle,
    /// ile każe nagłówek Retry-After, i ponawiamy - zamiast tracić resztę miesiąca.
    /// </summary>
    private async Task<DzielnikApiSalesInvoice> SendWithRateLimitRetryAsync(
        Func<Task<DzielnikApiSalesInvoice>> operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DzielnikApiException e) when (e.StatusCode == 429 && attempt < MaxRateLimitRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Clamp(e.RetryAfterSeconds ?? 5, 1, 60));
                _logger.LogInformation("Dzielnik: limit zapytań, czekam {Delay}s przed ponowieniem.",
                    delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task<DzielnikInvoiceRecord> SaveRecordAsync(Guid paymentSessionGuid, DzielnikApiSalesInvoice invoice,
        CancellationToken ct)
    {
        var record = new DzielnikInvoiceRecord
        {
            PaymentSessionGuid = paymentSessionGuid,
            DzielnikInvoiceGuid = invoice.Guid,
            InvoiceNumber = invoice.Number,
            GrossTotal = invoice.GrossTotal,
            Created = DateTime.Now
        };

        _context.Add(record);
        try
        {
            await _context.SaveChangesAsync(ct);
            return record;
        }
        catch (DbUpdateException e)
        {
            // Unikalny indeks na PaymentSessionGuid - równoległy przebieg zdążył pierwszy.
            // Faktura w Dzielniku mogła powstać podwójnie; to trzeba zgłosić, nie ukryć.
            _context.Entry(record).State = EntityState.Detached;
            throw new InvalidOperationException(
                $"Faktura {invoice.Number} została utworzona w Dzielniku, ale zapis rekordu się nie powiódł " +
                "(równoległy przebieg?). Sprawdź w Dzielniku, czy nie powstał duplikat.", e);
        }
    }
}
