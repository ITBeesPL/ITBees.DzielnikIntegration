using ITBees.DzielnikIntegration.Entities;
using ITBees.DzielnikIntegration.Models;
using ITBees.Interfaces.Repository;

namespace ITBees.DzielnikIntegration.Services;

/// <summary>
/// Ustawienia integracji z dzielnik.com trzymane w bazie aplikacji hosta (jeden wiersz).
/// Host musi zarejestrować encję przez <see cref="Setup.DbModelBuilder.Register"/> oraz
/// udostępniać generyczne repozytoria ITBees (IReadOnlyRepository/IWriteOnlyRepository).
/// </summary>
public class DzielnikIntegrationSettingsService : IDzielnikIntegrationSettingsService
{
    private const string DefaultBaseUrl = "https://dzielnik.com";

    private readonly IReadOnlyRepository<DzielnikIntegrationSettings> _settingsRoRepo;
    private readonly IWriteOnlyRepository<DzielnikIntegrationSettings> _settingsWoRepo;

    public DzielnikIntegrationSettingsService(
        IReadOnlyRepository<DzielnikIntegrationSettings> settingsRoRepo,
        IWriteOnlyRepository<DzielnikIntegrationSettings> settingsWoRepo)
    {
        _settingsRoRepo = settingsRoRepo;
        _settingsWoRepo = settingsWoRepo;
    }

    public DzielnikIntegrationSettingsVm Get()
    {
        var settings = _settingsRoRepo.GetData(x => true).FirstOrDefault();
        return settings == null ? new DzielnikIntegrationSettingsVm() : new DzielnikIntegrationSettingsVm(settings);
    }

    public DzielnikIntegrationSettingsVm Update(DzielnikIntegrationSettingsIm im, Guid? modifiedByGuid = null)
    {
        var apiKey = im.ApiKey?.Trim() ?? "";
        var baseUrl = NormalizeBaseUrl(im.BaseUrl);
        var existing = _settingsRoRepo.GetData(x => true).FirstOrDefault();

        if (existing == null)
        {
            var inserted = _settingsWoRepo.InsertData(new DzielnikIntegrationSettings()
            {
                Enabled = im.Enabled,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                SendToKsef = im.SendToKsef,
                Modified = DateTime.Now,
                ModifiedByGuid = modifiedByGuid
            });

            return new DzielnikIntegrationSettingsVm(inserted);
        }

        var updated = _settingsWoRepo.UpdateData(x => x.Id == existing.Id, x =>
        {
            x.Enabled = im.Enabled;
            x.ApiKey = apiKey;
            x.BaseUrl = baseUrl;
            x.SendToKsef = im.SendToKsef;
            x.Modified = DateTime.Now;
            x.ModifiedByGuid = modifiedByGuid;
        }).First();

        return new DzielnikIntegrationSettingsVm(updated);
    }

    public DzielnikIntegrationSettings? GetEnabledSettingsOrNull()
    {
        var settings = _settingsRoRepo.GetData(x => true).FirstOrDefault();
        if (settings == null || settings.Enabled == false || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return null;
        }

        settings.BaseUrl = NormalizeBaseUrl(settings.BaseUrl);
        return settings;
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        var url = baseUrl?.Trim().TrimEnd('/') ?? "";
        return string.IsNullOrWhiteSpace(url) ? DefaultBaseUrl : url;
    }
}
