using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Microsoft.Extensions.Localization;
using System.Globalization;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsChartTextTests
{
    [Fact]
    public void LookupsUseEnglishEvenWhenRenderExecutionContextHasAnotherCulture()
    {
        var prior = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
            var text = new StatsChartText(new CultureReportingLocalizer());

            Assert.Equal("en:StatsCategory", text.Get("StatsCategory"));
            Assert.Equal("en:StatsVisibleCityCount:1,2", text.Get("StatsVisibleCityCount", 1, 2));
            Assert.Equal("es", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prior;
        }
    }

    [Fact]
    public void DecimalFormattingUsesEnglishAndRestoresBothCallerCultures()
    {
        var priorCulture = CultureInfo.CurrentCulture;
        var priorUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
            var text = new StatsChartText(new CultureReportingLocalizer());

            Assert.Equal("1,609.34", text.Format(1609.344m));
            Assert.Equal("es-ES", CultureInfo.CurrentCulture.Name);
            Assert.Equal("es", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    sealed class CultureReportingLocalizer : IStringLocalizer<RouteFilterResources>
    {
        public LocalizedString this[string name] => new(name, $"{CultureInfo.CurrentUICulture.Name}:{name}");

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, $"{CultureInfo.CurrentUICulture.Name}:{name}:{string.Join(',', arguments)}");

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
