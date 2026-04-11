using Inventory.Management;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// HTTP endpoint wiring for stock write-off (Slice 24).
/// Handler and validator live in the domain project.
/// Requires OperationsManager policy — more destructive than damage recording.
/// </summary>
public static class WriteOffStockEndpoint
{
    [WolverinePost("/api/inventory/write-off-stock")]
    [Authorize(Policy = "OperationsManager")]
    public static WriteOffStock Post(WriteOffStock command) => command;
}
