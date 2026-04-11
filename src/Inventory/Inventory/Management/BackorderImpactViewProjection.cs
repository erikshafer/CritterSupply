using Marten.Events.Projections;

namespace Inventory.Management;

/// <summary>
/// Async multi-stream projection keyed by SKU that tracks backorder impact.
/// Reacts to BackorderRegistered and BackorderCleared events.
/// Registered as Async — dashboard read model.
/// </summary>
public class BackorderImpactViewProjection : MultiStreamProjection<BackorderImpactView, string>
{
    public BackorderImpactViewProjection()
    {
        Identity<BackorderRegistered>(e => e.Sku);
        Identity<BackorderCleared>(e => e.Sku);
    }

    public void Apply(BackorderImpactView view, BackorderRegistered e)
    {
        view.Sku = e.Sku;
        view.Id = e.Sku;
        view.LastBackorderAt = e.RegisteredAt;

        if (!view.AffectedWarehouses.Contains(e.WarehouseId))
        {
            view.AffectedWarehouses.Add(e.WarehouseId);
            view.ActiveBackorderCount = view.AffectedWarehouses.Count;
        }
    }

    public void Apply(BackorderImpactView view, BackorderCleared e)
    {
        view.LastClearedAt = e.ClearedAt;

        if (view.AffectedWarehouses.Contains(e.WarehouseId))
        {
            view.AffectedWarehouses.Remove(e.WarehouseId);
            view.ActiveBackorderCount = view.AffectedWarehouses.Count;
        }
    }
}
