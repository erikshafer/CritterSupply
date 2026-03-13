namespace Correspondence.Providers;

/// <summary>
/// Sends transactional emails. Returns the provider message ID on success.
/// </summary>
public interface IEmailProvider
{
    Task<ProviderResult> SendEmailAsync(EmailMessage message, CancellationToken ct);
}
