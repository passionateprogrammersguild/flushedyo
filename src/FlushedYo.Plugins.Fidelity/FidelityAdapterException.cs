namespace FlushedYo.Plugins.Fidelity;

/// <summary>
/// Signals that a Fidelity CSV artifact broke the export contract. Messages are value-free:
/// they name the broken invariant, never a row value, payee, amount, account, or hash.
/// </summary>
public sealed class FidelityAdapterException : Exception
{
    /// <summary>Initializes the exception with a value-free message.</summary>
    public FidelityAdapterException(string message) : base(message)
    {
    }
}
