namespace Messages.Contracts.Inventory;

/// <summary>
/// M47.0 / Slice 4 — request published by the Returns BC when a
/// cross-product exchange is cancelled (payment-capture failure) or
/// rejected (inspection failure) and the held replacement-stock
/// reservation should be released back to the available pool. Closes
/// the carry-forward in <c>docs/planning/milestones/m47-0-plan.md</c>:
/// "Releases the Slice 1 replacement reservation in the rejection /
/// cancellation paths".
///
/// <para>
/// <see cref="ReservationId"/> is the same key the Returns BC used when
/// requesting the original reservation via
/// <see cref="ReserveReplacementForExchange"/> (i.e. the <c>ReturnId</c>
/// per ADR 0061). <see cref="InventoryId"/> is the deterministic
/// Inventory stream id captured by the Returns BC from
/// <see cref="ReplacementReserved"/>.
/// </para>
///
/// <para>
/// Idempotency: the receiving handler is a no-op when the reservation
/// is no longer present (already released by either the explicit
/// release path or the ExpireReservation timer).
/// </para>
/// </summary>
public sealed record ReleaseExchangeReservation(
    Guid InventoryId,
    Guid ReservationId,
    string Reason);
