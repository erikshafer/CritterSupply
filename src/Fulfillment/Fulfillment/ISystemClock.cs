namespace Fulfillment;

/// <summary>
/// Abstraction for the system clock to enable time-based testing.
/// Introduced for Slices 26 (lost-in-transit) and 29 (SLA monitoring)
/// which need to simulate elapsed time in integration tests.
/// </summary>
public interface ISystemClock
{
    /// <summary>Gets the current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Default implementation that wraps DateTimeOffset.UtcNow.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
