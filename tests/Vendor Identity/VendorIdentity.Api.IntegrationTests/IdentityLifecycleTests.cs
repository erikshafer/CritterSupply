using Alba;
using Messages.Contracts.VendorIdentity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using VendorIdentity.TenantManagement;
using VendorIdentity.UserInvitations;
using VendorIdentity.UserManagement;

namespace VendorIdentity.Api.IntegrationTests;

public sealed class IdentityLifecycleTests : IClassFixture<VendorIdentityApiFixture>, IAsyncLifetime
{
    private readonly VendorIdentityApiFixture _fixture;

    public IdentityLifecycleTests(VendorIdentityApiFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDataAsync();
    }

    // ──────────────────────────────────────────────────────
    //  Helper methods
    // ──────────────────────────────────────────────────────

    private async Task<Guid> CreateActiveTenantAsync(string orgName = "Test Vendor", string email = "contact@test.com")
    {
        await using var dbContext = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = orgName,
            ContactEmail = email,
            Status = VendorTenantStatus.Active,
            OnboardedAt = DateTimeOffset.UtcNow
        };
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<Guid> CreateUserAsync(
        Guid tenantId,
        VendorRole role = VendorRole.Admin,
        VendorUserStatus status = VendorUserStatus.Active,
        string email = "user@test.com")
    {
        await using var dbContext = _fixture.GetDbContext();
        var user = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenantId,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Role = role,
            Status = status,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = status == VendorUserStatus.Active ? DateTimeOffset.UtcNow : null,
            DeactivatedAt = status == VendorUserStatus.Deactivated ? DateTimeOffset.UtcNow : null
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(Guid UserId, Guid InvitationId)> CreateInvitedUserWithPendingInvitationAsync(
        Guid tenantId,
        string email = "invited@test.com")
    {
        await using var dbContext = _fixture.GetDbContext();
        var user = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenantId,
            Email = email,
            FirstName = "Invited",
            LastName = "User",
            Role = VendorRole.CatalogManager,
            Status = VendorUserStatus.Invited,
            InvitedAt = DateTimeOffset.UtcNow
        };
        dbContext.Users.Add(user);

        var invitation = new VendorUserInvitation
        {
            Id = Guid.NewGuid(),
            VendorUserId = user.Id,
            VendorTenantId = tenantId,
            Token = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
            InvitedRole = VendorRole.CatalogManager,
            Status = InvitationStatus.Pending,
            InvitedAt = user.InvitedAt!.Value,
            ExpiresAt = user.InvitedAt.Value.AddHours(72),
            ResendCount = 0
        };
        dbContext.Invitations.Add(invitation);

        await dbContext.SaveChangesAsync();
        return (user.Id, invitation.Id);
    }

    // ──────────────────────────────────────────────────────
    //  1. Resend Invitation — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResendInvitation_WithPendingInvitation_Returns200AndUpdatesTokenAndExpiry()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Resend Tenant", "resend@test.com");
        var (userId, invitationId) = await CreateInvitedUserWithPendingInvitationAsync(tenantId, "resend-user@test.com");

        var command = new ResendVendorUserInvitation(tenantId, userId);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var invitation = await dbContext.Invitations.FirstAsync(i => i.Id == invitationId);

        invitation.Token.ShouldNotBe("ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890");
        invitation.Token.Length.ShouldBe(64); // SHA-256 hex string
        invitation.ResendCount.ShouldBe(1);
        invitation.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddHours(71));
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    // ──────────────────────────────────────────────────────
    //  2. Resend Invitation — User Not Found
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResendInvitation_WithNonExistentUser_Returns400()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Resend Tenant 2", "resend2@test.com");
        var nonExistentUserId = Guid.NewGuid();

        var command = new ResendVendorUserInvitation(tenantId, nonExistentUserId);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{nonExistentUserId}/invitation/resend");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  3. Revoke Invitation — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeInvitation_WithPendingInvitation_Returns200AndSetsRevokedStatus()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Revoke Tenant", "revoke@test.com");
        var (userId, invitationId) = await CreateInvitedUserWithPendingInvitationAsync(tenantId, "revoke-user@test.com");

        var command = new RevokeVendorUserInvitation(tenantId, userId, "No longer needed");

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/revoke");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var invitation = await dbContext.Invitations.FirstAsync(i => i.Id == invitationId);

        invitation.Status.ShouldBe(InvitationStatus.Revoked);
        invitation.RevokedAt.ShouldNotBeNull();
        invitation.RevokedAt.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
    }

    // ──────────────────────────────────────────────────────
    //  4. Revoke Invitation — No Pending Invitation
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeInvitation_WithNoPendingInvitation_Returns400()
    {
        // Arrange — create an active user (no pending invitation)
        var tenantId = await CreateActiveTenantAsync("Revoke Tenant 2", "revoke2@test.com");
        var userId = await CreateUserAsync(tenantId, VendorRole.CatalogManager, VendorUserStatus.Active, "revoke-active@test.com");

        var command = new RevokeVendorUserInvitation(tenantId, userId, "Test reason");

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/revoke");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  5. Reactivate User — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateUser_WhenDeactivated_Returns200AndSetsActiveStatus()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Reactivate Tenant", "reactivate@test.com");
        var userId = await CreateUserAsync(tenantId, VendorRole.CatalogManager, VendorUserStatus.Deactivated, "reactivate-user@test.com");

        var command = new ReactivateVendorUser(tenantId, userId);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/reactivate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);

        user.Status.ShouldBe(VendorUserStatus.Active);
        user.DeactivatedAt.ShouldBeNull();
    }

    // ──────────────────────────────────────────────────────
    //  6. Reactivate User — Already Active
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateUser_WhenAlreadyActive_Returns400()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Reactivate Tenant 2", "reactivate2@test.com");
        var userId = await CreateUserAsync(tenantId, VendorRole.CatalogManager, VendorUserStatus.Active, "reactivate-active@test.com");

        var command = new ReactivateVendorUser(tenantId, userId);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/reactivate");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  7. Change Role — Happy Path (Admin → CatalogManager)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_FromAdminToCatalogManager_Returns200AndUpdatesRole()
    {
        // Arrange — need at least 2 admins so we can demote one
        var tenantId = await CreateActiveTenantAsync("Role Change Tenant", "rolechange@test.com");
        var admin1 = await CreateUserAsync(tenantId, VendorRole.Admin, VendorUserStatus.Active, "admin1@test.com");
        var admin2 = await CreateUserAsync(tenantId, VendorRole.Admin, VendorUserStatus.Active, "admin2@test.com");

        var command = new ChangeVendorUserRole(tenantId, admin1, VendorRole.CatalogManager);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Patch.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{admin1}/role");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == admin1);

        user.Role.ShouldBe(VendorRole.CatalogManager);
    }

    // ──────────────────────────────────────────────────────
    //  8. Change Role — Last Admin Protection
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_LastAdminDemotion_Returns400()
    {
        // Arrange — only one admin
        var tenantId = await CreateActiveTenantAsync("Last Admin Role Tenant", "lastadmin-role@test.com");
        var soleAdmin = await CreateUserAsync(tenantId, VendorRole.Admin, VendorUserStatus.Active, "sole-admin-role@test.com");

        var command = new ChangeVendorUserRole(tenantId, soleAdmin, VendorRole.ReadOnly);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Patch.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{soleAdmin}/role");
            x.StatusCodeShouldBe(400);
        });

        // Verify role didn't change
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == soleAdmin);
        user.Role.ShouldBe(VendorRole.Admin);
    }

    // ──────────────────────────────────────────────────────
    //  9. Deactivate User — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUser_WhenActive_Returns200AndSetsDeactivatedStatus()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Deactivate Tenant", "deactivate@test.com");
        var userId = await CreateUserAsync(tenantId, VendorRole.CatalogManager, VendorUserStatus.Active, "deactivate-user@test.com");

        var command = new DeactivateVendorUser(tenantId, userId, "Left the company");

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/deactivate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);

        user.Status.ShouldBe(VendorUserStatus.Deactivated);
        user.DeactivatedAt.ShouldNotBeNull();
        user.DeactivatedAt.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
    }

    // ──────────────────────────────────────────────────────
    //  10. Deactivate User — Last Admin Protection
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUser_LastAdmin_Returns400()
    {
        // Arrange — only one admin
        var tenantId = await CreateActiveTenantAsync("Last Admin Deactivate", "lastadmin-deact@test.com");
        var soleAdmin = await CreateUserAsync(tenantId, VendorRole.Admin, VendorUserStatus.Active, "sole-admin-deact@test.com");

        var command = new DeactivateVendorUser(tenantId, soleAdmin, "Attempted deactivation");

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{soleAdmin}/deactivate");
            x.StatusCodeShouldBe(400);
        });

        // Verify user status didn't change
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == soleAdmin);
        user.Status.ShouldBe(VendorUserStatus.Active);
    }

    // ──────────────────────────────────────────────────────
    //  11. Suspend Tenant — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendTenant_WhenActive_Returns200AndSetsSuspendedStatus()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Suspend Tenant", "suspend@test.com");

        var command = new SuspendVendorTenant(tenantId, "Policy violation");

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/suspend");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var tenant = await dbContext.Tenants.FirstAsync(t => t.Id == tenantId);

        tenant.Status.ShouldBe(VendorTenantStatus.Suspended);
        tenant.SuspendedAt.ShouldNotBeNull();
        tenant.SuspendedAt.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
        tenant.SuspensionReason.ShouldBe("Policy violation");
    }

    // ──────────────────────────────────────────────────────
    //  12. Suspend Tenant — Already Suspended
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendTenant_WhenAlreadySuspended_Returns400()
    {
        // Arrange — create a suspended tenant
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Already Suspended",
            ContactEmail = "suspended@test.com",
            Status = VendorTenantStatus.Suspended,
            OnboardedAt = DateTimeOffset.UtcNow,
            SuspendedAt = DateTimeOffset.UtcNow,
            SuspensionReason = "Previous reason"
        };
        setupDb.Tenants.Add(tenant);
        await setupDb.SaveChangesAsync();

        var command = new SuspendVendorTenant(tenant.Id, "New reason");

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/suspend");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  13. Reinstate Tenant — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReinstateTenant_WhenSuspended_Returns200AndSetsActiveStatus()
    {
        // Arrange — create a suspended tenant
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Reinstate Tenant",
            ContactEmail = "reinstate@test.com",
            Status = VendorTenantStatus.Suspended,
            OnboardedAt = DateTimeOffset.UtcNow,
            SuspendedAt = DateTimeOffset.UtcNow,
            SuspensionReason = "Under review"
        };
        setupDb.Tenants.Add(tenant);
        await setupDb.SaveChangesAsync();

        var command = new ReinstateVendorTenant(tenant.Id);

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/reinstate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var updated = await dbContext.Tenants.FirstAsync(t => t.Id == tenant.Id);

        updated.Status.ShouldBe(VendorTenantStatus.Active);
        updated.SuspendedAt.ShouldBeNull();
        updated.SuspensionReason.ShouldBeNull();
    }

    // ──────────────────────────────────────────────────────
    //  14. Reinstate Tenant — Not Suspended
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReinstateTenant_WhenNotSuspended_Returns400()
    {
        // Arrange — tenant is Active, not Suspended
        var tenantId = await CreateActiveTenantAsync("Reinstate Active Tenant", "reinstate-active@test.com");

        var command = new ReinstateVendorTenant(tenantId);

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/reinstate");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  15. Terminate Tenant — Happy Path
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task TerminateTenant_WhenActive_Returns200AndSetsTerminatedStatus()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Terminate Tenant", "terminate@test.com");

        var command = new TerminateVendorTenant(tenantId, "Contract violation");

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/terminate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var tenant = await dbContext.Tenants.FirstAsync(t => t.Id == tenantId);

        tenant.Status.ShouldBe(VendorTenantStatus.Terminated);
        tenant.TerminatedAt.ShouldNotBeNull();
        tenant.TerminatedAt.Value.ShouldBeInRange(DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
        tenant.TerminationReason.ShouldBe("Contract violation");
    }

    // ──────────────────────────────────────────────────────
    //  16. Terminate Tenant — Already Terminated
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task TerminateTenant_WhenAlreadyTerminated_Returns400()
    {
        // Arrange — create a terminated tenant
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Already Terminated",
            ContactEmail = "terminated@test.com",
            Status = VendorTenantStatus.Terminated,
            OnboardedAt = DateTimeOffset.UtcNow,
            TerminatedAt = DateTimeOffset.UtcNow
        };
        setupDb.Tenants.Add(tenant);
        await setupDb.SaveChangesAsync();

        var command = new TerminateVendorTenant(tenant.Id, "Duplicate contract");

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/terminate");
            x.StatusCodeShouldBe(400);
        });
    }
}
