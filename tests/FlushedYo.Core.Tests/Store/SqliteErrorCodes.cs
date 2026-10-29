namespace FlushedYo.Core.Tests.Store;

/// <summary>SQLite result-code constants from <c>sqlite3.h</c>, encoded as <c>primary | (kind &lt;&lt; 8)</c>.</summary>
internal static class SqliteErrorCodes
{
    /// <summary><c>SQLITE_CONSTRAINT</c> (19): primary constraint-violation code.</summary>
    internal const int Constraint = 19;

    /// <summary><c>SQLITE_CONSTRAINT_UNIQUE</c>: <see cref="Constraint"/> | (8 &lt;&lt; 8).</summary>
    internal const int ConstraintUnique = 2067;

    /// <summary><c>SQLITE_CONSTRAINT_CHECK</c>: <see cref="Constraint"/> | (1 &lt;&lt; 8).</summary>
    internal const int ConstraintCheck = 275;
}
