namespace Promotions.Promotion;

/// <summary>
/// Strong-typed tag identifier for Promotion event streams in DCB queries.
/// Wraps the Promotion's UUID v7 stream ID.
/// Required because Marten's RegisterTagType needs a value type with a single
/// public property and a constructor — raw Guid doesn't satisfy ValueTypeInfo.
/// </summary>
public sealed record PromotionTag(Guid Value);
