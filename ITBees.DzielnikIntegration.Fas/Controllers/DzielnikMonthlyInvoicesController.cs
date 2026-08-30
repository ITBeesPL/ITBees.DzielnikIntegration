using ITBees.DzielnikIntegration.Fas.Models;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.DzielnikIntegration.Fas.Controllers;

/// <summary>
/// Tworzy w dzielnik.com faktury za wszystkie opłacone płatności wystawione od pierwszego dnia
/// bieżącego miesiąca, a przy włączonym "Wysyłaj od razu do KSeF" od razu wysyła je do KSeF.
/// Idempotentne - płatności z już utworzoną fakturą są pomijane, a faktury bez numeru KSeF dosyłane.
/// </summary>
[Authorize(Roles = "PlatformOperator")]
public class DzielnikMonthlyInvoicesController : RestfulControllerBase<DzielnikMonthlyInvoicesController>
{
    private readonly IDzielnikPaymentInvoiceService _dzielnikPaymentInvoiceService;

    public DzielnikMonthlyInvoicesController(ILogger<DzielnikMonthlyInvoicesController> logger,
        IDzielnikPaymentInvoiceService dzielnikPaymentInvoiceService) : base(logger)
    {
        _dzielnikPaymentInvoiceService = dzielnikPaymentInvoiceService;
    }

    [HttpPost]
    [Produces<DzielnikMonthlyInvoicesVm>]
    public Task<IActionResult> Post()
    {
        return ReturnOkResultAsync(async () =>
            (object)await _dzielnikPaymentInvoiceService.CreateInvoicesForCurrentMonthAsync(
                HttpContext.RequestAborted));
    }
}
