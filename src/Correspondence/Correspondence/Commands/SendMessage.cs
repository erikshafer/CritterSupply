namespace Correspondence.Commands;

/// <summary>
/// Internal command to send a message via the configured provider.
/// Triggered by integration event handlers or scheduled for retry.
/// </summary>
public sealed record SendMessage(Guid MessageId);
