using Alba;
using Messages.Contracts.VendorIdentity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;

namespace VendorIdentity.Api.IntegrationTests;

public sealed class UserInvitationTests : IClassFixture<VendorIdentityApiFixture>, IAsyncLifetime
{
    private readonly VendorIdentityApiFixture _fixture;

    public UserInvitationTests(VendorIdentityApiFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDataAsync();
    }

    private async Task<Guid> CreateTenantAsync(string orgName, string contactEmail)
    {
        await using var dbContext = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = orgName,
            ContactEmail = contactEmail,
            Status = VendorTenantStatus.Active,
            OnboardedAt = DateTimeOffset.UtcNow
        };
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant.Id;
    }

    [Fact]
    public async Task InviteVendorUser_WithValidData_Returns201AndCreatesUserAndInvitation()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Vendor", "contact@testvendor.com");

        var command = new InviteVendorUser(
            tenantId,
            "newuser@testvendor.com",
            "Jane",
            "Smith",
            VendorRole.CatalogManager
        );

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
            x.StatusCodeShouldBe(201);
        });

        // Assert
        var location = result.Context.Response.Headers.Location!.ToString();
        location.ShouldStartWith($"/api/vendor-identity/tenants/{tenantId}/users/");

        var userId = Guid.Parse(location.Split('/').Last());

        await using var dbContext = _fixture.GetDbContext();

        // Verify user created
        var user = await dbContext.Users
            .Include(u => u.Invitations)
            .FirstOrDefaultAsync(u => u.Id == userId);

        user.ShouldNotBeNull();
        user.VendorTenantId.ShouldBe(tenantId);
        user.Email.ShouldBe("newuser@testvendor.com");
        user.FirstName.ShouldBe("Jane");
        user.LastName.ShouldBe("Smith");
        user.Role.ShouldBe(VendorRole.CatalogManager);
        user.Status.ShouldBe(VendorUserStatus.Invited);
        user.InvitedAt.ShouldNotBeNull();
        user.PasswordHash.ShouldBeNull();

        // Verify invitation created
        user.Invitations.Count.ShouldBe(1);
        var invitation = user.Invitations.First();
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.Token.ShouldNotBeNullOrWhiteSpace();
        invitation.ExpiresAt.ShouldBe(user.InvitedAt!.Value.AddHours(72), TimeSpan.FromSeconds(1));
        invitation.ResendCount.ShouldBe(0);
    }

    [Fact]
    public async Task InviteVendorUser_WithNonExistentTenant_Returns400()
    {
        // Arrange
        var nonExistentTenantId = Guid.NewGuid();

        var command = new InviteVendorUser(
            nonExistentTenantId,
            "user@example.com",
            "John",
            "Doe",
            VendorRole.Admin
        );

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{nonExistentTenantId}/users/invite");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task InviteVendorUser_WithDuplicateEmail_Returns400()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Vendor 2", "contact@testvendor2.com");

        var firstCommand = new InviteVendorUser(
            tenantId,
            "existing@testvendor.com",
            "First",
            "User",
            VendorRole.Admin
        );

        // Create first user
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(firstCommand).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
            x.StatusCodeShouldBe(201);
        });

        // Act - try to invite user with same email
        var duplicateCommand = new InviteVendorUser(
            tenantId,
            "existing@testvendor.com",
            "Second",
            "User",
            VendorRole.ReadOnly
        );

        // Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(duplicateCommand).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task InviteVendorUser_WithInvalidEmail_Returns400()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Vendor 3", "contact@testvendor3.com");

        var command = new InviteVendorUser(
            tenantId,
            "not-an-email",
            "John",
            "Doe",
            VendorRole.Admin
        );

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task InviteVendorUser_WithMissingFirstName_Returns400()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Vendor 4", "contact@testvendor4.com");

        var command = new InviteVendorUser(
            tenantId,
            "user@example.com",
            "",
            "Doe",
            VendorRole.Admin
        );

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task InviteVendorUser_TokenIsHashed()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Vendor 5", "contact@testvendor5.com");

        var command = new InviteVendorUser(
            tenantId,
            "tokentest@example.com",
            "Token",
            "Test",
            VendorRole.Admin
        );

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/invite");
            x.StatusCodeShouldBe(201);
        });

        // Assert - verify token is stored as hash (SHA-256 hex = 64 characters)
        await using var dbContext = _fixture.GetDbContext();
        var invitation = await dbContext.Invitations
            .FirstOrDefaultAsync(i => i.VendorTenantId == tenantId);

        invitation.ShouldNotBeNull();
        invitation.Token.Length.ShouldBe(64); // SHA-256 hex string
        invitation.Token.ShouldMatch(@"^[0-9A-F]{64}$"); // Hex digits only
    }
}
