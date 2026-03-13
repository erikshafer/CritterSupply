namespace Correspondence.Messages;

public enum MessageStatus
{
    Queued,      // Created, not yet sent
    Delivered,   // Successfully delivered by provider
    Failed,      // Permanently failed after max retries
    Skipped      // Not sent (customer opted out, or channel disabled)
}
