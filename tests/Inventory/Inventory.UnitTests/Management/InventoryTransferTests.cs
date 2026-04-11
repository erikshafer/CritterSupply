using Inventory.Management;
using Shouldly;

namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for the InventoryTransfer aggregate — Create and Apply methods.
/// </summary>
public class InventoryTransferTests
{
    private static readonly DateTimeOffset DefaultTimestamp = DateTimeOffset.UtcNow;

    [Fact]
    public void Create_From_TransferRequested_Sets_Initial_State()
    {
        var transferId = Guid.CreateVersion7();
        var evt = new TransferRequested(transferId, "SKU-001", "WH-01", "WH-02", 50, "ops@test.com", DefaultTimestamp);

        var transfer = InventoryTransfer.Create(evt);

        transfer.Id.ShouldBe(transferId);
        transfer.Sku.ShouldBe("SKU-001");
        transfer.SourceWarehouseId.ShouldBe("WH-01");
        transfer.DestinationWarehouseId.ShouldBe("WH-02");
        transfer.Quantity.ShouldBe(50);
        transfer.Status.ShouldBe(TransferStatus.Requested);
        transfer.ReceivedQuantity.ShouldBeNull();
        transfer.RequestedBy.ShouldBe("ops@test.com");
    }

    [Fact]
    public void Apply_TransferShipped_Sets_Status_Shipped()
    {
        var transfer = CreateTransfer();

        var result = transfer.Apply(new TransferShipped(transfer.Id, "shipper@test.com", DefaultTimestamp));

        result.Status.ShouldBe(TransferStatus.Shipped);
    }

    [Fact]
    public void Apply_TransferReceived_Sets_Status_And_Quantity()
    {
        var transfer = CreateTransfer().Apply(new TransferShipped(Guid.Empty, "s", DefaultTimestamp));

        var result = transfer.Apply(new TransferReceived(transfer.Id, 50, "receiver@test.com", DefaultTimestamp));

        result.Status.ShouldBe(TransferStatus.Received);
        result.ReceivedQuantity.ShouldBe(50);
    }

    [Fact]
    public void Apply_TransferShortReceived_Sets_Status_And_Partial_Quantity()
    {
        var transfer = CreateTransfer().Apply(new TransferShipped(Guid.Empty, "s", DefaultTimestamp));

        var result = transfer.Apply(new TransferShortReceived(transfer.Id, 50, 40, 10, "receiver@test.com", DefaultTimestamp));

        result.Status.ShouldBe(TransferStatus.Received);
        result.ReceivedQuantity.ShouldBe(40);
    }

    [Fact]
    public void Apply_TransferCancelled_Sets_Status_Cancelled()
    {
        var transfer = CreateTransfer();

        var result = transfer.Apply(new TransferCancelled(transfer.Id, "No longer needed", "ops@test.com", DefaultTimestamp));

        result.Status.ShouldBe(TransferStatus.Cancelled);
    }

    private static InventoryTransfer CreateTransfer() =>
        InventoryTransfer.Create(new TransferRequested(
            Guid.CreateVersion7(), "SKU-001", "WH-01", "WH-02", 50, "ops@test.com", DefaultTimestamp));
}
