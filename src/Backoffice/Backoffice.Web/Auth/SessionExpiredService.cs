namespace Backoffice.Web.Auth;

/// <summary>
/// Global service for triggering session-expired modal.
/// Pages call TriggerSessionExpired() when they receive 401 responses.
/// The modal component subscribes to OnSessionExpired and shows blocking overlay.
/// </summary>
public sealed class SessionExpiredService
{
    /// <summary>
    /// Event fired when a 401 Unauthorized response is detected.
    /// Subscribers should show a blocking session-expired modal.
    /// </summary>
    public event Action? OnSessionExpired;

    /// <summary>
    /// Trigger session-expired modal globally.
    /// Called by pages when they detect 401 responses from API calls.
    /// </summary>
    public void TriggerSessionExpired()
    {
        OnSessionExpired?.Invoke();
    }
}
