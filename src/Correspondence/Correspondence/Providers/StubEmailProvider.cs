namespace Correspondence.Providers;

/// <summary>
/// Stub implementation of IEmailProvider for development and testing.
/// Simulates SendGrid behavior without making real API calls.
/// </summary>
public sealed class StubEmailProvider : IEmailProvider
{
    private readonly Dictionary<string, bool> _failureSimulation = new();

    public Task<ProviderResult> SendEmailAsync(EmailMessage message, CancellationToken ct)
    {
        // Simulate SendGrid 202 Accepted response with X-Message-Id
        var providerId = $"sendgrid-{Guid.NewGuid():N}.filter001";

        // Check if we should simulate a failure for this email
        if (_failureSimulation.TryGetValue(message.ToEmail, out var shouldFail) && shouldFail)
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: "Simulated SendGrid 500 error",
                IsRetriable: true
            ));
        }

        // Simulate successful send
        return Task.FromResult(new ProviderResult(
            Success: true,
            ProviderId: providerId,
            FailureReason: null,
            IsRetriable: false
        ));
    }

    // Test helper: configure failure simulation for specific email
    public void SimulateFailureFor(string email)
    {
        _failureSimulation[email] = true;
    }

    // Test helper: clear failure simulation
    public void ClearFailureSimulation()
    {
        _failureSimulation.Clear();
    }
}
