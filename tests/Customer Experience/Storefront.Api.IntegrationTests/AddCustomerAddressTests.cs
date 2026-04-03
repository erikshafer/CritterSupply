using System.Net;
using System.Net.Http.Json;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Integration tests for the AddCustomerAddress BFF endpoint.
/// Verifies the BFF correctly proxies address creation to Customer Identity BC.
/// </summary>
[Collection("Storefront Integration Tests")]
public class AddCustomerAddressTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public AddCustomerAddressTests(TestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ClearAllStubs();
    }

    [Fact]
    public async Task AddAddress_ReturnsCreatedWithAddressId()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var payload = new
        {
            nickname = "Home",
            addressLine1 = "123 Main St",
            addressLine2 = (string?)null,
            city = "Seattle",
            stateOrProvince = "WA",
            postalCode = "98101",
            country = "US"
        };

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(payload).ToUrl($"/api/storefront/customers/{customerId}/addresses");
            scenario.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        // Assert — response contains an addressId
        var response = await result.ReadAsJsonAsync<AddressCreatedResponse>();
        response.ShouldNotBeNull();
        response.AddressId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task AddAddress_WhenCustomerNotFound_Returns404()
    {
        // Arrange — stub will throw 404 for unknown customer
        // (StubCustomerIdentityClient.AddAddressAsync creates a customer implicitly,
        //  but a real Customer Identity BC would return 404)
        // For this test, we configure the stub to throw
        var customerId = Guid.NewGuid();
        _fixture.StubCustomerIdentityClient.ConfigureAddAddressFailure(
            customerId, HttpStatusCode.NotFound);

        var payload = new
        {
            nickname = "Home",
            addressLine1 = "123 Main St",
            city = "Seattle",
            stateOrProvince = "WA",
            postalCode = "98101",
            country = "US"
        };

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(payload).ToUrl($"/api/storefront/customers/{customerId}/addresses");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task AddAddress_WhenNicknameConflict_Returns409()
    {
        // Arrange — configure stub to throw 409 for nickname conflict
        var customerId = Guid.NewGuid();
        _fixture.StubCustomerIdentityClient.ConfigureAddAddressFailure(
            customerId, HttpStatusCode.Conflict);

        var payload = new
        {
            nickname = "Home",
            addressLine1 = "123 Main St",
            city = "Seattle",
            stateOrProvince = "WA",
            postalCode = "98101",
            country = "US"
        };

        // Act
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(payload).ToUrl($"/api/storefront/customers/{customerId}/addresses");
            scenario.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    private sealed record AddressCreatedResponse(Guid AddressId);
}
