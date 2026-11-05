using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlushedYo.Core.SecretStore;

/// <summary>
/// Starts fixed-command child processes without passing vault or website secrets through the
/// inherited environment. Standard error is drained and discarded rather than logged because a
/// failing native backend can include sensitive input in its diagnostics.
/// </summary>
internal static class ScopedChildProcess
{
    private static readonly string[] UnixInheritedVariableNames =
    [
        "PATH", "HOME", "USER", "LOGNAME", "SHELL", "TMPDIR", "TEMP", "TMP",
        "LANG", "LC_ALL", "DISPLAY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS",
        "XDG_RUNTIME_DIR",
    ];

    private static readonly string[] WindowsInheritedVariableNames =
    [
        "PATH", "SystemRoot", "WINDIR", "COMSPEC", "PATHEXT", "TEMP", "TMP",
        "USERPROFILE", "APPDATA", "LOCALAPPDATA", "PROGRAMDATA",
    ];

    internal static Process Start(
        string command,
        IEnumerable<string> arguments,
        bool redirectStandardInput = false,
        bool redirectStandardOutput = false)
    {
        var startInfo = CreateStartInfo(
            command,
            arguments,
            GetCurrentPlatform(),
            Environment.GetEnvironmentVariable,
            redirectStandardInput,
            redirectStandardOutput);

        try
        {
            return Process.Start(startInfo) ?? throw new Win32Exception("The native process did not start.");
        }
        catch
        {
            startInfo.Environment.Clear();
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string command,
        IEnumerable<string> arguments,
        OSPlatform platform,
        Func<string, string?> environmentReader,
        bool redirectStandardInput = false,
        bool redirectStandardOutput = false)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environmentReader);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        var inheritedNames = platform == OSPlatform.Windows
            ? WindowsInheritedVariableNames
            : UnixInheritedVariableNames;
        foreach (var name in inheritedNames)
        {
            var value = environmentReader(name);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment["NO_COLOR"] = "1";
        return startInfo;
    }

    internal static OSPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OSPlatform.OSX;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? OSPlatform.Linux
            : OSPlatform.Create("UNSUPPORTED");
    }

    internal static async Task<NativeProcessResult> RunForOutputAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = Start(command, arguments, redirectStandardOutput: true);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError);
            return new NativeProcessResult(process.ExitCode, standardOutput.Result);
        }
        catch
        {
            await TerminateAsync(process);
            throw;
        }
    }

    internal static async Task TerminateAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Win32Exception)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // Teardown is bounded. The caller still fails closed and never reuses the session.
        }
    }

    internal sealed record NativeProcessResult(int ExitCode, string StandardOutput);
}
