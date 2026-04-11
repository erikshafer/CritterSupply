using Inventory.Management;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// HTTP endpoint wiring for cycle count operations (Slices 20–22).
/// Handlers and validators live in the domain project; this file
/// exposes them as HTTP endpoints with authorization.
/// </summary>
public static class CycleCountEndpoints
{
    [WolverinePost("/api/inventory/initiate-cycle-count")]
    [Authorize(Policy = "WarehouseClerk")]
    public static InitiateCycleCount Post(InitiateCycleCount command) => command;

    [WolverinePost("/api/inventory/complete-cycle-count")]
    [Authorize(Policy = "WarehouseClerk")]
    public static CompleteCycleCount Post(CompleteCycleCount command) => command;
}
