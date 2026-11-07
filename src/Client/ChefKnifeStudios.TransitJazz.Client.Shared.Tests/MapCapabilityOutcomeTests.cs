using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class MapCapabilityOutcomeTests
{
    [Fact]
    public void ExplicitStyleFailureNeedsVerifiedPreviousSourcesAndLayers()
    {
        var clock = new NotificationTestTimeProvider();
        var state = ReadyMap(clock, out var instance);
        var operation = state.BeginStyleChange(instance);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Null(state.ActiveRenderIssue);

        var restored = state.CompleteStyleChange(instance, operation, false, true, true);
        Assert.Equal(NotificationIssueCode.MapStyleChangeFailed, restored?.IssueCode);
    }

    [Fact]
    public void FailedStyleWithUnusableRestoreIsMapRootFailure()
    {
        var state = ReadyMap(new NotificationTestTimeProvider(), out var instance);
        var operation = state.BeginStyleChange(instance);

        var outcome = state.CompleteStyleChange(instance, operation, false, false, false, terminalFailure: true);

        Assert.Equal(NotificationIssueCode.MapUnavailable, outcome?.IssueCode);
        Assert.NotEqual(NotificationIssueCode.MapStyleChangeFailed, outcome?.IssueCode);
    }

    [Fact]
    public void M5NeedsTerminalEvidenceOrTwoFailedValidBatchesTenVisibleSecondsApart()
    {
        var clock = new NotificationTestTimeProvider();
        var state = ReadyMap(clock, out var instance);

        Assert.Null(state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: false));
        clock.Advance(TimeSpan.FromSeconds(9.999));
        Assert.Null(state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: false));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var issue = state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: false);

        Assert.Equal(NotificationIssueCode.MapTransitDisplayFailed, issue?.IssueCode);
        Assert.Equal(NotificationSeverity.Error, NotificationPolicy.GetSeverity(issue!.IssueCode!.Value));
    }

    [Fact]
    public void InvalidOptionalAndHiddenOnlyFailuresDoNotQualifyM5()
    {
        var clock = new NotificationTestTimeProvider();
        var state = ReadyMap(clock, out var instance);

        Assert.Null(state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: false, terminal: false));
        Assert.Null(state.ObserveRenderFailure(instance, MapRenderLayerKind.OptionalResource, validBatch: true, terminal: true));
        Assert.Null(state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: false));
        Assert.True(state.SetVisible(instance, false));
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(state.SetVisible(instance, true));
        Assert.Null(state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: false));
        Assert.Null(state.ActiveRenderIssue);
    }

    [Theory]
    [InlineData(MapRenderLayerKind.PrimaryTransit, NotificationIssueCode.MapTransitDisplayFailed, NotificationSeverity.Error)]
    [InlineData(MapRenderLayerKind.SecondaryLayer, NotificationIssueCode.MapLayerUnavailable, NotificationSeverity.Warning)]
    public void TerminalMapEvidenceHasSeverityForActualAffectedLayer(
        MapRenderLayerKind layer,
        NotificationIssueCode expected,
        NotificationSeverity severity)
    {
        var state = ReadyMap(new NotificationTestTimeProvider(), out var instance);

        var outcome = state.ObserveRenderFailure(instance, layer, validBatch: true, terminal: true);

        Assert.Equal(expected, outcome?.IssueCode);
        Assert.Equal(severity, NotificationPolicy.GetSeverity(expected));
    }

    [Fact]
    public void RecoveryRequiresVerifiedRenderingOfAffectedLayerAndResolvesSilently()
    {
        var state = ReadyMap(new NotificationTestTimeProvider(), out var instance);
        state.ObserveRenderFailure(instance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: true);

        Assert.Null(state.ObserveSuccessfulRender(instance, primaryLayerUsable: false, secondaryLayerUsable: true));
        var recovery = state.ObserveSuccessfulRender(instance, primaryLayerUsable: true, secondaryLayerUsable: false);

        Assert.Equal(NotificationIssueCode.MapTransitDisplayFailed, recovery?.ResolvedIssueCode);
        Assert.Null(recovery?.IssueCode);
        Assert.Null(state.ActiveRenderIssue);
    }

    [Fact]
    public void StaleMapGenerationCannotReportOrResolveOutcome()
    {
        var state = ReadyMap(new NotificationTestTimeProvider(), out var oldInstance);
        var currentInstance = state.BeginInstance();
        Assert.True(state.MarkReady(currentInstance));

        Assert.Null(state.ObserveRenderFailure(oldInstance, MapRenderLayerKind.PrimaryTransit, validBatch: true, terminal: true));
        Assert.Null(state.ObserveSuccessfulRender(oldInstance, primaryLayerUsable: true, secondaryLayerUsable: true));
        Assert.Null(state.ActiveRenderIssue);
    }

    static MapCapabilityOutcomeState ReadyMap(NotificationTestTimeProvider clock, out long generation)
    {
        var state = new MapCapabilityOutcomeState(clock);
        generation = state.BeginInstance();
        Assert.True(state.MarkReady(generation));
        return state;
    }
}
