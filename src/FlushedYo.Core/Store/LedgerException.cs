namespace FlushedYo.Core.Store;

/// <summary>
/// Thrown when the local ledger database cannot be prepared, for example when its schema is newer
/// than this build supports or a migration fails.
/// </summary>
public sealed class LedgerException(string message, Exception? innerException = null)
    : Exception(message, innerException);
