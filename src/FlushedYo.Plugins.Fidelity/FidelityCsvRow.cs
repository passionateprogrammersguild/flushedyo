using System.Globalization;

namespace FlushedYo.Plugins.Fidelity;

/// <summary>One CSV row with header-based value-free column access.</summary>
internal sealed class FidelityCsvRow(string[] fields, Dictionary<string, int> header)
{
    /// <summary>Raw amount cell text, before sign or currency handling.</summary>
    public string? Amount => Text(FidelityCsvContract.Amount);

    /// <summary>The action cell, or <see langword="null"/> when the cell is blank.</summary>
    public string? Action
    {
        get
        {
            var value = Text(FidelityCsvContract.Action);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>Reads the first parseable date header, in the given preference order.</summary>
    public bool TryGetDate(string[] names, out DateOnly date)
    {
        foreach (var name in names)
        {
            var value = Text(name);
            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, out date))
            {
                return true;
            }
        }

        date = default;
        return false;
    }

    /// <summary>
    /// Reads the magnitude and the explicit sign in the institution's dialect. An unreadable
    /// value is <see cref="AmbiguousReason.MissingAmount"/>; a value with neither a sign nor
    /// parentheses is <see cref="AmbiguousReason.AmbiguousSign"/> rather than a guessed direction.
    /// </summary>
    public AmbiguousReason? TryGetSignedAmount(out decimal amount)
    {
        amount = default;
        if (!TryGetAmount(out var magnitude))
        {
            return AmbiguousReason.MissingAmount;
        }

        if (ResolveSign() is not { } sign)
        {
            return AmbiguousReason.AmbiguousSign;
        }

        amount = magnitude * sign;
        return null;
    }

    /// <summary>Reads a named cell, trimmed, refusing an absent or blank value.</summary>
    public bool TryGetTrimmed(string name, out string value)
    {
        var text = Text(name)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            value = string.Empty;
            return false;
        }

        value = text;
        return true;
    }

    /// <summary>Reads the payee-ish memo from name, description, or symbol, in that order.</summary>
    public bool TryGetMemo(out string memo) =>
        TryGetTrimmed(FidelityCsvContract.Name, out memo) ||
        TryGetTrimmed(FidelityCsvContract.Description, out memo) ||
        TryGetTrimmed(FidelityCsvContract.Symbol, out memo);

    private bool TryGetAmount(out decimal magnitude)
    {
        var digits = Amount?.TrimStart(['$', '+', '-', '(', ' ']).TrimEnd([')', ' ']);
        return decimal.TryParse(
            digits,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out magnitude);
    }

    private decimal? ResolveSign()
    {
        var text = Amount;
        if (text is null)
        {
            return null;
        }

        if (text.Contains('(') || text.Contains('-'))
        {
            return -1m;
        }

        return text.StartsWith('+') || text.Contains('+') ? 1m : null;
    }

    private string? Text(string name) =>
        header.TryGetValue(name, out var index) && index < fields.Length ? fields[index] : null;
}

/// <summary>The validated values read from one row.</summary>
internal readonly record struct RowFields(
    string AccountKey,
    DateOnly Date,
    decimal Amount,
    string Memo,
    string? Action);
