namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Configures the isolated browser bridge. The configuration section is
/// <c>FlushedYo:AgentBridge</c>; an empty origin allowlist denies every destination.
/// </summary>
public sealed class AgentBridgeOptions
{
    /// <summary>Gets the exact origins to which browser navigation is permitted.</summary>
    public List<string> AllowedOrigins { get; init; } = [];

    /// <summary>Gets a value indicating whether Chromium runs without a visible window.</summary>
    public bool Headless { get; init; }

    /// <summary>Gets the maximum duration of a committed page navigation.</summary>
    public TimeSpan NavigationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the maximum duration of an individual browser action.</summary>
    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the maximum duration of graceful browser teardown.</summary>
    public TimeSpan TeardownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the longest timeout a caller may request for a manual MFA pause.</summary>
    public TimeSpan MaxMfaTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets an optional Chromium executable path, primarily for controlled test environments.</summary>
    public string? BrowserExecutablePath { get; init; }

    internal void Validate()
    {
        if (AllowedOrigins is null)
        {
            throw new BridgeOperationException("The browser origin allowlist is not configured.");
        }

        ValidatePositive(NavigationTimeout, "navigation timeout");
        ValidatePositive(ActionTimeout, "action timeout");
        ValidatePositive(TeardownTimeout, "teardown timeout");
        ValidatePositive(MaxMfaTimeout, "maximum MFA timeout");
    }

    private static void ValidatePositive(TimeSpan value, string category)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromHours(1))
        {
            throw new BridgeOperationException($"The browser {category} is invalid.");
        }
    }
}
