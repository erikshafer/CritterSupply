---
name: critterstack-testing-patterns
description: Write unit and integration tests for applications using the Critter Stack, which tools include Wolverine and Marten. Covers message handling, message validation, event persistence, endpoint interaction validation. Includes guidance on using the Alba integration testing library. 
---

# Critter Stack Testing Patterns

## When to Use This Skill

Use this skill when:
- Writing unit tests for Wolverine message handlers
- Testing Marten event sourcing and document storage
- Validating message routing and endpoint interactions
- Mocking external dependencies in handler tests

### ✅ Use Alba for integration tests

Easy integration testing for ASP.NET Core applications, especially those using Wolverine and Marten.
- Declarative Syntax
- Classic & Minimal API Support
- Authorization Stubbing

### ✅ Use Shouldly for validation

Shouldly is an assertion framework which focuses on giving great error messages when the assertion fails while being simple and terse. Shouldly uses the code before the ShouldBe statement to report on errors, which makes diagnosing easier.

## Required NuGet Packages

```xml
<ItemGroup>
  <!-- xUnit (or your preferred test framework) -->
  <PackageReference Include="xunit" Version="*" />
  <PackageReference Include="xunit.runner.visualstudio" Version="*" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="*" />

  <!-- Integration testing framework -->
  <PackageReference Include="Alba" Version="*" />
    
  <!-- Assertions (recommended) -->
  <PackageReference Include="Shouldly" Version="*" />
</ItemGroup>
```
---

## Additional Resources

- **Alba (documentation)**: https://jasperfx.github.io/alba/
- **Alba (github)**: https://github.com/JasperFx/alba
- **Shouldly (documentation)**: https://docs.shouldly.org/
- **Shouldly (github)**: https://github.com/shouldly/shouldly

# Alba - Sending and Checking JSON

Example 1: Sending a JSON command and checking for a successful (200 OK) response.

```csharp
// Good, inherit from IntegrationContext and set the AppFixture.
public class AddInventoryTests(AppFixture fixture) : IntegrationContext(fixture)
{
    private readonly AppFixture _fixture = fixture;

    // A simple yet effective test using Alba to send a JSON command
    [Fact]
    public async Task AddInventorySucceeds()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var command = new AddInventory(id, 10, "Initial stock");

        // Act & Assert
        await _fixture.Host!.Scenario(x =>
        {
            x.Post.Url($"/api/inventory/{id}/add");
            x.Post.Json(command);
            x.StatusCodeShouldBeOk(); // Expect a 200 OK response
        });
    }
}
```

Example 2: Sending a JSON command and checking for a specific response body. Assertions are handled by the Shouldly library. As we're working with event sourced data, made possible by Marten, we query for events after sending the command via a `LightweightSession`. If we were needing to check data stored via EF Core, we would do something similar with a `DbContext` fetching the designated entity.

```csharp
    [Fact]
    public async Task CommitInventory_WithExistingReservation_Succeeds()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        await using var session = Store.LightweightSession();
        var inventoryAdded = new InventoryAdded(productId, 100, "Initial stock", DateTimeProvider.UtcNow);
        var inventoryReserved = new InventoryReserved(orderId, 15, DateTimeProvider.UtcNow);
        session.Events.StartStream<ProductInventory>(productId, inventoryAdded, inventoryReserved);
        await session.SaveChangesAsync();

        var command = new CommitInventory(productId, orderId);

        // Act
        var tracked = await Host.InvokeMessageAndWaitAsync(command);

        // Assert
        tracked.Executed.MessagesOf<CommitInventory>()
            .ShouldHaveSingleItem();

        await using var querySession = Store.LightweightSession();
        var inventory = await querySession.Events.AggregateStreamAsync<ProductInventory>(productId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(85); // Still reduced
        inventory.ReservedQuantity.ShouldBe(0); // Reservation removed
        inventory.Reservations.ShouldNotContainKey(orderId); // No longer reserved
    }
```
