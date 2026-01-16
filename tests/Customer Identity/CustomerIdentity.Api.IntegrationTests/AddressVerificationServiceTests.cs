using CustomerIdentity.AddressBook;
using Shouldly;

namespace CustomerIdentity.Api.IntegrationTests;

public class AddressVerificationServiceTests
{
    [Fact]
    public async Task StubService_AlwaysReturnsVerifiedStatus()
    {
        // Arrange
        var service = new StubAddressVerificationService();

        // Act
        var result = await service.VerifyAsync(
            "123 Fake St",
            null,
            "Springfield",
            "IL",
            "62701",
            "US",
            CancellationToken.None);

        // Assert
        result.Status.ShouldBe(VerificationStatus.Verified);
    }

    [Fact]
    public async Task StubService_ReturnsConfidenceScoreOfOne()
    {
        // Arrange
        var service = new StubAddressVerificationService();

        // Act
        var result = await service.VerifyAsync(
            "456 Test Ave",
            "Apt 5B",
            "Testville",
            "CA",
            "90210",
            "US",
            CancellationToken.None);

        // Assert
        result.ConfidenceScore.ShouldBe(1.0);
    }

    [Fact]
    public async Task StubService_ReturnsSuggestedAddressMatchingInput()
    {
        // Arrange
        var service = new StubAddressVerificationService();

        // Act
        var result = await service.VerifyAsync(
            "789 Example Blvd",
            "Suite 200",
            "Demo City",
            "TX",
            "75001",
            "US",
            CancellationToken.None);

        // Assert
        result.SuggestedAddress.ShouldNotBeNull();
        result.SuggestedAddress.AddressLine1.ShouldBe("789 Example Blvd");
        result.SuggestedAddress.AddressLine2.ShouldBe("Suite 200");
        result.SuggestedAddress.City.ShouldBe("Demo City");
        result.SuggestedAddress.StateOrProvince.ShouldBe("TX");
        result.SuggestedAddress.PostalCode.ShouldBe("75001");
        result.SuggestedAddress.Country.ShouldBe("US");
    }

    [Fact]
    public async Task StubService_HandlesNullAddressLine2()
    {
        // Arrange
        var service = new StubAddressVerificationService();

        // Act
        var result = await service.VerifyAsync(
            "321 Test Rd",
            null,
            "Test Town",
            "NY",
            "10001",
            "US",
            CancellationToken.None);

        // Assert
        result.SuggestedAddress.ShouldNotBeNull();
        result.SuggestedAddress.AddressLine2.ShouldBeNull();
    }

    [Fact]
    public async Task StubService_ReturnsNoErrorMessage()
    {
        // Arrange
        var service = new StubAddressVerificationService();

        // Act
        var result = await service.VerifyAsync(
            "999 Sample St",
            null,
            "Sample City",
            "FL",
            "33101",
            "US",
            CancellationToken.None);

        // Assert
        result.ErrorMessage.ShouldBeNull();
    }
}
