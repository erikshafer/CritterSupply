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

    // ──────────────────────────────────────────────────────
    //  17. Deactivate User — Not Found
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUser_WhenUserNotFound_Returns400()
    {
        var tenantId = await CreateActiveTenantAsync("Deactivate NotFound Tenant", "deactivate-nf@test.com");
        var nonExistentUserId = Guid.NewGuid();

        var command = new DeactivateVendorUser(tenantId, nonExistentUserId, "No longer needed");

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{nonExistentUserId}/deactivate");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  18. Suspend Tenant — Not Found
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendTenant_WhenTenantNotFound_Returns400()
    {
        var nonExistentTenantId = Guid.NewGuid();
        var command = new SuspendVendorTenant(nonExistentTenantId, "Policy violation");

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{nonExistentTenantId}/suspend");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  19. Terminate Tenant — Not Found
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task TerminateTenant_WhenTenantNotFound_Returns400()
    {
        var nonExistentTenantId = Guid.NewGuid();
        var command = new TerminateVendorTenant(nonExistentTenantId, "Contract ended");

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{nonExistentTenantId}/terminate");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  20. Terminate Tenant — Missing Reason Returns 400
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task TerminateTenant_WithMissingReason_Returns400()
    {
        var tenantId = await CreateActiveTenantAsync("Terminate No Reason", "terminate-noreason@test.com");
        var command = new TerminateVendorTenant(tenantId, "");

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/terminate");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  21. Reinstate Tenant — Terminated Tenant Cannot Be Reinstated
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReinstateTenant_WhenTerminated_Returns400()
    {
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Reinstate Terminated",
            ContactEmail = "reinstate-terminated@test.com",
            Status = VendorTenantStatus.Terminated,
            OnboardedAt = DateTimeOffset.UtcNow,
            TerminatedAt = DateTimeOffset.UtcNow,
            TerminationReason = "Contract ended"
        };
        setupDb.Tenants.Add(tenant);
        await setupDb.SaveChangesAsync();

        var command = new ReinstateVendorTenant(tenant.Id);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/reinstate");
            x.StatusCodeShouldBe(400);
        });
    }

    // ──────────────────────────────────────────────────────
    //  22. Deactivate User in Suspended Tenant — Succeeds
    //  (Identity-level operations are not blocked by tenant suspension)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUser_InSuspendedTenant_Returns200()
    {
        // Arrange — create a suspended tenant with an active user
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Suspended Tenant Deactivate",
            ContactEmail = "suspended-deact@test.com",
            Status = VendorTenantStatus.Suspended,
            OnboardedAt = DateTimeOffset.UtcNow,
            SuspendedAt = DateTimeOffset.UtcNow,
            SuspensionReason = "Policy review"
        };
        setupDb.Tenants.Add(tenant);

        // Create two admins so deactivation is allowed
        var admin1 = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "susp-admin1@test.com",
            FirstName = "Admin",
            LastName = "One",
            Role = VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        var catalogUser = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "susp-catalog@test.com",
            FirstName = "Catalog",
            LastName = "User",
            Role = VendorRole.CatalogManager,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        setupDb.Users.AddRange(admin1, catalogUser);
        await setupDb.SaveChangesAsync();

        var command = new DeactivateVendorUser(tenant.Id, catalogUser.Id, "Removing during suspension");

        // Act — should succeed because validators don't check tenant status
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/users/{catalogUser.Id}/deactivate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == catalogUser.Id);
        user.Status.ShouldBe(VendorUserStatus.Deactivated);
        user.DeactivatedAt.ShouldNotBeNull();
    }

    // ──────────────────────────────────────────────────────
    //  23. Change Role in Suspended Tenant — Succeeds
    //  (Identity-level operations are not blocked by tenant suspension)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_InSuspendedTenant_Returns200()
    {
        // Arrange — create a suspended tenant with two admins
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Suspended Tenant Role",
            ContactEmail = "suspended-role@test.com",
            Status = VendorTenantStatus.Suspended,
            OnboardedAt = DateTimeOffset.UtcNow,
            SuspendedAt = DateTimeOffset.UtcNow,
            SuspensionReason = "Under investigation"
        };
        setupDb.Tenants.Add(tenant);

        var admin1 = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "susp-role-admin1@test.com",
            FirstName = "Admin",
            LastName = "One",
            Role = VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        var admin2 = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "susp-role-admin2@test.com",
            FirstName = "Admin",
            LastName = "Two",
            Role = VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        setupDb.Users.AddRange(admin1, admin2);
        await setupDb.SaveChangesAsync();

        var command = new ChangeVendorUserRole(tenant.Id, admin1.Id, VendorRole.CatalogManager);

        // Act — should succeed because validators don't check tenant status
        await _fixture.Host.Scenario(x =>
        {
            x.Patch.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/users/{admin1.Id}/role");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == admin1.Id);
        user.Role.ShouldBe(VendorRole.CatalogManager);
    }

    // ──────────────────────────────────────────────────────
    //  24. Reactivate User in Suspended Tenant — Succeeds
    //  (Identity-level operations are not blocked by tenant suspension)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateUser_InSuspendedTenant_Returns200()
    {
        // Arrange — create a suspended tenant with a deactivated user
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Suspended Tenant Reactivate",
            ContactEmail = "suspended-react@test.com",
            Status = VendorTenantStatus.Suspended,
            OnboardedAt = DateTimeOffset.UtcNow,
            SuspendedAt = DateTimeOffset.UtcNow,
            SuspensionReason = "Temporary hold"
        };
        setupDb.Tenants.Add(tenant);

        var deactivatedUser = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "susp-react-user@test.com",
            FirstName = "Deactivated",
            LastName = "User",
            Role = VendorRole.CatalogManager,
            Status = VendorUserStatus.Deactivated,
            InvitedAt = DateTimeOffset.UtcNow,
            DeactivatedAt = DateTimeOffset.UtcNow
        };
        setupDb.Users.Add(deactivatedUser);
        await setupDb.SaveChangesAsync();

        var command = new ReactivateVendorUser(tenant.Id, deactivatedUser.Id);

        // Act — should succeed because validators don't check tenant status
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/users/{deactivatedUser.Id}/reactivate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == deactivatedUser.Id);
        user.Status.ShouldBe(VendorUserStatus.Active);
        user.DeactivatedAt.ShouldBeNull();
    }

    // ──────────────────────────────────────────────────────
    //  25. Deactivate User in Terminated Tenant — Succeeds
    //  (Validators check tenant existence, not status)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUser_InTerminatedTenant_Returns200()
    {
        // Arrange — create a terminated tenant with an active user
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Terminated Tenant Deactivate",
            ContactEmail = "terminated-deact@test.com",
            Status = VendorTenantStatus.Terminated,
            OnboardedAt = DateTimeOffset.UtcNow,
            TerminatedAt = DateTimeOffset.UtcNow,
            TerminationReason = "Contract ended"
        };
        setupDb.Tenants.Add(tenant);

        // Need an admin to satisfy last-admin check + a user to deactivate
        var admin = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "term-deact-admin@test.com",
            FirstName = "Admin",
            LastName = "User",
            Role = VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        var catalogUser = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "term-deact-catalog@test.com",
            FirstName = "Catalog",
            LastName = "User",
            Role = VendorRole.CatalogManager,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        setupDb.Users.AddRange(admin, catalogUser);
        await setupDb.SaveChangesAsync();

        var command = new DeactivateVendorUser(tenant.Id, catalogUser.Id, "Cleanup after termination");

        // Act — should succeed because validators don't check tenant status
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/users/{catalogUser.Id}/deactivate");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == catalogUser.Id);
        user.Status.ShouldBe(VendorUserStatus.Deactivated);
        user.DeactivatedAt.ShouldNotBeNull();
    }

    // ──────────────────────────────────────────────────────
    //  26. Change Role in Terminated Tenant — Succeeds
    //  (Validators check tenant existence, not status)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_InTerminatedTenant_Returns200()
    {
        // Arrange — create a terminated tenant with two admins
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Terminated Tenant Role",
            ContactEmail = "terminated-role@test.com",
            Status = VendorTenantStatus.Terminated,
            OnboardedAt = DateTimeOffset.UtcNow,
            TerminatedAt = DateTimeOffset.UtcNow,
            TerminationReason = "Breach of contract"
        };
        setupDb.Tenants.Add(tenant);

        var admin1 = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "term-role-admin1@test.com",
            FirstName = "Admin",
            LastName = "One",
            Role = VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        var admin2 = new VendorUser
        {
            Id = Guid.NewGuid(),
            VendorTenantId = tenant.Id,
            Email = "term-role-admin2@test.com",
            FirstName = "Admin",
            LastName = "Two",
            Role = VendorRole.Admin,
            Status = VendorUserStatus.Active,
            InvitedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow
        };
        setupDb.Users.AddRange(admin1, admin2);
        await setupDb.SaveChangesAsync();

        var command = new ChangeVendorUserRole(tenant.Id, admin1.Id, VendorRole.ReadOnly);

        // Act — should succeed because validators don't check tenant status
        await _fixture.Host.Scenario(x =>
        {
            x.Patch.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/users/{admin1.Id}/role");
            x.StatusCodeShouldBe(200);
        });

        // Assert
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == admin1.Id);
        user.Role.ShouldBe(VendorRole.ReadOnly);
    }

    // ──────────────────────────────────────────────────────
    //  27. Invite User to Terminated Tenant — Succeeds
    //  (InviteVendorUserValidator checks tenant existence, not status)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task InviteUser_InTerminatedTenant_Returns201()
    {
        // Arrange — create a terminated tenant
        await using var setupDb = _fixture.GetDbContext();
        var tenant = new VendorTenant
        {
            Id = Guid.NewGuid(),
            OrganizationName = "Terminated Tenant Invite",
            ContactEmail = "terminated-invite@test.com",
            Status = VendorTenantStatus.Terminated,
            OnboardedAt = DateTimeOffset.UtcNow,
            TerminatedAt = DateTimeOffset.UtcNow,
            TerminationReason = "Account closed"
        };
        setupDb.Tenants.Add(tenant);
        await setupDb.SaveChangesAsync();

        var command = new InviteVendorUser(
            tenant.Id,
            "new-invite-terminated@test.com",
            "New",
            "User",
            VendorRole.CatalogManager);

        // Act — should succeed because validator doesn't check tenant status
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenant.Id}/users/invite");
            x.StatusCodeShouldBe(201);
        });

        // Assert — user and invitation were created
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == "new-invite-terminated@test.com");
        user.ShouldNotBeNull();
        user.VendorTenantId.ShouldBe(tenant.Id);
        user.Status.ShouldBe(VendorUserStatus.Invited);

        var invitation = await dbContext.Invitations.FirstOrDefaultAsync(i => i.VendorUserId == user.Id);
        invitation.ShouldNotBeNull();
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    // ──────────────────────────────────────────────────────
    //  28. Resend Invitation — Produces Different Token Each Time
    //  (Verifies new random bytes are generated per resend)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResendInvitation_ConsecutiveResends_ProduceDifferentTokens()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Token Diff Tenant", "tokendiff@test.com");
        var (userId, invitationId) = await CreateInvitedUserWithPendingInvitationAsync(tenantId, "tokendiff-user@test.com");

        var command = new ResendVendorUserInvitation(tenantId, userId);

        // Act — first resend
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend");
            x.StatusCodeShouldBe(200);
        });

        string tokenAfterFirstResend;
        {
            await using var db1 = _fixture.GetDbContext();
            var inv1 = await db1.Invitations.FirstAsync(i => i.Id == invitationId);
            tokenAfterFirstResend = inv1.Token;
            inv1.ResendCount.ShouldBe(1);
        }

        // Act — second resend
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend");
            x.StatusCodeShouldBe(200);
        });

        string tokenAfterSecondResend;
        {
            await using var db2 = _fixture.GetDbContext();
            var inv2 = await db2.Invitations.FirstAsync(i => i.Id == invitationId);
            tokenAfterSecondResend = inv2.Token;
            inv2.ResendCount.ShouldBe(2);
        }

        // Assert — all three tokens must be distinct (original, first resend, second resend)
        var originalToken = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890";
        tokenAfterFirstResend.ShouldNotBe(originalToken);
        tokenAfterSecondResend.ShouldNotBe(originalToken);
        tokenAfterSecondResend.ShouldNotBe(tokenAfterFirstResend);

        // Both tokens should be valid SHA-256 hex strings (64 chars)
        tokenAfterFirstResend.Length.ShouldBe(64);
        tokenAfterSecondResend.Length.ShouldBe(64);
    }

    // ──────────────────────────────────────────────────────
    //  29. Change Role — Same Role (No-Op) Succeeds
    //  (No validator rule prevents changing to the same role)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_ToSameRole_Returns200AndRoleUnchanged()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Same Role Tenant", "samerole@test.com");
        var userId = await CreateUserAsync(tenantId, VendorRole.CatalogManager, VendorUserStatus.Active, "samerole-user@test.com");

        var command = new ChangeVendorUserRole(tenantId, userId, VendorRole.CatalogManager);

        // Act — should succeed (no validator prevents same-role assignment)
        await _fixture.Host.Scenario(x =>
        {
            x.Patch.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/role");
            x.StatusCodeShouldBe(200);
        });

        // Assert — role should still be CatalogManager
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        user.Role.ShouldBe(VendorRole.CatalogManager);
    }

    // ──────────────────────────────────────────────────────
    //  30. Deactivate User — Already Deactivated Returns 400
    //  (Validator requires user to be in Active status)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUser_WhenAlreadyDeactivated_Returns400()
    {
        // Arrange — create a deactivated user
        var tenantId = await CreateActiveTenantAsync("Double Deactivate Tenant", "doubledeact@test.com");
        var userId = await CreateUserAsync(tenantId, VendorRole.CatalogManager, VendorUserStatus.Deactivated, "doubledeact-user@test.com");

        var command = new DeactivateVendorUser(tenantId, userId, "Second deactivation attempt");

        // Act & Assert — should fail because user is not Active
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/users/{userId}/deactivate");
            x.StatusCodeShouldBe(400);
        });

        // Verify status didn't change
        await using var dbContext = _fixture.GetDbContext();
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        user.Status.ShouldBe(VendorUserStatus.Deactivated);
    }

    // ──────────────────────────────────────────────────────
    //  31. Suspend Tenant — Empty Reason Returns 400
    //  (Validator requires non-empty reason)
    // ──────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendTenant_WithEmptyReason_Returns400()
    {
        // Arrange
        var tenantId = await CreateActiveTenantAsync("Suspend No Reason", "suspend-noreason@test.com");
        var command = new SuspendVendorTenant(tenantId, "");

        // Act & Assert — should fail because reason is empty
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/vendor-identity/tenants/{tenantId}/suspend");
            x.StatusCodeShouldBe(400);
        });

        // Verify tenant status didn't change
        await using var dbContext = _fixture.GetDbContext();
        var tenant = await dbContext.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.Status.ShouldBe(VendorTenantStatus.Active);
    }
}
