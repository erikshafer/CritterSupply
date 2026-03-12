namespace Returns.Returns;

public sealed record RequestReturn(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<RequestReturnItem> Items);

public sealed record RequestReturnItem(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    ReturnReason Reason,
    string? Explanation = null);

public sealed record ApproveReturn(Guid ReturnId);

public sealed record DenyReturn(Guid ReturnId, string Reason, string Message);

public sealed record ReceiveReturn(Guid ReturnId);

public sealed record StartInspection(Guid ReturnId, string InspectorId);

public sealed record SubmitInspection(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> Results);

public sealed record ExpireReturn(Guid ReturnId);
