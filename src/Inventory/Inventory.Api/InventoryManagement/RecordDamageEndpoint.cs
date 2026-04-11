using Inventory.Management;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Inventory.Api.InventoryManagement;

/// <summary>
/// HTTP endpoint wiring for damage recording (Slice 23).
/// Handler and validator live in the domain project.
/// </summary>
public static class RecordDamageEndpoint
{
    [WolverinePost("/api/inventory/record-damage")]
    [Authorize(Policy = "WarehouseClerk")]
    public static RecordDamage Post(RecordDamage command) => command;
}
