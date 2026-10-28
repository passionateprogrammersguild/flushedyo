using System.Globalization;
using FlushedYo.Core.Models;
using FlushedYo.Core.Tracking;

namespace FlushedYo.Core.Tests.Tracking;

/// <summary>
/// Culture independence of the canonical form. Joins the environment-mutation collection because
/// <see cref="CultureInfo.CurrentCulture"/> is process-global state.
/// </summary>
[Collection("EnvironmentMutation")]
public sealed class CanonicalTransactionHasherCultureTests
{
    private const string Expected = "fidelity|abc-123|-1234.50|2026-01-02|CORNER STORE";

    private static Transaction Tx() => new()
    {
        Institution = "fidelity",
        ExternalId = "abc-123",
        Amount = -1234.50m,
        Date = new DateOnly(2026, 1, 2),
        Payee = "Corner Store",
        Type = "debit",
    };

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void Canonicalize_IgnoresCurrentCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal(Expected, CanonicalTransactionHasher.Canonicalize(Tx()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ComputeHash_IsTheSameUnderDifferentCultures()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = CanonicalTransactionHasher.ComputeHash(Tx());

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var german = CanonicalTransactionHasher.ComputeHash(Tx());

            Assert.Equal(invariant, german);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
