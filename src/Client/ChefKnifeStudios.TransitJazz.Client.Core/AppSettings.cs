using ChefKnifeStudios.TransitJazz.Shared.Enums;

namespace ChefKnifeStudios.TransitJazz.Client.Core;

public class AppSettings
{
    public List<WebAPI> ExternalApis { get; set; } = new();
    public Dictionary<FeatureFlags, bool> FeatureFlags { get; set; } = new();

    public class WebAPI
    {
        public string Name { get; set; } = string.Empty;
        public string BaseUri { get; set; } = string.Empty;
        public bool AuthenticationRequired { get; set; }
        public bool AddHttpClient { get; set; }
    }
}
