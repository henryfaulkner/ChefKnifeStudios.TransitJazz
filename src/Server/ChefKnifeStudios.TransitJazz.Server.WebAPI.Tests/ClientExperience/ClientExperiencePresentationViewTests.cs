using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperiencePresentationViewTests
{
    [ClientExperiencePostgresFact]
    public async Task AcceptedCombinedPresentationIsOneRowAndLogicalDuplicatesAreQuarantined()
    {
        await using var fixture = await ClientExperienceDatabaseFixture.TryCreateAsync()
            ?? throw new InvalidOperationException("The PostgreSQL fact requires a disposable connection.");
        await using var context = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var pageSessionId = Guid.NewGuid();
        var activationId = Guid.NewGuid();
        var validPresentationId = Guid.NewGuid();
        var duplicatePresentationId = Guid.NewGuid();
        context.ClientExperienceEvents.AddRange(
            Event(Guid.NewGuid(), pageSessionId, activationId, validPresentationId, now,
                ["RouteLoadFailed", "MapUnavailable"]),
            Event(Guid.NewGuid(), pageSessionId, activationId, duplicatePresentationId, now,
                ["AudioUnavailable"]),
            Event(Guid.NewGuid(), pageSessionId, activationId, duplicatePresentationId, now,
                ["AudioUnavailable"]));
        await context.SaveChangesAsync();
        await context.Database.OpenConnectionAsync();

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var acceptedCommand = new NpgsqlCommand(
            "SELECT presentation_id, jsonb_array_length(issue_codes) FROM client_experience_presentations WHERE presentation_id = @id", connection);
        acceptedCommand.Parameters.AddWithValue("id", validPresentationId);
        await using var acceptedReader = await acceptedCommand.ExecuteReaderAsync();
        Assert.True(await acceptedReader.ReadAsync());
        Assert.Equal(validPresentationId, acceptedReader.GetGuid(0));
        Assert.Equal(2, acceptedReader.GetInt32(1));
        Assert.False(await acceptedReader.ReadAsync());
        await acceptedReader.DisposeAsync();

        await using var excludedCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM client_experience_presentations WHERE presentation_id = @id", connection);
        excludedCommand.Parameters.AddWithValue("id", duplicatePresentationId);
        Assert.Equal(0L, (long)(await excludedCommand.ExecuteScalarAsync())!);

        await using var qualityCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM client_experience_presentation_quality WHERE presentation_id = @id AND quality_code = 'logical_presentation_conflict'", connection);
        qualityCommand.Parameters.AddWithValue("id", duplicatePresentationId);
        Assert.Equal(2L, (long)(await qualityCommand.ExecuteScalarAsync())!);
    }

    static ClientExperienceEvent Event(Guid eventId, Guid pageSessionId, Guid activationId, Guid presentationId,
        DateTimeOffset now, string[] issues)
    {
        var payload = JsonSerializer.Serialize(new
        {
            presentationId,
            severity = "error",
            summaryKind = "multiple_capabilities",
            capabilitiesPresented = new[] { "ToastCapabilityRoutes", "ToastCapabilityMap" },
            issueCodes = issues,
            explainedIssueCodes = Array.Empty<string>(),
            resourceKeys = new[] { "ToastSeverityError", "ToastMultipleCapabilitiesFailure" },
            linkedEpisodeIds = Array.Empty<Guid>()
        });
        return new ClientExperienceEvent
        {
            EventId = eventId,
            PageSessionId = pageSessionId,
            CityActivationId = activationId,
            Sequence = 1,
            OccurredAtUtc = now,
            ReceivedAtUtc = now,
            ClockSkewed = false,
            Environment = "test",
            CitySlug = "atlanta",
            ClientRelease = "test",
            SchemaVersion = 1,
            MeasurementVersion = 1,
            EventKind = "toast_presented",
            PageElapsedMs = 100,
            Payload = payload
        };
    }
}
