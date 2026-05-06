using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace VendorPortal.Api.IntegrationTests;

/// <summary>
/// Regression guard tests for the M44.0 hardening of <see cref="TestFixture"/>.
///
/// These tests pin the invariants that <see cref="TestFixture.CleanAllDataAsync"/>
/// and <see cref="TestFixture.WaitForNonStaleProjectionDataAsync"/> were added to
/// uphold:
///
/// 1. After <see cref="TestFixture.CleanAllDataAsync"/>, both document storage
///    AND event-store storage must be empty (otherwise the async-projection
///    daemon's highwater mark would race ahead of the next test's events and
///    silently produce stale reads — see M44.0 Session 1 retrospective).
///
/// 2. Schema migration must complete during <see cref="TestFixture.InitializeAsync"/>
///    so that the very first test in a cold-container run sees a fully-migrated
///    Marten schema (mscore tables, event-store tables, advisory-lock tables).
///
/// If either invariant regresses (for example, someone adds an async projection
/// to Vendor Portal and switches tests to write events without using
/// <see cref="TestFixture.CleanAllDataAsync"/>), these tests fail fast with a
/// clear message that points back at the retrospective.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TestFixtureGuardTests(TestFixture fixture)
{
    private readonly TestFixture _fixture = fixture;

    [Fact]
    public async Task CleanAllDataAsync_RemovesEventStoreData_NotJustDocuments()
    {
        // Arrange — append an event so the event-store has something to clean up.
        var streamId = Guid.NewGuid();
        await using (var session = _fixture.GetDocumentSession())
        {
            session.Events.StartStream(streamId, new GuardProbeEvent(streamId, "before-clean"));
            await session.SaveChangesAsync();
        }

        // Act
        await _fixture.CleanAllDataAsync();

        // Assert — the event-store must be empty. If CleanAllDataAsync regresses to
        // documents-only cleanup, the event remains and async daemons running in any
        // future Vendor Portal projection will see stale highwater marks.
        await using var verifySession = _fixture.GetDocumentSession();
        var events = await verifySession.Events.QueryAllRawEvents().ToListAsync();
        events.ShouldBeEmpty(
            "CleanAllDataAsync must clear event-store data, otherwise the next test's " +
            "async-projection daemon highwater drifts (M44.0 Session 1).");
    }

    [Fact]
    public async Task SchemaMigration_HasCompleted_BeforeAnyTestRuns()
    {
        // The fixture's InitializeAsync calls ApplyAllConfiguredChangesToDatabaseAsync.
        // We verify that by exercising the event-store path — appending an event
        // requires the mt_streams / mt_events tables to exist. If migration didn't
        // complete (or completed after this test's test-host scope), this throws.
        var streamId = Guid.NewGuid();
        await using var session = _fixture.GetDocumentSession();

        Should.NotThrow(() =>
        {
            session.Events.StartStream(streamId, new GuardProbeEvent(streamId, "schema-check"));
            return session.SaveChangesAsync();
        });

        // Cleanup so the test is hermetic.
        await _fixture.CleanAllDataAsync();
    }

    /// <summary>
    /// Probe event used solely by the guard suite. Lives outside any production
    /// stream and is never registered in any projection — its only purpose is to
    /// give the test something to write so it can verify cleanup behaviour.
    /// </summary>
    public sealed record GuardProbeEvent(Guid StreamId, string Marker);
}
