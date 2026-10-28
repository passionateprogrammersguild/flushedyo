namespace FlushedYo.Core.Tests.Tracking;

/// <summary>A <see cref="TimeProvider"/> whose UTC time is set by the test.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
