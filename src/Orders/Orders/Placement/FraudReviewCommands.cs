using FluentValidation;

namespace Orders.Placement;

/// <summary>
/// Command to put an order on hold for manual review (e.g., fraud screening, AVS mismatch,
/// CS investigation). Transitions the saga to <see cref="OrderStatus.OnHold"/> and emits
/// <c>OrderPutOnHold</c> for downstream consumers (Backoffice review queue, Storefront notification).
///
/// Eligibility window (M45.1 / S5): the order must be in a pre-fulfillment status —
/// <c>Placed</c>, <c>PendingPayment</c>, <c>PaymentConfirmed</c>, <c>InventoryReserved</c>.
/// On-hold post-handoff requires Fulfillment-side coordination and is deferred to the Orders
/// remaster per <c>docs/planning/milestones/m45-1-fraud-review-onhold-gap-memo.md</c>.
///
/// Triggering surface: deferred. This command is intentionally not yet exposed via HTTP —
/// the Backoffice review-queue UI is a separate scope. Today the command can be issued
/// programmatically (tests, internal handlers, future fraud-scoring service).
/// </summary>
public sealed record PutOrderOnHold(
    Guid OrderId,
    string Reason,
    string ReviewerId)
{
    public class PutOrderOnHoldValidator : AbstractValidator<PutOrderOnHold>
    {
        public PutOrderOnHoldValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty().WithMessage("Order identifier is required");
            RuleFor(x => x.Reason).NotEmpty().WithMessage("Hold reason is required");
            RuleFor(x => x.ReviewerId).NotEmpty().WithMessage("Reviewer identifier is required");
        }
    }
}

/// <summary>
/// Command to release an order from on-hold review back into normal processing.
/// Transitions the saga back to its pre-hold status (typically <c>PaymentConfirmed</c> or
/// <c>InventoryReserved</c>) and emits <c>OrderReleasedFromHold</c>.
///
/// Today the saga returns to <c>PaymentConfirmed</c> as a safe default. Restoring the exact
/// pre-hold status requires snapshotting it on the saga, which is deferred to the remaster.
/// </summary>
public sealed record ReleaseOrderFromHold(
    Guid OrderId,
    string ReviewerId,
    string? ReleaseNotes = null)
{
    public class ReleaseOrderFromHoldValidator : AbstractValidator<ReleaseOrderFromHold>
    {
        public ReleaseOrderFromHoldValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty().WithMessage("Order identifier is required");
            RuleFor(x => x.ReviewerId).NotEmpty().WithMessage("Reviewer identifier is required");
        }
    }
}

/// <summary>
/// Command to reject an order for fraud after manual review.
/// Transitions the saga to <c>Cancelled</c>, triggers compensation (release inventory, refund
/// captured payment), and emits <c>OrderRejectedForFraud</c> as well as the standard
/// <c>OrderCancelled</c> integration event so downstream BCs (Inventory, Fulfillment, Customer
/// Experience) react identically to any other cancellation.
///
/// Reusing the cancellation compensation path is intentional — fraud rejection is structurally
/// a special-case cancellation. The dedicated <c>OrderRejectedForFraud</c> event lets
/// Customer Experience surface a different message to the customer (and lets Backoffice flag
/// the customer account) without changing the cancellation choreography.
/// </summary>
public sealed record RejectOrderForFraud(
    Guid OrderId,
    string Reason,
    string ReviewerId)
{
    public class RejectOrderForFraudValidator : AbstractValidator<RejectOrderForFraud>
    {
        public RejectOrderForFraudValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty().WithMessage("Order identifier is required");
            RuleFor(x => x.Reason).NotEmpty().WithMessage("Rejection reason is required");
            RuleFor(x => x.ReviewerId).NotEmpty().WithMessage("Reviewer identifier is required");
        }
    }
}
