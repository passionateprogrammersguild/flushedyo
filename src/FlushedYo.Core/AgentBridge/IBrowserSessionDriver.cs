namespace FlushedYo.Core.AgentBridge;

/// <summary>
/// Internal browser seam. The provider-facing bridge supplies all policy, snapshot, secret-store,
/// and exclusivity decisions; implementations expose neither raw browser handles nor script execution.
/// </summary>
internal interface IBrowserSessionDriver
{
    bool IsFaulted { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task NavigateAsync(Uri destination, CancellationToken cancellationToken = default);

    Task<BrowserSecuritySnapshot> GetSecuritySnapshotAsync(CancellationToken cancellationToken = default);

    Task SetDocumentNavigationFrozenAsync(bool frozen, CancellationToken cancellationToken = default);

    Task EnterCredentialAsync(
        string cssSelector,
        string secret,
        bool submit,
        Func<CancellationToken, Task> verifySecurityState,
        Func<CancellationToken, Task> verifyAfterSubmit,
        CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    void ForceClose();
}
