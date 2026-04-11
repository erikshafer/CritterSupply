using Inventory.Management;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// HTTP endpoint wiring for quarantine lifecycle operations (Slices 33–35).
/// Handlers, validators, and domain logic live in the domain project.
/// </summary>
public static class QuarantineEndpoints
{
    [WolverinePost("/api/inventory/quarantine")]
    [Authorize(Policy = "WarehouseClerk")]
    public static QuarantineStock PostQuarantine(QuarantineStock command) => command;

    [WolverinePost("/api/inventory/quarantine/release")]
    [Authorize(Policy = "WarehouseClerk")]
    public static ReleaseQuarantine PostRelease(ReleaseQuarantine command) => command;

    [WolverinePost("/api/inventory/quarantine/dispose")]
    [Authorize(Policy = "OperationsManager")]
    public static DisposeQuarantine PostDispose(DisposeQuarantine command) => command;
}
