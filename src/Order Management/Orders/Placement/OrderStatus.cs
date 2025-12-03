namespace Orders.Placement;

/// <summary>
/// Saga states representing the order lifecycle.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order created, awaiting payment and inventory confirmation.</summary>
    Placed,
    
    /// <summary>Awaiting async payment confirmation.</summary>
    PendingPayment,
    
    /// <summary>Funds captured successfully.</summary>
    PaymentConfirmed,
    
    /// <summary>Payment declined (terminal or retry branch).</summary>
    PaymentFailed,
    
    /// <summary>Flagged for fraud review or inventory issues.</summary>
    OnHold,
    
    /// <summary>Handed off to Fulfillment BC.</summary>
    Fulfilling,
    
    /// <summary>Integration event from Fulfillment.</summary>
    Shipped,
    
    /// <summary>Integration event from Fulfillment.</summary>
    Delivered,
    
    /// <summary>Compensation triggered.</summary>
    Cancelled,
    
    /// <summary>Customer initiated return.</summary>
    ReturnRequested,
    
    /// <summary>Terminal state.</summary>
    Closed
}
