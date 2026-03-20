using Alba;
using BackofficeIdentity.Identity;
using BackofficeIdentity.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using HttpStatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace BackofficeIdentity.Api.IntegrationTests;

/// <summary>
/// Integration tests for ResetBackofficeUserPassword endpoint.
/// Tests password hashing, refresh token invalidation, validation, and error handling.
/// </summary>
public sealed class ResetBackofficeUserPasswordTests : IClassFixture<BackofficeIdentityApiFixture>
{
    private readonly BackofficeIdentityApiFixture _fixture;
    private static readonly PasswordHasher<BackofficeUser> PasswordHasher = new();

    public ResetBackofficeUserPasswordTests(BackofficeIdentityApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ResetPassword_WithValidUserId_UpdatesPasswordHashAndInvalidatesRefreshToken()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var userId = Guid.NewGuid();
        var originalPasswordHash = PasswordHasher.HashPassword(null!, "OldPassword123");
        var user = new BackofficeUser
        {
            Id = userId,
            Email = $"test-{userId}@critter.test",
            PasswordHash = originalPasswordHash,
            FirstName = "Test",
            LastName = "User",
            Role = BackofficeRole.CustomerService,
            Status = BackofficeUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            RefreshToken = "original-refresh-token",
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var newPassword = "NewSecurePassword123";
        var request = new { newPassword };

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl($"/api/backoffice-identity/users/{userId}/reset-password");
            x.StatusCodeShouldBe(HttpStatusCodes.Status200OK);
        });

        // Assert
        var response = result.ReadAsJson<ResetPasswordResponse>();
        response.ShouldNotBeNull();
        response.UserId.ShouldBe(userId);
        response.ResetAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-5));

        // Verify password hash changed
        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            var updatedUser = await dbContext.Users.FindAsync(userId);

            updatedUser.ShouldNotBeNull();
            updatedUser.PasswordHash.ShouldNotBe(originalPasswordHash);

            // Verify new password is valid
            var verifyResult = PasswordHasher.VerifyHashedPassword(updatedUser, updatedUser.PasswordHash, newPassword);
            verifyResult.ShouldBe(PasswordVerificationResult.Success);

            // Verify refresh token invalidated
            updatedUser.RefreshToken.ShouldBeNull();
            updatedUser.RefreshTokenExpiresAt.ShouldBeNull();

            // Verify other fields unchanged
            updatedUser.Email.ShouldBe(user.Email);
            updatedUser.FirstName.ShouldBe(user.FirstName);
            updatedUser.LastName.ShouldBe(user.LastName);
            updatedUser.Role.ShouldBe(user.Role);
            updatedUser.Status.ShouldBe(user.Status);
            updatedUser.CreatedAt.ShouldBe(user.CreatedAt, TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task ResetPassword_WithNonExistentUser_Returns404()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var nonExistentUserId = Guid.NewGuid();
        var request = new { newPassword = "NewPassword123" };

        // Act & Assert
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl($"/api/backoffice-identity/users/{nonExistentUserId}/reset-password");
            scenario.StatusCodeShouldBe(HttpStatusCodes.Status404NotFound);
        });
    }

    [Fact]
    public async Task ResetPassword_WithPasswordLessThan8Chars_FailsValidation()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var userId = Guid.NewGuid();
        var user = new BackofficeUser
        {
            Id = userId,
            Email = $"test-{userId}@critter.test",
            PasswordHash = PasswordHasher.HashPassword(null!, "OldPassword123"),
            FirstName = "Test",
            LastName = "User",
            Role = BackofficeRole.CustomerService,
            Status = BackofficeUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var request = new { newPassword = "Short1" }; // Only 6 characters

        // Act & Assert
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl($"/api/backoffice-identity/users/{userId}/reset-password");
            scenario.StatusCodeShouldBe(HttpStatusCodes.Status400BadRequest);
        });
    }

    [Fact]
    public async Task ResetPassword_WithEmptyPassword_FailsValidation()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var userId = Guid.NewGuid();
        var user = new BackofficeUser
        {
            Id = userId,
            Email = $"test-{userId}@critter.test",
            PasswordHash = PasswordHasher.HashPassword(null!, "OldPassword123"),
            FirstName = "Test",
            LastName = "User",
            Role = BackofficeRole.CustomerService,
            Status = BackofficeUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var request = new { newPassword = "" };

        // Act & Assert
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(request).ToUrl($"/api/backoffice-identity/users/{userId}/reset-password");
            scenario.StatusCodeShouldBe(HttpStatusCodes.Status400BadRequest);
        });
    }

    [Fact]
    public async Task ResetPassword_PreservesOtherUserFields()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var userId = Guid.NewGuid();
        var lastLoginAt = DateTimeOffset.UtcNow.AddDays(-3);
        var createdAt = DateTimeOffset.UtcNow.AddMonths(-6);

        var user = new BackofficeUser
        {
            Id = userId,
            Email = $"test-{userId}@critter.test",
            PasswordHash = PasswordHasher.HashPassword(null!, "OldPassword123"),
            FirstName = "John",
            LastName = "Doe",
            Role = BackofficeRole.PricingManager,
            Status = BackofficeUserStatus.Active,
            CreatedAt = createdAt,
            LastLoginAt = lastLoginAt,
            RefreshToken = "refresh-token",
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var request = new { newPassword = "NewPassword123" };

        // Act
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl($"/api/backoffice-identity/users/{userId}/reset-password");
            x.StatusCodeShouldBe(HttpStatusCodes.Status200OK);
        });

        // Assert
        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            var updatedUser = await dbContext.Users.FindAsync(userId);

            updatedUser.ShouldNotBeNull();

            // These fields should NOT change
            updatedUser.Email.ShouldBe(user.Email);
            updatedUser.FirstName.ShouldBe(user.FirstName);
            updatedUser.LastName.ShouldBe(user.LastName);
            updatedUser.Role.ShouldBe(user.Role);
            updatedUser.Status.ShouldBe(user.Status);
            updatedUser.CreatedAt.ShouldBe(createdAt, TimeSpan.FromMilliseconds(1));
            updatedUser.LastLoginAt.ShouldNotBeNull();
            updatedUser.LastLoginAt.Value.ShouldBe(lastLoginAt, TimeSpan.FromMilliseconds(1));

            // Refresh token SHOULD be cleared
            updatedUser.RefreshToken.ShouldBeNull();
            updatedUser.RefreshTokenExpiresAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ResetPassword_WithDeactivatedUser_StillWorksButUserStaysDeactivated()
    {
        // Arrange
        await _fixture.CleanAllDataAsync();

        var userId = Guid.NewGuid();
        var deactivatedAt = DateTimeOffset.UtcNow.AddDays(-5);

        var user = new BackofficeUser
        {
            Id = userId,
            Email = $"test-{userId}@critter.test",
            PasswordHash = PasswordHasher.HashPassword(null!, "OldPassword123"),
            FirstName = "Test",
            LastName = "User",
            Role = BackofficeRole.CustomerService,
            Status = BackofficeUserStatus.Deactivated,
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
            DeactivatedAt = deactivatedAt,
            DeactivationReason = "User requested account closure"
        };

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var request = new { newPassword = "NewPassword123" };

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(request).ToUrl($"/api/backoffice-identity/users/{userId}/reset-password");
            x.StatusCodeShouldBe(HttpStatusCodes.Status200OK);
        });

        // Assert
        var response = result.ReadAsJson<ResetPasswordResponse>();
        response.UserId.ShouldBe(userId);

        using (var scope = _fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeIdentityDbContext>();
            var updatedUser = await dbContext.Users.FindAsync(userId);

            updatedUser.ShouldNotBeNull();

            // Status should remain Deactivated
            updatedUser.Status.ShouldBe(BackofficeUserStatus.Deactivated);
            updatedUser.DeactivatedAt.ShouldNotBeNull();
            updatedUser.DeactivatedAt.Value.ShouldBe(deactivatedAt, TimeSpan.FromMilliseconds(1));
            updatedUser.DeactivationReason.ShouldBe(user.DeactivationReason);

            // Password should still be updated
            var verifyResult = PasswordHasher.VerifyHashedPassword(updatedUser, updatedUser.PasswordHash, "NewPassword123");
            verifyResult.ShouldBe(PasswordVerificationResult.Success);
        }
    }
}
