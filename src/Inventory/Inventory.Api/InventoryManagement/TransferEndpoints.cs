using Inventory.Management;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// HTTP endpoint wiring for inter-warehouse transfer operations (Slices 25–29).
/// Handlers, validators, and domain logic live in the domain project.
/// </summary>
public static class TransferEndpoints
{
    [WolverinePost("/api/inventory/transfers/request")]
    [Authorize(Policy = "OperationsManager")]
    public static RequestTransfer PostRequest(RequestTransfer command) => command;

    [WolverinePost("/api/inventory/transfers/ship")]
    [Authorize(Policy = "WarehouseClerk")]
    public static ShipTransfer PostShip(ShipTransfer command) => command;

    [WolverinePost("/api/inventory/transfers/receive")]
    [Authorize(Policy = "WarehouseClerk")]
    public static ReceiveTransfer PostReceive(ReceiveTransfer command) => command;

    [WolverinePost("/api/inventory/transfers/cancel")]
    [Authorize(Policy = "OperationsManager")]
    public static CancelTransfer PostCancel(CancelTransfer command) => command;
}
