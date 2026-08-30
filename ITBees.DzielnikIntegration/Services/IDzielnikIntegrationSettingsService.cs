using ITBees.DzielnikIntegration.Entities;
using ITBees.DzielnikIntegration.Models;

namespace ITBees.DzielnikIntegration.Services;

public interface IDzielnikIntegrationSettingsService
{
    DzielnikIntegrationSettingsVm Get();
    DzielnikIntegrationSettingsVm Update(DzielnikIntegrationSettingsIm im, Guid? modifiedByGuid = null);

    /// <summary>Zapisane ustawienia albo null, gdy integracja jest wyłączona lub brakuje klucza.</summary>
    DzielnikIntegrationSettings? GetEnabledSettingsOrNull();
}
