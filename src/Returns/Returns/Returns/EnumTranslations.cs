namespace Returns.Returns;

/// <summary>
/// Provides customer-friendly text translations for internal enum values.
/// This anticorruption layer ensures customers never see technical jargon like "Inspecting".
/// </summary>
public static class EnumTranslations
{
    /// <summary>
    /// Translates ReturnStatus enum to customer-facing text.
    /// </summary>
    /// <param name="status">The internal return status</param>
    /// <param name="shipByDeadline">Optional deadline for Approved status</param>
    /// <returns>Customer-friendly status text</returns>
    public static string ToCustomerFacingText(ReturnStatus status, DateTimeOffset? shipByDeadline = null)
    {
        return status switch
        {
            ReturnStatus.Requested => "Return requested",
            ReturnStatus.Approved => shipByDeadline.HasValue
                ? $"Return approved — ship by {shipByDeadline.Value:MMM dd, yyyy}"
                : "Return approved",
            ReturnStatus.Denied => "Return denied",
            ReturnStatus.LabelGenerated => "Return label generated",
            ReturnStatus.InTransit => "Return in transit",
            ReturnStatus.Received => "Return received",
            ReturnStatus.Inspecting => "Your return is being inspected",
            ReturnStatus.Completed => "Return processed — refund issued",
            ReturnStatus.Rejected => "Return rejected",
            ReturnStatus.Expired => "Return expired",
            _ => status.ToString()
        };
    }

    /// <summary>
    /// Translates ReturnReason enum to customer-facing text.
    /// </summary>
    public static string ToCustomerFacingText(ReturnReason reason)
    {
        return reason switch
        {
            ReturnReason.Defective => "Item was defective or damaged",
            ReturnReason.WrongItem => "Wrong item was shipped",
            ReturnReason.DamagedInTransit => "Item was damaged during shipping",
            ReturnReason.Unwanted => "No longer wanted",
            ReturnReason.Other => "Other reason",
            _ => reason.ToString()
        };
    }

    /// <summary>
    /// Translates DispositionDecision enum to customer-facing text.
    /// </summary>
    public static string ToCustomerFacingText(DispositionDecision disposition)
    {
        return disposition switch
        {
            DispositionDecision.Restockable => "Item will be restocked",
            DispositionDecision.Dispose => "Item cannot be restocked",
            DispositionDecision.Quarantine => "Item requires additional inspection",
            DispositionDecision.ReturnToCustomer => "Item will be returned to you",
            _ => disposition.ToString()
        };
    }

    /// <summary>
    /// Translates ItemCondition enum to customer-facing text.
    /// </summary>
    public static string ToCustomerFacingText(ItemCondition condition)
    {
        return condition switch
        {
            ItemCondition.AsExpected => "Item condition as expected",
            ItemCondition.BetterThanExpected => "Item condition better than expected",
            ItemCondition.WorseThanExpected => "Item condition was worse than reported",
            ItemCondition.NotReceived => "Item not received",
            _ => condition.ToString()
        };
    }
}
