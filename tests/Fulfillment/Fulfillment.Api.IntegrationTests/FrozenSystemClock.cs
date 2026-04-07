using Fulfillment;

namespace Fulfillment.Api.IntegrationTests;

/// <summary>
/// Test stub for ISystemClock that allows advancing time to simulate elapsed time.
/// Used for Slices 26 (lost-in-transit) and 29 (SLA monitoring) tests.
/// </summary>
public sealed class FrozenSystemClock : ISystemClock
{
    private DateTimeOffset _now;

    public FrozenSystemClock()
    {
        _now = DateTimeOffset.UtcNow;
    }

    public FrozenSystemClock(DateTimeOffset initialTime)
    {
        _now = initialTime;
    }

    public DateTimeOffset UtcNow => _now;

    /// <summary>Advances the clock by the specified duration.</summary>
    public void Advance(TimeSpan duration) => _now = _now.Add(duration);

    /// <summary>Sets the clock to a specific point in time.</summary>
    public void SetUtcNow(DateTimeOffset value) => _now = value;
}
