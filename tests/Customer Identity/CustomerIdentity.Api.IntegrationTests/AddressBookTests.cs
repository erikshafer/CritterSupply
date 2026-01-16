using Alba;
using CustomerIdentity.AddressBook;
using Marten;
using Shouldly;

namespace CustomerIdentity.Api.IntegrationTests;

public class AddressBookTests : IClassFixture<CustomersApiFixture>
{
    private readonly CustomersApiFixture _fixture;

    public AddressBookTests(CustomersApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanAddAddressToCustomer()
    {
        var customerId = Guid.CreateVersion7();
        var command = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            "Apt 4B",
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        var location = result.Context.Response.Headers.Location!.ToString();
        location.ShouldNotBeNull();
        location.ShouldStartWith($"/api/customers/{customerId}/addresses/");

        // Verify address was persisted
        await using var session = _fixture.GetDocumentSession();
        var addresses = await session.Query<CustomerAddress>()
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();

        addresses.Count.ShouldBe(1);
        addresses[0].Nickname.ShouldBe("Home");
        addresses[0].IsDefault.ShouldBeTrue();
        addresses[0].IsVerified.ShouldBeTrue(); // Stub service always returns verified
    }

    [Fact]
    public async Task AddingDefaultAddressUnsetsExistingDefault()
    {
        var customerId = Guid.CreateVersion7();

        // Add first default shipping address
        var firstCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(firstCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        // Add second default shipping address
        var secondCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Work",
            "456 Office Blvd",
            "Suite 200",
            "Portland",
            "OR",
            "97201",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(secondCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        // Verify only the second address is default
        await using var session = _fixture.GetDocumentSession();
        var addresses = await session.Query<CustomerAddress>()
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();

        addresses.Count.ShouldBe(2);
        var defaults = addresses.Where(a => a.IsDefault).ToList();
        defaults.Count.ShouldBe(1);
        defaults[0].Nickname.ShouldBe("Work");

        // Both addresses should be verified by stub service
        addresses.All(a => a.IsVerified).ShouldBeTrue();
    }

    [Fact]
    public async Task CanUpdateAddress()
    {
        var customerId = Guid.CreateVersion7();

        // Add address
        var addCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        var addResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(addCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        var location = addResult.Context.Response.Headers.Location!.ToString();
        var addressId = Guid.Parse(location!.Split('/').Last());

        // Update address
        var updateCommand = new UpdateAddress(
            addressId,
            customerId,
            AddressType.Both,
            "Home (Updated)",
            "789 New St",
            "Unit 5",
            "Tacoma",
            "WA",
            "98402",
            "US");

        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(updateCommand).ToUrl($"/api/customers/{customerId}/addresses/{addressId}");
            x.StatusCodeShouldBe(204);
        });

        // Verify update
        await using var session = _fixture.GetDocumentSession();
        var updated = await session.LoadAsync<CustomerAddress>(addressId);

        updated.ShouldNotBeNull();
        updated.Nickname.ShouldBe("Home (Updated)");
        updated.AddressLine1.ShouldBe("789 New St");
        updated.City.ShouldBe("Tacoma");
        updated.Type.ShouldBe(AddressType.Both);
        updated.IsVerified.ShouldBeTrue(); // Re-verified on update
    }

    [Fact]
    public async Task CanSetDefaultAddress()
    {
        var customerId = Guid.CreateVersion7();

        // Add two addresses, first one default
        var firstCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(firstCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        var secondCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Work",
            "456 Office Blvd",
            null,
            "Portland",
            "OR",
            "97201",
            "US",
            IsDefault: false);

        var secondResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(secondCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        var location = secondResult.Context.Response.Headers.Location!.ToString();
        var secondAddressId = Guid.Parse(location!.Split('/').Last());

        // Set second address as default
        var setDefaultCommand = new SetDefaultAddress(secondAddressId, customerId);

        await _fixture.Host.Scenario(x =>
        {
            x.Put.Json(setDefaultCommand).ToUrl($"/api/customers/{customerId}/addresses/{secondAddressId}/set-default");
            x.StatusCodeShouldBe(204);
        });

        // Verify only second address is default
        await using var session = _fixture.GetDocumentSession();
        var addresses = await session.Query<CustomerAddress>()
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();

        var defaults = addresses.Where(a => a.IsDefault).ToList();
        defaults.Count.ShouldBe(1);
        defaults[0].Id.ShouldBe(secondAddressId);
    }

    [Fact]
    public async Task CanGetCustomerAddresses()
    {
        var customerId = Guid.CreateVersion7();

        // Add multiple addresses
        var shippingCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(shippingCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        var billingCommand = new AddAddress(
            customerId,
            AddressType.Billing,
            "Billing",
            "456 Office Blvd",
            null,
            "Portland",
            "OR",
            "97201",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(billingCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        // Get all addresses
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(200);
        });

        var addresses = result.ReadAsJson<List<CustomerAddress>>();
        addresses.ShouldNotBeNull();
        addresses.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CanGetAddressSnapshot()
    {
        var customerId = Guid.CreateVersion7();

        // Add address
        var command = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Home",
            "123 Main St",
            "Apt 4B",
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        var addResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(command).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        var location = addResult.Context.Response.Headers.Location!.ToString();
        var addressId = Guid.Parse(location!.Split('/').Last());

        // Get snapshot
        var snapshotResult = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/addresses/{addressId}/snapshot");
            x.StatusCodeShouldBe(200);
        });

        var snapshot = snapshotResult.ReadAsJson<AddressSnapshot>();
        snapshot.ShouldNotBeNull();
        snapshot.AddressLine1.ShouldBe("123 Main St");
        snapshot.AddressLine2.ShouldBe("Apt 4B");
        snapshot.City.ShouldBe("Seattle");
        snapshot.StateOrProvince.ShouldBe("WA");
        snapshot.PostalCode.ShouldBe("98101");
        snapshot.Country.ShouldBe("US");

        // Verify LastUsedAt was updated
        await using var session = _fixture.GetDocumentSession();
        var address = await session.LoadAsync<CustomerAddress>(addressId);
        address.ShouldNotBeNull();
        address.LastUsedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AddressTypesBothHandlesDefaultsCorrectly()
    {
        var customerId = Guid.CreateVersion7();

        // Add shipping-only default
        var shippingCommand = new AddAddress(
            customerId,
            AddressType.Shipping,
            "Shipping Only",
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(shippingCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        // Add billing-only default
        var billingCommand = new AddAddress(
            customerId,
            AddressType.Billing,
            "Billing Only",
            "456 Office Blvd",
            null,
            "Portland",
            "OR",
            "97201",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(billingCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        // Add Both default - should unset both existing defaults
        var bothCommand = new AddAddress(
            customerId,
            AddressType.Both,
            "Both",
            "789 Home Ave",
            null,
            "Tacoma",
            "WA",
            "98402",
            "US",
            IsDefault: true);

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(bothCommand).ToUrl($"/api/customers/{customerId}/addresses");
            x.StatusCodeShouldBe(201);
        });

        // Verify only the Both address is default
        await using var session = _fixture.GetDocumentSession();
        var addresses = await session.Query<CustomerAddress>()
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();

        addresses.Count.ShouldBe(3);
        var defaults = addresses.Where(a => a.IsDefault).ToList();
        defaults.Count.ShouldBe(1);
        defaults[0].Type.ShouldBe(AddressType.Both);
        defaults[0].Nickname.ShouldBe("Both");
    }
}
