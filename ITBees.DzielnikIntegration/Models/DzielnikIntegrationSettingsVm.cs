using ITBees.DzielnikIntegration.Entities;

namespace ITBees.DzielnikIntegration.Models;

public class DzielnikIntegrationSettingsVm
{
    public DzielnikIntegrationSettingsVm()
    {
    }

    public DzielnikIntegrationSettingsVm(DzielnikIntegrationSettings x)
    {
        Enabled = x.Enabled;
        ApiKey = x.ApiKey;
        BaseUrl = x.BaseUrl;
        SendToKsef = x.SendToKsef;
        Modified = x.Modified;
    }

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://dzielnik.com";
    public bool SendToKsef { get; set; }
    public DateTime? Modified { get; set; }
}

public class DzielnikIntegrationSettingsIm
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public bool SendToKsef { get; set; }
}
