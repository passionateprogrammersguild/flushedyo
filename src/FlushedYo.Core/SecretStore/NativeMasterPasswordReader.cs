using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FlushedYo.Core.SecretStore;

/// <summary>
/// Reads master passwords through the macOS Keychain/<c>osascript</c> hidden prompt or Linux
/// <c>secret-tool</c>/<c>systemd-ask-password</c>. Other platforms fail closed. The password is
/// captured only in process memory; it is never an argument, environment variable, log, or file.
/// The OS keystore and brokered prompt are trusted only against other users: malware running as
/// the same OS user can generally read that user's keystore items or intercept its processes.
/// </summary>
public sealed class NativeMasterPasswordReader : IMasterPasswordReader
{
    private const int MaximumPasswordLength = 4096;
    private readonly NativeProcessHandler _processHandler;
    private readonly OSPlatform _platform;

    /// <summary>Creates a reader using the current operating system's native backends.</summary>
    public NativeMasterPasswordReader()
        : this(ScopedChildProcess.RunForOutputAsync, ScopedChildProcess.GetCurrentPlatform())
    {
    }

    internal NativeMasterPasswordReader(NativeProcessHandler processHandler, OSPlatform platform)
    {
        _processHandler = processHandler ?? throw new ArgumentNullException(nameof(processHandler));
        _platform = platform;
    }

    /// <inheritdoc />
    public async Task<string> ReadAsync(MasterPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new SecretStoreException("The master password timeout must be positive.");
        }

        var invocation = GetInvocation(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        try
        {
            var result = await _processHandler(invocation.Command, invocation.Arguments, timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
            {
                throw new SecretStoreException("The master password backend declined or could not complete the request.");
            }

            return ValidateOutput(result.StandardOutput);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreException("Timed out waiting for the native master password source.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretStoreException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            throw new SecretStoreException("The native master password backend is unavailable.");
        }
        catch (Exception)
        {
            // Native diagnostics are deliberately not attached as an inner exception: a backend can
            // echo its input, and exception messages are commonly logged by hosts.
            throw new SecretStoreException("The native master password backend failed safely.");
        }
    }

    private NativeProcessInvocation GetInvocation(MasterPasswordRequest request)
    {
        if (_platform != OSPlatform.OSX && _platform != OSPlatform.Linux)
        {
            throw new SecretStoreException("No supported native master password backend exists on this platform.");
        }

        if (request.Source == MasterPasswordSource.NativeMaskedPrompt)
        {
            return _platform == OSPlatform.OSX
                ? new NativeProcessInvocation(
                    "osascript",
                    ["-e", "set dialogResult to display dialog \"flushedyo master password:\" default answer \"\" with hidden answer with title \"flushedyo\"\nreturn text returned of dialogResult"])
                : new NativeProcessInvocation("systemd-ask-password", ["flushedyo master password:"]);
        }

        if (request.Source != MasterPasswordSource.OsKeyStore ||
            string.IsNullOrWhiteSpace(request.Service) ||
            string.IsNullOrWhiteSpace(request.Account))
        {
            throw new SecretStoreException("An explicit keystore source, service, and account are required.");
        }

        return _platform == OSPlatform.OSX
            ? new NativeProcessInvocation(
                "security",
                ["find-generic-password", "-s", request.Service, "-a", request.Account, "-w"])
            : new NativeProcessInvocation(
                "secret-tool",
                ["lookup", "service", request.Service, "account", request.Account]);
    }

    private static string ValidateOutput(string output)
    {
        var password = output;
        if (password.EndsWith('\n'))
        {
            password = password[..^1];
            if (password.EndsWith('\r'))
            {
                password = password[..^1];
            }
        }

        if (password.Length is 0 or > MaximumPasswordLength ||
            password.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new SecretStoreException("The native master password backend returned invalid output.");
        }

        return password;
    }

    internal delegate Task<ScopedChildProcess.NativeProcessResult> NativeProcessHandler(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);

    private sealed record NativeProcessInvocation(string Command, IReadOnlyList<string> Arguments);
}
