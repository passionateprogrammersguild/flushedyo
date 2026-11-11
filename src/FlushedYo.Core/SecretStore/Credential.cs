namespace FlushedYo.Core.SecretStore;

/// <summary>A username and password retrieved from one secret-store item.</summary>
/// <param name="Username">The login username.</param>
/// <param name="Password">The login password.</param>
public sealed record Credential(string Username, string Password)
{
    /// <summary>Returns a representation that masks both credential values.</summary>
    public override string ToString() => "Credential { Username = ********, Password = ******** }";
}
