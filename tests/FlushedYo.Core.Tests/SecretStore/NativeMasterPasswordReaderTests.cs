using System.ComponentModel;
using System.Runtime.InteropServices;
using FlushedYo.Core.SecretStore;

namespace FlushedYo.Core.Tests.SecretStore;

public sealed class NativeMasterPasswordReaderTests
{
    private const string SyntheticPassword = "synthetic-only-password";

    [Fact]
    public async Task ReadAsync_MacKeyStore_UsesFixedArgumentsWithoutPassword()
    {
        string? command = null;
        IReadOnlyList<string>? arguments = null;
        var reader = new NativeMasterPasswordReader(
            (actualCommand, actualArguments, _) =>
            {
                command = actualCommand;
                arguments = actualArguments;
                return Task.FromResult(new ScopedChildProcess.NativeProcessResult(0, SyntheticPassword + "\n"));
            },
            OSPlatform.OSX);

        var result = await reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(SyntheticPassword, result);
        Assert.Equal("security", command);
        Assert.Equal(["find-generic-password", "-s", "flushedyo-test", "-a", "test-account", "-w"], arguments);
        Assert.DoesNotContain(SyntheticPassword, arguments!);
    }

    [Fact]
    public async Task ReadAsync_LinuxPrompt_UsesNativeMaskedPrompt()
    {
        string? command = null;
        var reader = new NativeMasterPasswordReader(
            (actualCommand, _, _) =>
            {
                command = actualCommand;
                return Task.FromResult(new ScopedChildProcess.NativeProcessResult(0, SyntheticPassword));
            },
            OSPlatform.Linux);

        var result = await reader.ReadAsync(
            new MasterPasswordRequest { Source = MasterPasswordSource.NativeMaskedPrompt },
            TestContext.Current.CancellationToken);

        Assert.Equal(SyntheticPassword, result);
        Assert.Equal("systemd-ask-password", command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("first\nsecond\n")]
    [InlineData("carriage\rreturn\n")]
    [InlineData("embedded\0null")]
    public async Task ReadAsync_InvalidOutput_FailsClosed(string output)
    {
        var reader = SucceedingReader(output);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("The native master password backend returned invalid output.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsync_OverlongOutput_FailsClosed()
    {
        var reader = SucceedingReader(new string('x', 4097));

        await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_UnsupportedPlatform_DoesNotInvokeBackend()
    {
        var invoked = false;
        var reader = new NativeMasterPasswordReader(
            (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult(new ScopedChildProcess.NativeProcessResult(0, SyntheticPassword));
            },
            OSPlatform.Windows);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken));

        Assert.False(invoked);
        Assert.Contains("No supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_AbsentBackend_RedactsNativeDiagnostics()
    {
        var reader = new NativeMasterPasswordReader(
            (_, _, _) => throw new Win32Exception("missing backend " + SyntheticPassword),
            OSPlatform.OSX);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("The native master password backend is unavailable.", exception.Message);
        Assert.DoesNotContain(SyntheticPassword, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsync_UnexpectedBackendFailure_RedactsAllDetails()
    {
        var reader = new NativeMasterPasswordReader(
            (_, _, _) => throw new InvalidDataException(SyntheticPassword),
            OSPlatform.Linux);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("The native master password backend failed safely.", exception.Message);
        Assert.DoesNotContain(SyntheticPassword, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsync_NonzeroExit_DoesNotReturnOutput()
    {
        var reader = new NativeMasterPasswordReader(
            (_, _, _) => Task.FromResult(new ScopedChildProcess.NativeProcessResult(1, SyntheticPassword)),
            OSPlatform.OSX);

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(KeyStoreRequest(), TestContext.Current.CancellationToken));

        Assert.DoesNotContain(SyntheticPassword, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_CancellationBeforeStart_DoesNotInvokeBackend()
    {
        var invoked = false;
        var reader = new NativeMasterPasswordReader(
            (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult(new ScopedChildProcess.NativeProcessResult(0, SyntheticPassword));
            },
            OSPlatform.OSX);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(KeyStoreRequest(), cancellation.Token));
        Assert.False(invoked);
    }

    [Fact]
    public async Task ReadAsync_CancellationDuringPrompt_PropagatesWithoutFallback()
    {
        var reader = new NativeMasterPasswordReader(
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ScopedChildProcess.NativeProcessResult(0, SyntheticPassword);
            },
            OSPlatform.Linux);
        using var cancellation = new CancellationTokenSource();
        var pending = reader.ReadAsync(
            new MasterPasswordRequest { Source = MasterPasswordSource.NativeMaskedPrompt },
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task ReadAsync_Timeout_FailsClosedAndRedacts()
    {
        var reader = new NativeMasterPasswordReader(
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ScopedChildProcess.NativeProcessResult(0, SyntheticPassword);
            },
            OSPlatform.OSX);
        var request = KeyStoreRequest() with { Timeout = TimeSpan.FromMilliseconds(20) };

        var exception = await Assert.ThrowsAsync<SecretStoreException>(
            () => reader.ReadAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("Timed out waiting for the native master password source.", exception.Message);
        Assert.DoesNotContain(SyntheticPassword, exception.ToString(), StringComparison.Ordinal);
    }

    private static NativeMasterPasswordReader SucceedingReader(string output) => new(
        (_, _, _) => Task.FromResult(new ScopedChildProcess.NativeProcessResult(0, output)),
        OSPlatform.OSX);

    private static MasterPasswordRequest KeyStoreRequest() => new()
    {
        Source = MasterPasswordSource.OsKeyStore,
        Service = "flushedyo-test",
        Account = "test-account",
        Timeout = TimeSpan.FromSeconds(2),
    };
}
