using Marten;
using Shopping.Cart;

namespace Shopping.Api.IntegrationTests.Cart;

/// <summary>
/// Alba-based validation tests for InitializeCart command.
/// Tests edge cases for customer vs anonymous cart initialization.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class InitializeCartValidationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public InitializeCartValidationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task POST_InitializeCart_WithCustomerId_Succeeds()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(customerId, null))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(201);
            x.Header("Location").ShouldHaveValues();
        });

        // Assert
        var response = result.ReadAsJson<CreationResponseDto>();
        response.Value.ShouldNotBe(Guid.Empty);

        using var session = _fixture.GetDocumentSession();
        var cart = await session.LoadAsync<Shopping.Cart.Cart>(response.Value);
        cart!.CustomerId.ShouldBe(customerId);
        cart.SessionId.ShouldBeNull();
    }

    [Fact]
    public async Task POST_InitializeCart_WithSessionId_Succeeds()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(null, sessionId))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(201);
        });

        // Assert
        var response = result.ReadAsJson<CreationResponseDto>();

        using var session = _fixture.GetDocumentSession();
        var cart = await session.LoadAsync<Shopping.Cart.Cart>(response.Value);
        cart!.CustomerId.ShouldBeNull();
        cart.SessionId.ShouldBe(sessionId);
    }

    [Fact]
    public async Task POST_InitializeCart_WithBothCustomerIdAndSessionId_UsesCustomerId()
    {
        // Arrange - Provide both (edge case, shouldn't happen in practice)
        var customerId = Guid.CreateVersion7();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(customerId, sessionId))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(201);
        });

        // Assert - CustomerId should win (authenticated takes precedence)
        var response = result.ReadAsJson<CreationResponseDto>();

        using var session = _fixture.GetDocumentSession();
        var cart = await session.LoadAsync<Shopping.Cart.Cart>(response.Value);
        cart!.CustomerId.ShouldBe(customerId);
        cart.SessionId.ShouldBe(sessionId); // May store both, or just customerId
    }

    [Fact]
    public async Task POST_InitializeCart_WithNeitherCustomerIdNorSessionId_Returns400()
    {
        // Act & Assert - Neither customerId nor sessionId provided
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(null, null))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(400);
            x.ContentShouldContain("Either CustomerId or SessionId must be provided");
        });
    }

    [Fact]
    public async Task POST_InitializeCart_WithEmptySessionId_Returns400()
    {
        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(null, ""))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task POST_InitializeCart_WithWhitespaceSessionId_Returns400()
    {
        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(null, "   "))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task POST_InitializeCart_MultipleTimesForSameCustomer_CreatesMultipleCarts()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();

        // Act - Initialize cart twice for same customer
        var result1 = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(customerId, null))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(201);
        });

        var result2 = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new InitializeCart(customerId, null))
                .ToUrl("/api/carts");
            x.StatusCodeShouldBe(201);
        });

        // Assert - Should create two separate carts
        var cart1Id = result1.ReadAsJson<CreationResponseDto>().Value;
        var cart2Id = result2.ReadAsJson<CreationResponseDto>().Value;

        cart1Id.ShouldNotBe(cart2Id);

        using var session = _fixture.GetDocumentSession();
        var carts = await session.Query<Shopping.Cart.Cart>()
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();

        carts.Count.ShouldBe(2);
    }

    // DTO for deserializing CreationResponse<Guid>
    private record CreationResponseDto(Guid Value, string Url);
}
