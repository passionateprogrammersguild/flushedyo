using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FlushedYo.Core.Commands;

namespace FlushedYo.Core.PropertyTests.Commands;

/// <summary>
/// Property family for shared CLI flag parsing (spec: cli_shared_building_blocks).
/// Valid flag multisets parse to the accumulating model and are stable under any ordering of the
/// flag groups; structurally invalid token sets always fail with non-null default options and a
/// non-empty error. Exact error-message text stays pinned in <c>CommandLineParserTests</c>.
/// </summary>
public sealed class CommandLineParserProperties
{
    private const string PluginFlag = "--plugin";
    private const string JsonFlag = "--json";

    // Order/repetition invariance: each flag may appear at most once; --plugin and its value are one
    // group, so any ordering of the flag groups is valid and must yield the same options.
    [Property(MaxTest = 100)]
    public Property ValidFlagMultisets_ParseToTheAccumulatingModel_AndAreOrderStable() =>
        Prop.ForAll(ValidTokens().ToArbitrary(), pair =>
        {
            if (!CommandLineParser.TryParse(pair.Tokens, out var options, out var error) || error is not null)
            {
                return false;
            }

            if (options != pair.Expected)
            {
                return false;
            }

            // The other ordering of the same flag groups must produce the identical result.
            var atoms = Atoms(pair.HasJson, pair.HasPlugin, pair.Name);
            var alternate = pair.Reversed ? atoms : atoms.Reverse().ToArray();
            var alternateTokens = alternate.SelectMany(static atom => atom).ToArray();

            return CommandLineParser.TryParse(alternateTokens, out var alternateOptions, out var alternateError)
                && alternateError is null
                && alternateOptions == pair.Expected;
        });

    // Failure classification: generated invalid token sets always fail, return the non-null default
    // options, and report a non-empty error.
    [Property(MaxTest = 100)]
    public Property InvalidTokenSets_AlwaysFailWithDefaultOptionsAndAnError() =>
        Prop.ForAll(InvalidTokens().ToArbitrary(), tokens =>
        {
            var failed = !CommandLineParser.TryParse(tokens, out var options, out var error);
            return failed
                && options == new CommandLineOptions(null, false)
                && !string.IsNullOrWhiteSpace(error);
        });

    /// <summary>
    /// A valid token sequence built from a random subset of the two flag groups in a random order,
    /// paired with the options the accumulating model must yield. The plugin name is a trimmed
    /// variant, mirroring the parser's <c>Trim()</c> of the value.
    /// </summary>
    private static Gen<(string[] Tokens, CommandLineOptions Expected, bool HasJson, bool HasPlugin, string Name, bool Reversed)> ValidTokens() =>
        from hasJson in Gen.Elements(false, true)
        from hasPlugin in Gen.Elements(false, true)
        from name in PluginName()
        from reversed in Gen.Elements(false, true)
        let atoms = Atoms(hasJson, hasPlugin, name)
        let ordered = reversed ? atoms.Reverse().ToArray() : atoms
        let expected = new CommandLineOptions(hasPlugin ? name.Trim() : null, hasJson)
        select (ordered.SelectMany(static atom => atom).ToArray(), expected, hasJson, hasPlugin, name, reversed);

    /// <summary>
    /// An invalid token sequence drawn from the structural failure classes: unknown or wrongly cased
    /// flags, positional text, a dangling/blank/flag-valued <c>--plugin</c>, and repeated flags.
    /// </summary>
    private static Gen<string[]> InvalidTokens() =>
        Gen.Frequency(
            (2, UnknownFlag()),
            (2, WrongCaseFlag()),
            (1, Gen.Constant<string[]>(["extra"])),
            (1, Gen.Constant<string[]>(["--json", "extra"])),
            (1, Gen.Constant<string[]>([PluginFlag])),
            (1, Gen.Constant<string[]>(["--json", PluginFlag])),
            (1, Gen.Constant<string[]>([PluginFlag, ""])),
            (1, Gen.Constant<string[]>([PluginFlag, "   "])),
            (1, Gen.Constant<string[]>([PluginFlag, "--json"])),
            (1, Gen.Constant<string[]>([JsonFlag, JsonFlag])),
            (1, Gen.Constant<string[]>([JsonFlag, PluginFlag, "a", JsonFlag])),
            (1, Gen.Constant<string[]>([PluginFlag, "a", PluginFlag, "b"])));

    /// <summary>A token sequence with an unknown <c>--flag</c>.</summary>
    private static Gen<string[]> UnknownFlag() =>
        Gen.Elements("bogus", "verbose", "output", "help", "pluginx", "jsonx")
            .Select(name => new[] { "--" + name });

    /// <summary>A wrongly cased known flag token.</summary>
    private static Gen<string[]> WrongCaseFlag() =>
        Gen.Elements<string[]>(["--JSON"], ["--Plugin"], ["--Json"]);

    /// <summary>A non-blank plugin name that does not start with <c>--</c>, with optional surrounding whitespace.</summary>
    private static Gen<string> PluginName() =>
        from name in Gen.Elements("fidelity", "fidelity-bank", "schwab", "usaa", "ally", "corner-store")
        from wrap in Gen.Elements("", " ", "  ", "\t", " \t ")
        select wrap + name + wrap;

    /// <summary>The flag groups for a multiset, each group kept adjacent (<c>--plugin</c> consumes its value).</summary>
    private static string[][] Atoms(bool hasJson, bool hasPlugin, string name)
    {
        var atoms = new List<string[]>();
        if (hasPlugin)
        {
            atoms.Add([PluginFlag, name]);
        }

        if (hasJson)
        {
            atoms.Add([JsonFlag]);
        }

        return atoms.ToArray();
    }
}
