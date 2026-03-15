using Alba;
using CustomerIdentity.AddressBook;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CustomerIdentity.Api.IntegrationTests;

public class CustomerSearchTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public CustomerSearchTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCustomerByEmail_ExistingEmail_ReturnsCustomer()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var email = "alice@example.com";

        await using (var dbContext = _fixture.GetDbContext())
        {
            var customer = Customer.Create(customerId, email, "Alice", "Smith");
            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync();
        }

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/customers?email={email}");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var customerResponse = result.ReadAsJson<CustomerResponse>();
        customerResponse.ShouldNotBeNull();
        customerResponse.Id.ShouldBe(customerId);
        customerResponse.Email.ShouldBe(email);
        customerResponse.FirstName.ShouldBe("Alice");
        customerResponse.LastName.ShouldBe("Smith");
    }

    [Fact]
    public async Task GetCustomerByEmail_NonexistentEmail_ReturnsNotFound()
    {
        // Arrange
        var nonexistentEmail = "doesnotexist@example.com";

        // Act & Assert
        await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/customers?email={nonexistentEmail}");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetCustomerByEmail_EmailWithSpecialCharacters_ReturnsCustomer()
    {
        // Arrange
        var customerId = Guid.CreateVersion7();
        var email = "alice+test@example.com"; // Email with + character

        await using (var dbContext = _fixture.GetDbContext())
        {
            var customer = Customer.Create(customerId, email, "Alice", "Test");
            dbContext.Customers.Add(customer);
            await dbContext.SaveChangesAsync();
        }

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/customers?email={Uri.EscapeDataString(email)}");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var customerResponse = result.ReadAsJson<CustomerResponse>();
        customerResponse.ShouldNotBeNull();
        customerResponse.Email.ShouldBe(email);
    }
}
