using System.Globalization;
using System.Text.Json;
using FlushedYo.Core.Commands;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>JSON conventions shared by all commands (spec: cli_shared_building_blocks).</summary>
public sealed class CliJsonTests
{
    private enum Status
    {
        Succeeded,
        NotRunnable,
    }

    private sealed record Sample(string Name, string? Detail, Status Status, DateTimeOffset At, int? Count);

    private static readonly Sample Value = new("fidelity", null, Status.NotRunnable, new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), null);

    [Fact]
    public void NullValues_AreWrittenExplicitly()
    {
        using var document = JsonDocument.Parse(CliJson.Serialize(Value));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("detail").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("count").ValueKind);
    }

    [Fact]
    public void PropertyNames_AreCamelCase()
    {
        using var document = JsonDocument.Parse(CliJson.Serialize(Value));

        Assert.Equal(
            ["name", "detail", "status", "at", "count"],
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());
    }

    [Fact]
    public void Enums_AreCamelCaseStrings()
    {
        using var document = JsonDocument.Parse(CliJson.Serialize(Value));

        Assert.Equal("notRunnable", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void UtcTimestamps_RoundTripAsTheSameInstant()
    {
        using var document = JsonDocument.Parse(CliJson.Serialize(Value));

        var parsed = DateTimeOffset.Parse(
            document.RootElement.GetProperty("at").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        Assert.Equal(Value.At.UtcDateTime, parsed.UtcDateTime);
    }

    [Fact]
    public void Output_IsIndented_UsesLf_AndHasNoTrailingNewline()
    {
        var json = CliJson.Serialize(Value);

        Assert.Contains('\n', json);
        Assert.DoesNotContain('\r', json);
        Assert.False(json.EndsWith('\n'));
    }
}
