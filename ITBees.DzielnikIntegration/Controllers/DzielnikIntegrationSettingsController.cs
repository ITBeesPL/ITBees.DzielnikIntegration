using System.Security.Claims;
using ITBees.DzielnikIntegration.Models;
using ITBees.DzielnikIntegration.Services;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.DzielnikIntegration.Controllers;

/// <summary>
/// Endpoint ustawień integracji z dzielnik.com dla paneli administracyjnych ITBees.
/// Wykrywany automatycznie przez ASP.NET po dodaniu referencji do biblioteki;
/// wymagana rejestracja: DzielnikIntegrationSetup.Register(services) + DbModelBuilder.Register(modelBuilder).
/// </summary>
[Authorize(Roles = "PlatformOperator")]
public class DzielnikIntegrationSettingsController : RestfulControllerBase<DzielnikIntegrationSettingsController>
{
    private readonly IDzielnikIntegrationSettingsService _dzielnikIntegrationSettingsService;

    public DzielnikIntegrationSettingsController(ILogger<DzielnikIntegrationSettingsController> logger,
        IDzielnikIntegrationSettingsService dzielnikIntegrationSettingsService) : base(logger)
    {
        _dzielnikIntegrationSettingsService = dzielnikIntegrationSettingsService;
    }

    [HttpGet]
    [Produces<DzielnikIntegrationSettingsVm>]
    public IActionResult Get()
    {
        return ReturnOkResult(() => _dzielnikIntegrationSettingsService.Get());
    }

    [HttpPut]
    [Produces<DzielnikIntegrationSettingsVm>]
    public IActionResult Put([FromBody] DzielnikIntegrationSettingsIm dzielnikIntegrationSettingsIm)
    {
        return ReturnOkResult(() =>
            _dzielnikIntegrationSettingsService.Update(dzielnikIntegrationSettingsIm, GetCurrentUserGuidOrNull()));
    }

    private Guid? GetCurrentUserGuidOrNull()
    {
        var id = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var guid) ? guid : null;
    }
}
