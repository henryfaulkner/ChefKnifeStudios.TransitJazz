using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests.ClientExperience;

public sealed class ClientExperienceCatalogTests
{
    [Fact]
    public void WireCatalogCoversEveryNotificationIssueCode()
    {
        var current = Enum.GetNames<NotificationIssueCode>();

        Assert.Equal(26, current.Length);
        Assert.True(ClientExperienceCatalog.NotificationIssues.SetEquals(current));
    }
}
