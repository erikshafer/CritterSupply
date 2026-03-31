using Messages.Contracts.Marketplaces;
using Wolverine.Tracking;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Integration tests verifying that RegisterMarketplace and DeactivateMarketplace
/// handlers publish the correct integration messages via OutgoingMessages.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MarketplaceMessagePublishingTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public MarketplaceMessagePublishingTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
    }

    // -------------------------------------------------------------------------
    // RegisterMarketplace → MarketplaceRegistered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegisterMarketplace_NewChannel_PublishesMarketplaceRegistered()
    {
        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new
            {
                ChannelCode = "PUBLISH_REG",
                DisplayName = "Publish Registration Test"
            }).ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        tracked.Sent.SingleMessage<MarketplaceRegistered>().ShouldNotBeNull();
        var msg = tracked.Sent.SingleMessage<MarketplaceRegistered>();
        msg.ChannelCode.ShouldBe("PUBLISH_REG");
        msg.DisplayName.ShouldBe("Publish Registration Test");
    }

    [Fact]
    public async Task RegisterMarketplace_DuplicateChannel_DoesNotPublishMarketplaceRegistered()
    {
        // First registration — should publish
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "PUBLISH_DUP", DisplayName = "First" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        // Second registration (idempotent) — should NOT publish
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new { ChannelCode = "PUBLISH_DUP", DisplayName = "Second" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBeOk();
        });

        tracked.Sent.MessagesOf<MarketplaceRegistered>().ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // DeactivateMarketplace → MarketplaceDeactivated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateMarketplace_ActiveChannel_PublishesMarketplaceDeactivated()
    {
        // Create marketplace
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "PUBLISH_DEACT", DisplayName = "Deactivate Publish Test" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        // Deactivate — should publish
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new { }).ToUrl("/api/marketplaces/PUBLISH_DEACT/deactivate");
            s.StatusCodeShouldBeOk();
        });

        tracked.Sent.SingleMessage<MarketplaceDeactivated>().ShouldNotBeNull();
        var msg = tracked.Sent.SingleMessage<MarketplaceDeactivated>();
        msg.ChannelCode.ShouldBe("PUBLISH_DEACT");
    }

    [Fact]
    public async Task DeactivateMarketplace_AlreadyInactive_DoesNotPublishMarketplaceDeactivated()
    {
        // Create and deactivate
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { ChannelCode = "PUBLISH_IDEMP", DisplayName = "Idempotent Deactivate Publish" })
                .ToUrl("/api/marketplaces");
            s.StatusCodeShouldBe(201);
        });

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { }).ToUrl("/api/marketplaces/PUBLISH_IDEMP/deactivate");
            s.StatusCodeShouldBeOk();
        });

        // Second deactivation — should NOT publish
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new { }).ToUrl("/api/marketplaces/PUBLISH_IDEMP/deactivate");
            s.StatusCodeShouldBeOk();
        });

        tracked.Sent.MessagesOf<MarketplaceDeactivated>().ShouldBeEmpty();
    }
}
