using FlushedYo.Core.Commands;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>
/// Starting state and exact error-message specs (spec: cli_shared_building_blocks).
/// Order/repetition invariance and failure classification are covered as properties in
/// <c>FlushedYo.Core.PropertyTests.Commands.CommandLineParserProperties</c>.
/// </summary>
public sealed class CommandLineParserTests
{
    [Fact]
    public void NoArguments_YieldsDefaults()
    {
        Assert.True(CommandLineParser.TryParse([], out var options, out var error));

        Assert.Null(error);
        Assert.Null(options.Plugin);
        Assert.False(options.Json);
    }

    public static TheoryData<string[]> PluginArgumentsWithoutAUsableValue => new()
    {
        { ["--plugin"] },
        { ["--plugin", ""] },
        { ["--plugin", "  "] },
        { ["--plugin", "--json"] },
    };

    [Theory]
    [MemberData(nameof(PluginArgumentsWithoutAUsableValue))]
    public void PluginWithoutAUsableValue_Fails(string[] arguments)
    {
        Assert.False(CommandLineParser.TryParse(arguments, out var options, out var error));

        Assert.Equal("Option '--plugin' requires a plugin name.", error);
        Assert.NotNull(options);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--JSON")]
    [InlineData("--Plugin")]
    public void UnknownOrWronglyCasedOption_Fails(string token)
    {
        Assert.False(CommandLineParser.TryParse([token], out _, out var error));

        Assert.Equal($"Unknown option '{token}'.", error);
    }
}
