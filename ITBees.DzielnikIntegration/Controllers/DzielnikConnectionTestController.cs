using ITBees.DzielnikIntegration.Models;
using ITBees.DzielnikIntegration.Services;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.DzielnikIntegration.Controllers;

/// <summary>
/// Test połączenia z publicznym API Dzielnika (GET /api/v1/status): czy klucz jest przyjmowany
/// i w jakim zakresie można korzystać z API (wystawianie faktur, odczyt historii itd.).
/// </summary>
[Authorize(Roles = "PlatformOperator")]
public class DzielnikConnectionTestController : RestfulControllerBase<DzielnikConnectionTestController>
{
    private readonly IDzielnikConnectionTestService _dzielnikConnectionTestService;

    public DzielnikConnectionTestController(ILogger<DzielnikConnectionTestController> logger,
        IDzielnikConnectionTestService dzielnikConnectionTestService) : base(logger)
    {
        _dzielnikConnectionTestService = dzielnikConnectionTestService;
    }

    [HttpPost]
    [Produces<DzielnikConnectionTestVm>]
    public Task<IActionResult> Post([FromBody] DzielnikConnectionTestIm dzielnikConnectionTestIm)
    {
        return ReturnOkResultAsync(async () =>
            (object)await _dzielnikConnectionTestService.TestAsync(dzielnikConnectionTestIm,
                HttpContext.RequestAborted));
    }
}
