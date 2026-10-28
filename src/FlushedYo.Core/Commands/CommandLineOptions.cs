namespace FlushedYo.Core.Commands;

/// <summary>Flags shared by the <c>login</c>, <c>download</c>, and <c>status</c> commands.</summary>
/// <param name="Plugin">The <c>--plugin</c> value, or <see langword="null"/> to act on every plugin.</param>
/// <param name="Json">True when <c>--json</c> was passed: write machine-readable output instead of a table.</param>
public sealed record CommandLineOptions(string? Plugin, bool Json);
