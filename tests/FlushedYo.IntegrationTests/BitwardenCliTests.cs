namespace FlushedYo.IntegrationTests;

public sealed class BitwardenCliTests
{
    [Theory]
    [InlineData("Not found.")]
    [InlineData("error: Not Found")]
    public void ClassifyReadFailure_DetectsMissingItem(string errorText)
    {
        Assert.Equal(BitwardenCli.ReadFailureKind.NotFound, BitwardenCli.ClassifyReadFailure(errorText));
    }

    [Theory]
    [InlineData("Vault is locked.")]
    [InlineData("Invalid session key.")]
    [InlineData("You are not logged in.")]
    [InlineData("? Master password:")]
    public void ClassifyReadFailure_DetectsUnavailableVault(string errorText)
    {
        Assert.Equal(BitwardenCli.ReadFailureKind.VaultUnavailable, BitwardenCli.ClassifyReadFailure(errorText));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unable to reach the server.")]
    public void ClassifyReadFailure_FallsBackToUnknown(string errorText)
    {
        Assert.Equal(BitwardenCli.ReadFailureKind.Unknown, BitwardenCli.ClassifyReadFailure(errorText));
    }
}
