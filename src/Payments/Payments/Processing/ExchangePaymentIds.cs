using System.Security.Cryptography;
using System.Text;

namespace Payments.Processing;

/// <summary>
/// Deterministic <see cref="Payment"/> stream-id derivations for
/// cross-product exchange flows. See ADR 0062.
///
/// <para>
/// The cross-product exchange "delta capture" path piggybacks on the
/// existing <see cref="Payment"/> aggregate (rather than introducing a
/// separate <c>ExchangePayment</c> aggregate). To get idempotent
/// behaviour under at-least-once redelivery of
/// <c>Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired</c>,
/// the <c>PaymentId</c> for the delta capture is derived deterministically
/// from the <see cref="Guid"/> ReturnId — a UUID-v5 of the ReturnId under
/// a fixed namespace. Two redeliveries of the same request resolve to the
/// same stream id, the second handler invocation finds an existing
/// stream, and re-emits the success reply rather than starting a duplicate
/// stream or double-charging the customer.
/// </para>
/// </summary>
public static class ExchangePaymentIds
{
    /// <summary>
    /// Fixed namespace for derived delta-capture payment ids. Generated
    /// once and immortalized here. Changing it would orphan every existing
    /// delta-capture stream — treat as constant.
    /// </summary>
    private static readonly Guid DeltaCaptureNamespace =
        new("9f7b1d1c-3c1e-4a8b-8b2a-7c6d9e6f1a01");

    /// <summary>
    /// Returns the deterministic <see cref="Payment"/> stream id used for
    /// the cross-product exchange delta capture associated with the
    /// supplied <paramref name="returnId"/>.
    /// </summary>
    public static Guid ComputeDeltaPaymentId(Guid returnId) =>
        UuidV5(DeltaCaptureNamespace, returnId.ToByteArray());

    private static Guid UuidV5(Guid ns, ReadOnlySpan<byte> name)
    {
        // RFC 9562 §5.5 — name-based UUIDv5 over SHA-1.
        Span<byte> nsBytes = stackalloc byte[16];
        if (!ns.TryWriteBytes(nsBytes))
        {
            // Fallback for older runtimes — unreachable on net10.0
            ns.ToByteArray().CopyTo(nsBytes);
        }
        SwapByteOrder(nsBytes);

        var buffer = new byte[nsBytes.Length + name.Length];
        nsBytes.CopyTo(buffer);
        name.CopyTo(buffer.AsSpan(nsBytes.Length));

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(buffer, hash);

        Span<byte> uuid = stackalloc byte[16];
        hash[..16].CopyTo(uuid);

        // Set version (5) and variant (RFC 4122).
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50);
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);

        SwapByteOrder(uuid);
        return new Guid(uuid);
    }

    private static void SwapByteOrder(Span<byte> guid)
    {
        // Convert between Guid's mixed-endian on-wire form and the
        // big-endian form RFC 4122 uses for namespace concatenation.
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
