using Shouldly;
using VendorIdentity.TenantManagement;

namespace VendorIdentity.Api.IntegrationTests;

public sealed class TenantManagementTests : IClassFixture<VendorIdentityApiFixture>, IAsyncLifetime
{
    private readonly VendorIdentityApiFixture _fixture;

    public TenantManagementTests(VendorIdentityApiFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDataAsync();
    }

    [Fact]
    public async Task CreateVendorTenant_WithValidData_Returns201AndCreatesRecord()
    {
        // Arrange
        var command = new CreateVendorTenant(
            OrganizationName: "Acme Pet Supplies",
            ContactEmail: "contact@acmepetsupplies.com"
        );

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl("/api/vendor-identity/tenants");
            x.StatusCodeShouldBe(201);
        });

        // Assert
        var location = result.Context.Response.Headers.Location!.ToString();
        location.ShouldStartWith("/api/vendor-identity/tenants/");

        var tenantId = Guid.Parse(location.Split('/').Last());

        await using var dbContext = _fixture.GetDbContext();
        var tenant = await dbContext.Tenants.FindAsync(tenantId);

        tenant.ShouldNotBeNull();
        tenant.OrganizationName.ShouldBe("Acme Pet Supplies");
        tenant.ContactEmail.ShouldBe("contact@acmepetsupplies.com");
        tenant.Status.ShouldBe(VendorTenantStatus.Onboarding);
        tenant.OnboardedAt.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateVendorTenant_WithDuplicateOrganizationName_Returns400()
    {
        // Arrange - create first tenant
        var firstCommand = new CreateVendorTenant(
            OrganizationName: "Unique Org Name",
            ContactEmail: "first@example.com"
        );

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(firstCommand).ToUrl("/api/vendor-identity/tenants");
            x.StatusCodeShouldBe(201);
        });

        // Act - try to create tenant with same organization name
        var duplicateCommand = new CreateVendorTenant(
            OrganizationName: "Unique Org Name",
            ContactEmail: "second@example.com"
        );

        // Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(duplicateCommand).ToUrl("/api/vendor-identity/tenants");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task CreateVendorTenant_WithMissingOrganizationName_Returns400()
    {
        // Arrange
        var command = new CreateVendorTenant(
            OrganizationName: "",
            ContactEmail: "contact@example.com"
        );

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl("/api/vendor-identity/tenants");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task CreateVendorTenant_WithInvalidEmail_Returns400()
    {
        // Arrange
        var command = new CreateVendorTenant(
            OrganizationName: "Test Org",
            ContactEmail: "not-an-email"
        );

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl("/api/vendor-identity/tenants");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task CreateVendorTenant_WithOrganizationNameExceeding200Chars_Returns400()
    {
        // Arrange
        var command = new CreateVendorTenant(
            OrganizationName: new string('A', 201),
            ContactEmail: "contact@example.com"
        );

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl("/api/vendor-identity/tenants");
            x.StatusCodeShouldBe(400);
        });
    }
}
