using ITBees.DzielnikIntegration.Entities;
using ITBees.DzielnikIntegration.Services;
using ITBees.RestfulApiControllers.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ITBees.DzielnikIntegration.Fas;

public interface IDzielnikInvoiceCorrectionService
{
    /// <summary>
    /// Wystawia w Dzielniku korektę faktury jednej sesji płatności (np. po zwrocie pieniędzy) i - przy włączonym
    /// "Wysyłaj od razu do KSeF" - wysyła ją do KSeF. Faktura musi być już w KSeF (Dzielnik koryguje tylko takie);
    /// jeśli jeszcze nie jest, najpierw jest do niego dosyłana. Do jednej faktury najwyżej jedna korekta.
    /// </summary>
    Task<DzielnikCorrectionVm> CreateCorrectionAsync(DzielnikCorrectionIm correctionIm, CancellationToken ct = default);

    /// <summary>Ponawia wysyłkę do KSeF korekty, której KSeF jeszcze nie przyjął.</summary>
    Task<DzielnikCorrectionVm> SendCorrectionToKsefAsync(int correctionId, CancellationToken ct = default);

    /// <summary>Faktury (i ich korekty) wystawione w Dzielniku dla podanych sesji płatności.</summary>
    Task<List<DzielnikPaymentInvoiceVm>> GetInvoicesAsync(IReadOnlyCollection<Guid> paymentSessionGuids,
        CancellationToken ct = default);
}

public class DzielnikCorrectionIm
{
    public Guid PaymentSessionGuid { get; set; }

    /// <summary>Pozycje faktury PO korekcie; pusta lista = korekta do zera (zwrot całości).</summary>
    public List<DzielnikApiInvoiceLine> LinesAfterCorrection { get; set; } = new();

    /// <summary>Przyczyna korekty - trafia do KSeF.</summary>
    public string Reason { get; set; } = "";

    /// <summary>Odnośnik hosta, np. identyfikator zwrotu w Stripe - zapisywany i dopisywany do uwag.</summary>
    public string? ExternalReference { get; set; }

    public string? Notes { get; set; }
}

public class DzielnikCorrectionVm
{
    public DzielnikCorrectionVm()
    {
    }

    public DzielnikCorrectionVm(DzielnikCorrectionRecord record)
    {
        Id = record.Id;
        PaymentSessionGuid = record.PaymentSessionGuid;
        CorrectedInvoiceNumber = record.CorrectedInvoiceNumber;
        InvoiceNumber = record.InvoiceNumber;
        GrossTotal = record.GrossTotal;
        Reason = record.Reason;
        ExternalReference = record.ExternalReference;
        KsefNumber = record.KsefNumber;
        KsefError = record.KsefError;
        Created = record.Created;
    }

    public int Id { get; set; }
    public Guid PaymentSessionGuid { get; set; }
    public string CorrectedInvoiceNumber { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public decimal GrossTotal { get; set; }
    public string Reason { get; set; } = "";
    public string? ExternalReference { get; set; }
    public string? KsefNumber { get; set; }
    public string? KsefError { get; set; }
    public DateTime Created { get; set; }
}

public class DzielnikPaymentInvoiceVm
{
    public Guid PaymentSessionGuid { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public decimal GrossTotal { get; set; }
    public string? KsefNumber { get; set; }
    public DateTime Created { get; set; }
    public List<DzielnikCorrectionVm> Corrections { get; set; } = new();
}

/// <typeparam name="TContext">DbContext hosta z DzielnikInvoiceRecord i DzielnikCorrectionRecord
/// (<see cref="Setup.DzielnikFasDbModelBuilder.RegisterDbModels"/>).</typeparam>
public class DzielnikInvoiceCorrectionService<TContext> : IDzielnikInvoiceCorrectionService where TContext : DbContext
{
    private const int MaxReasonLength = 256;

    private readonly TContext _context;
    private readonly IDzielnikIntegrationSettingsService _settingsService;
    private readonly IDzielnikApiClient _dzielnikApiClient;
    private readonly ILogger<DzielnikInvoiceCorrectionService<TContext>> _logger;

    public DzielnikInvoiceCorrectionService(TContext context,
        IDzielnikIntegrationSettingsService settingsService,
        IDzielnikApiClient dzielnikApiClient,
        ILogger<DzielnikInvoiceCorrectionService<TContext>> logger)
    {
        _context = context;
        _settingsService = settingsService;
        _dzielnikApiClient = dzielnikApiClient;
        _logger = logger;
    }

    public async Task<DzielnikCorrectionVm> CreateCorrectionAsync(DzielnikCorrectionIm correctionIm,
        CancellationToken ct = default)
    {
        var settings = RequireSettings();
        var reason = correctionIm.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            throw new FasApiErrorException("Podaj przyczynę korekty - bez niej korekta jest nieważna.", 400);
        if (reason.Length > MaxReasonLength)
            reason = reason[..MaxReasonLength];

        var invoice = await _context.Set<DzielnikInvoiceRecord>()
                          .FirstOrDefaultAsync(x => x.PaymentSessionGuid == correctionIm.PaymentSessionGuid, ct)
                      ?? throw new FasApiErrorException(
                          "Ta płatność nie ma faktury w Dzielniku - nie ma czego korygować.", 400);

        var existing = await _context.Set<DzielnikCorrectionRecord>()
            .FirstOrDefaultAsync(x => x.CorrectedDzielnikInvoiceGuid == invoice.DzielnikInvoiceGuid, ct);
        if (existing != null)
        {
            throw new FasApiErrorException(
                $"Faktura {invoice.InvoiceNumber} ma już korektę {existing.InvoiceNumber}. Dzielnik nie wystawia " +
                "kolejnej korekty z integracji - zrób ją ręcznie w Dzielniku.", 400);
        }

        // Dzielnik koryguje tylko dokumenty przyjęte przez KSeF.
        if (string.IsNullOrWhiteSpace(invoice.KsefNumber))
            await TrySendInvoiceToKsefAsync(settings, invoice, ct);
        if (string.IsNullOrWhiteSpace(invoice.KsefNumber))
        {
            throw new FasApiErrorException(
                $"Faktura {invoice.InvoiceNumber} nie jest jeszcze w KSeF, a Dzielnik koryguje tylko takie. " +
                (settings.SendToKsef
                    ? "Wysyłka do KSeF nie powiodła się - sprawdź fakturę w Dzielniku."
                    : "Włącz \"Wysyłaj od razu do KSeF\" albo wyślij ją z Dzielnika.") +
                " Zwrot w Stripe można zrobić bez korekty i wystawić ją później.", 400);
        }

        var notes = string.Join(". ", new[] { correctionIm.Notes, correctionIm.ExternalReference }
            .Where(x => string.IsNullOrWhiteSpace(x) == false));

        DzielnikApiSalesInvoice correction;
        try
        {
            correction = await _dzielnikApiClient.CreateCorrectionAsync(settings.BaseUrl, settings.ApiKey,
                invoice.DzielnikInvoiceGuid, new DzielnikApiCorrectionRequest
                {
                    Reason = reason,
                    IssueDate = DateTime.Today,
                    Lines = correctionIm.LinesAfterCorrection,
                    Notes = string.IsNullOrEmpty(notes) ? null : notes
                }, ct);
        }
        catch (DzielnikApiException e)
        {
            _logger.LogWarning(e, "Dzielnik odrzucił korektę faktury {Number} (sesja {SessionGuid}).",
                invoice.InvoiceNumber, invoice.PaymentSessionGuid);
            throw new FasApiErrorException($"Dzielnik odrzucił korektę faktury {invoice.InvoiceNumber}: {e.Message}",
                e.StatusCode is >= 400 and < 500 ? 400 : 502);
        }

        var record = new DzielnikCorrectionRecord
        {
            PaymentSessionGuid = invoice.PaymentSessionGuid,
            CorrectedDzielnikInvoiceGuid = invoice.DzielnikInvoiceGuid,
            CorrectedInvoiceNumber = invoice.InvoiceNumber,
            DzielnikInvoiceGuid = correction.Guid,
            InvoiceNumber = correction.Number,
            GrossTotal = correction.GrossTotal,
            Reason = reason,
            ExternalReference = correctionIm.ExternalReference,
            Created = DateTime.Now
        };
        _context.Add(record);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Dzielnik: korekta {Number} ({Gross}) do faktury {Original} (sesja {SessionGuid}).",
            record.InvoiceNumber, record.GrossTotal, invoice.InvoiceNumber, invoice.PaymentSessionGuid);

        if (settings.SendToKsef)
            await SendRecordToKsefAsync(settings, record, ct);

        return new DzielnikCorrectionVm(record);
    }

    public async Task<DzielnikCorrectionVm> SendCorrectionToKsefAsync(int correctionId, CancellationToken ct = default)
    {
        var settings = RequireSettings();
        var record = await _context.Set<DzielnikCorrectionRecord>().FirstOrDefaultAsync(x => x.Id == correctionId, ct)
                     ?? throw new FasApiErrorException("Nie znaleziono korekty.", 404);
        if (string.IsNullOrWhiteSpace(record.KsefNumber))
            await SendRecordToKsefAsync(settings, record, ct);

        return new DzielnikCorrectionVm(record);
    }

    public async Task<List<DzielnikPaymentInvoiceVm>> GetInvoicesAsync(IReadOnlyCollection<Guid> paymentSessionGuids,
        CancellationToken ct = default)
    {
        if (paymentSessionGuids.Count == 0)
            return new List<DzielnikPaymentInvoiceVm>();

        var invoices = await _context.Set<DzielnikInvoiceRecord>().AsNoTracking()
            .Where(x => paymentSessionGuids.Contains(x.PaymentSessionGuid))
            .ToListAsync(ct);
        var corrections = await _context.Set<DzielnikCorrectionRecord>().AsNoTracking()
            .Where(x => paymentSessionGuids.Contains(x.PaymentSessionGuid))
            .ToListAsync(ct);

        return invoices.Select(x => new DzielnikPaymentInvoiceVm
            {
                PaymentSessionGuid = x.PaymentSessionGuid,
                InvoiceNumber = x.InvoiceNumber,
                GrossTotal = x.GrossTotal,
                KsefNumber = x.KsefNumber,
                Created = x.Created,
                Corrections = corrections.Where(c => c.CorrectedDzielnikInvoiceGuid == x.DzielnikInvoiceGuid)
                    .OrderBy(c => c.Created)
                    .Select(c => new DzielnikCorrectionVm(c))
                    .ToList()
            })
            .ToList();
    }

    private DzielnikIntegrationSettings RequireSettings()
    {
        return _settingsService.GetEnabledSettingsOrNull()
               ?? throw new FasApiErrorException(
                   "Integracja z dzielnik.com jest wyłączona - korekty nie da się wystawić. Uzupełnij ustawienia w panelu.",
                   400);
    }

    private async Task TrySendInvoiceToKsefAsync(DzielnikIntegrationSettings settings, DzielnikInvoiceRecord invoice,
        CancellationToken ct)
    {
        if (settings.SendToKsef == false)
            return;

        try
        {
            var sent = await _dzielnikApiClient.SendInvoiceToKsefAsync(settings.BaseUrl, settings.ApiKey,
                invoice.DzielnikInvoiceGuid, ct);
            if (string.IsNullOrWhiteSpace(sent.KsefNumber))
                return;

            invoice.KsefNumber = sent.KsefNumber;
            await _context.SaveChangesAsync(ct);
        }
        catch (DzielnikApiException e)
        {
            _logger.LogWarning(e, "Dosyłka faktury {Number} do KSeF przed korektą nie powiodła się.",
                invoice.InvoiceNumber);
        }
    }

    /// <summary>Wysyła korektę do KSeF; wynik (numer albo przyczynę odmowy) zapisuje w rekordzie, nie rzuca.</summary>
    private async Task SendRecordToKsefAsync(DzielnikIntegrationSettings settings, DzielnikCorrectionRecord record,
        CancellationToken ct)
    {
        try
        {
            var sent = await _dzielnikApiClient.SendInvoiceToKsefAsync(settings.BaseUrl, settings.ApiKey,
                record.DzielnikInvoiceGuid, ct);
            record.KsefNumber = string.IsNullOrWhiteSpace(sent.KsefNumber) ? null : sent.KsefNumber;
            record.KsefError = record.KsefNumber != null
                ? null
                : string.IsNullOrWhiteSpace(sent.KsefError)
                    ? $"KSeF nie przyjął korekty (status {sent.Status})."
                    : sent.KsefError;
        }
        catch (DzielnikApiException e)
        {
            _logger.LogWarning(e, "Wysyłka korekty {Number} do KSeF nie powiodła się.", record.InvoiceNumber);
            record.KsefError = $"Wysyłka do KSeF: {e.Message}";
        }

        await _context.SaveChangesAsync(ct);
    }
}
