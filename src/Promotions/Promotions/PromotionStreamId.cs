namespace Promotions;

/// <summary>
/// Strong-typed tag identifier for Promotion event streams.
/// Required by Marten's DCB tag registration (Guid has 2 public properties in .NET 10
/// and cannot be used directly as a tag type).
/// Registered via: opts.Events.RegisterTagType&lt;PromotionStreamId&gt;("promotion").ForAggregate&lt;Promotion.Promotion&gt;()
/// </summary>
public sealed record PromotionStreamId(Guid Value);
