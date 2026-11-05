namespace FlushedYo.Core.AgentBridge;

/// <summary>Base class for value-free browser bridge failures.</summary>
public abstract class BridgeException : Exception
{
    private protected BridgeException(string message)
        : base(message)
    {
    }
}

/// <summary>Indicates that a browser origin or navigation violates provider policy.</summary>
public sealed class BridgePolicyException : BridgeException
{
    internal BridgePolicyException(string message)
        : base(message)
    {
    }
}

/// <summary>Indicates absent, ambiguous, unstable, or changed browser security state.</summary>
public sealed class BridgeSnapshotException : BridgeException
{
    internal BridgeSnapshotException(string message)
        : base(message)
    {
    }
}

/// <summary>Indicates that a bounded manual MFA pause could not complete safely.</summary>
public sealed class BridgeMfaException : BridgeException
{
    internal BridgeMfaException(string message)
        : base(message)
    {
    }
}

/// <summary>Indicates a value-free browser operation or teardown failure.</summary>
public sealed class BridgeOperationException : BridgeException
{
    internal BridgeOperationException(string message)
        : base(message)
    {
    }
}
