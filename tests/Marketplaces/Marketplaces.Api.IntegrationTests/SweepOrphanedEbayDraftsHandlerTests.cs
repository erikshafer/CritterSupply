using Marketplaces.Adapters;
using Marketplaces.Api.Listings;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="SweepOrphanedEbayDraftsHandler"/> — the background sweep
/// that finishes the eBay orphaned-draft lifecycle (Tier 1 C of
/// <c>docs/research/state-of-repo-2026-05.md</c>).
/// <para>
/// Uses a real <see cref="IDocumentSession"/> from <see cref="TestFixture"/>
/// (Marten + TestContainers Postgres), a fake adapter dictionary, and the
/// recording <see cref="RecordingMessageBus"/> double for verifying reschedule.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SweepOrphanedEbayDraftsHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public SweepOrphanedEbayDraftsHandlerTests(TestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        // Each test starts from a clean orphan table — we don't need seed data here.
        await using var session = _fixture.GetDocumentSession();
        session.DeleteWhere<OrphanedEbayDraft>(_ => true);
        await session.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sweep_DeletesOrphan_AndMarksCleaned_WhenAdapterSucceeds()
    {
        // Arrange — one pending orphan
        var orphan = NewOrphan("ebay-OFFER-CLEAN-001");
        await StoreAsync(orphan);

        var adapter = new RecordingEbayAdapter(deletedReturns: true);
        var bus = new SchedulingRecordingBus();

        // Act
        await using var session = _fixture.GetDocumentSession();
        await SweepOrphanedEbayDraftsHandler.Handle(
            new SweepOrphanedEbayDrafts(BatchSize: 25),
            session,
            BuildAdapters(adapter),
            bus,
            NullLogger<SweepOrphanedEbayDrafts>.Instance,
            CancellationToken.None);

        // Assert — adapter called once with the orphan id
        adapter.DeletedIds.ShouldBe(["ebay-OFFER-CLEAN-001"]);

        // Document marked cleaned
        await using var verify = _fixture.GetDocumentSession();
        var stored = await verify.LoadAsync<OrphanedEbayDraft>("ebay-OFFER-CLEAN-001");
        stored.ShouldNotBeNull();
        stored!.IsCleaned.ShouldBeTrue();
        stored.CleanedAt.ShouldNotBeNull();
        stored.CleanupAttempts.ShouldBe(1);
        stored.LastFailureReason.ShouldBeNull();

        // Sweep reschedules itself for the next pass
        bus.ScheduledSweeps.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_RecordsFailure_AndKeepsOrphanPending_WhenAdapterReturnsFalse()
    {
        // Arrange
        var orphan = NewOrphan("ebay-OFFER-FAIL-001");
        await StoreAsync(orphan);

        var adapter = new RecordingEbayAdapter(deletedReturns: false);
        var bus = new SchedulingRecordingBus();

        // Act
        await using var session = _fixture.GetDocumentSession();
        await SweepOrphanedEbayDraftsHandler.Handle(
            new SweepOrphanedEbayDrafts(),
            session,
            BuildAdapters(adapter),
            bus,
            NullLogger<SweepOrphanedEbayDrafts>.Instance,
            CancellationToken.None);

        // Assert — orphan still pending, attempts incremented, failure reason set
        await using var verify = _fixture.GetDocumentSession();
        var stored = await verify.LoadAsync<OrphanedEbayDraft>("ebay-OFFER-FAIL-001");
        stored.ShouldNotBeNull();
        stored!.IsCleaned.ShouldBeFalse();
        stored.CleanedAt.ShouldBeNull();
        stored.CleanupAttempts.ShouldBe(1);
        stored.LastFailureReason.ShouldNotBeNullOrEmpty();
        stored.LastCleanupAttemptAt.ShouldNotBeNull();

        // Reschedule still happens — the sweep keeps running
        bus.ScheduledSweeps.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_SkipsAlreadyCleanedOrphans()
    {
        // Arrange — one cleaned, one pending
        var cleaned = NewOrphan("ebay-OFFER-DONE");
        cleaned.IsCleaned = true;
        cleaned.CleanedAt = DateTimeOffset.UtcNow.AddHours(-1);
        await StoreAsync(cleaned);

        var pending = NewOrphan("ebay-OFFER-TODO");
        await StoreAsync(pending);

        var adapter = new RecordingEbayAdapter(deletedReturns: true);
        var bus = new SchedulingRecordingBus();

        // Act
        await using var session = _fixture.GetDocumentSession();
        await SweepOrphanedEbayDraftsHandler.Handle(
            new SweepOrphanedEbayDrafts(),
            session,
            BuildAdapters(adapter),
            bus,
            NullLogger<SweepOrphanedEbayDrafts>.Instance,
            CancellationToken.None);

        // Assert — only the pending one was acted on
        adapter.DeletedIds.ShouldBe(["ebay-OFFER-TODO"]);
    }

    [Fact]
    public async Task Sweep_RespectsBatchSize_ProcessingOldestOrphansFirst()
    {
        // Arrange — three pending orphans with distinct DetectedAt
        var oldest = NewOrphan("ebay-OFFER-OLDEST", detectedAt: DateTimeOffset.UtcNow.AddHours(-3));
        var middle = NewOrphan("ebay-OFFER-MIDDLE", detectedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newest = NewOrphan("ebay-OFFER-NEWEST", detectedAt: DateTimeOffset.UtcNow.AddHours(-1));
        await StoreAsync(oldest, middle, newest);

        var adapter = new RecordingEbayAdapter(deletedReturns: true);
        var bus = new SchedulingRecordingBus();

        // Act — batch size of 2 should process the two oldest
        await using var session = _fixture.GetDocumentSession();
        await SweepOrphanedEbayDraftsHandler.Handle(
            new SweepOrphanedEbayDrafts(BatchSize: 2),
            session,
            BuildAdapters(adapter),
            bus,
            NullLogger<SweepOrphanedEbayDrafts>.Instance,
            CancellationToken.None);

        // Assert
        adapter.DeletedIds.Count.ShouldBe(2);
        adapter.DeletedIds.ShouldContain("ebay-OFFER-OLDEST");
        adapter.DeletedIds.ShouldContain("ebay-OFFER-MIDDLE");
        adapter.DeletedIds.ShouldNotContain("ebay-OFFER-NEWEST");
    }

    [Fact]
    public async Task Sweep_StillReschedules_WhenNoOrphansPending()
    {
        // Arrange — empty orphan table (InitializeAsync cleaned it)
        var adapter = new RecordingEbayAdapter(deletedReturns: true);
        var bus = new SchedulingRecordingBus();

        // Act
        await using var session = _fixture.GetDocumentSession();
        await SweepOrphanedEbayDraftsHandler.Handle(
            new SweepOrphanedEbayDrafts(),
            session,
            BuildAdapters(adapter),
            bus,
            NullLogger<SweepOrphanedEbayDrafts>.Instance,
            CancellationToken.None);

        // Assert — adapter never called, but next sweep is still queued
        adapter.DeletedIds.ShouldBeEmpty();
        bus.ScheduledSweeps.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_RecordsAdapterException_AndContinuesWithRemainingOrphans()
    {
        // Arrange — first orphan throws; second succeeds.
        var thrower = NewOrphan("ebay-OFFER-THROW", detectedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var ok = NewOrphan("ebay-OFFER-OK", detectedAt: DateTimeOffset.UtcNow.AddHours(-1));
        await StoreAsync(thrower, ok);

        var adapter = new RecordingEbayAdapter(deletedReturns: true)
        {
            ThrowOnId = "ebay-OFFER-THROW"
        };
        var bus = new SchedulingRecordingBus();

        // Act
        await using var session = _fixture.GetDocumentSession();
        await SweepOrphanedEbayDraftsHandler.Handle(
            new SweepOrphanedEbayDrafts(),
            session,
            BuildAdapters(adapter),
            bus,
            NullLogger<SweepOrphanedEbayDrafts>.Instance,
            CancellationToken.None);

        // Assert
        await using var verify = _fixture.GetDocumentSession();
        var threw = await verify.LoadAsync<OrphanedEbayDraft>("ebay-OFFER-THROW");
        threw!.IsCleaned.ShouldBeFalse();
        threw.CleanupAttempts.ShouldBe(1);
        threw.LastFailureReason.ShouldNotBeNullOrEmpty();
        threw.LastFailureReason.ShouldContain("Adapter exception");

        var success = await verify.LoadAsync<OrphanedEbayDraft>("ebay-OFFER-OK");
        success!.IsCleaned.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static OrphanedEbayDraft NewOrphan(string id, DateTimeOffset? detectedAt = null) =>
        new()
        {
            Id = id,
            ListingId = Guid.NewGuid(),
            Sku = "SKU-" + id,
            ChannelCode = "EBAY_US",
            DetectedAt = detectedAt ?? DateTimeOffset.UtcNow,
            CleanupAttempts = 0,
            IsCleaned = false
        };

    private async Task StoreAsync(params OrphanedEbayDraft[] orphans)
    {
        await using var session = _fixture.GetDocumentSession();
        foreach (var o in orphans) session.Store(o);
        await session.SaveChangesAsync();
    }

    private static IReadOnlyDictionary<string, IMarketplaceAdapter> BuildAdapters(
        IMarketplaceAdapter adapter) =>
        new Dictionary<string, IMarketplaceAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            [adapter.ChannelCode] = adapter
        };

    private sealed class RecordingEbayAdapter(bool deletedReturns) : IMarketplaceAdapter
    {
        public string ChannelCode => "EBAY_US";
        public List<string> DeletedIds { get; } = [];
        public string? ThrowOnId { get; set; }

        public Task<SubmissionResult> SubmitListingAsync(ListingSubmission submission, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubmissionStatus> CheckSubmissionStatusAsync(string externalSubmissionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> DeactivateListingAsync(string externalListingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteOrphanedDraftAsync(string externalSubmissionId, CancellationToken ct = default)
        {
            if (ThrowOnId is not null && string.Equals(ThrowOnId, externalSubmissionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Simulated adapter failure");

            DeletedIds.Add(externalSubmissionId);
            return Task.FromResult(deletedReturns);
        }
    }

    /// <summary>
    /// Minimal IMessageBus double that captures <c>ScheduleAsync</c> calls for
    /// <see cref="SweepOrphanedEbayDrafts"/> so tests can assert reschedule behaviour.
    /// </summary>
    private sealed class SchedulingRecordingBus : IMessageBus
    {
        private readonly List<(object Message, DeliveryOptions? Options)> _sent = [];

        public IReadOnlyList<SweepOrphanedEbayDrafts> ScheduledSweeps =>
            _sent.Where(x => x.Options?.ScheduleDelay.HasValue == true)
                 .Select(x => x.Message)
                 .OfType<SweepOrphanedEbayDrafts>()
                 .ToList();

        public string? TenantId { get; set; }

        ValueTask IMessageBus.SendAsync<T>(T message, DeliveryOptions? options)
        {
            _sent.Add((message, options)!);
            return ValueTask.CompletedTask;
        }

        ValueTask IMessageBus.PublishAsync<T>(T message, DeliveryOptions? options)
        {
            _sent.Add((message, options)!);
            return ValueTask.CompletedTask;
        }

        ValueTask IMessageBus.BroadcastToTopicAsync(string topicName, object message, DeliveryOptions options) => ValueTask.CompletedTask;
        Task ICommandBus.InvokeAsync(object message, CancellationToken cancellation, TimeSpan? timeout) => Task.CompletedTask;
        Task ICommandBus.InvokeAsync(object message, DeliveryOptions options, CancellationToken cancellation, TimeSpan? timeout) => Task.CompletedTask;
        Task<T> ICommandBus.InvokeAsync<T>(object message, CancellationToken cancellation, TimeSpan? timeout) => Task.FromResult<T>(default!);
        Task<T> ICommandBus.InvokeAsync<T>(object message, DeliveryOptions options, CancellationToken cancellation, TimeSpan? timeout) => Task.FromResult<T>(default!);
        Task IMessageBus.InvokeForTenantAsync(string tenantId, object message, CancellationToken cancellation, TimeSpan? timeout) => Task.CompletedTask;
        Task<T> IMessageBus.InvokeForTenantAsync<T>(string tenantId, object message, CancellationToken cancellation, TimeSpan? timeout) => Task.FromResult<T>(default!);
        IDestinationEndpoint IMessageBus.EndpointFor(string endpointName) => throw new NotImplementedException();
        IDestinationEndpoint IMessageBus.EndpointFor(Uri uri) => throw new NotImplementedException();
        IReadOnlyList<Envelope> IMessageBus.PreviewSubscriptions(object message) => [];
        IReadOnlyList<Envelope> IMessageBus.PreviewSubscriptions(object message, DeliveryOptions options) => [];
    }
}
