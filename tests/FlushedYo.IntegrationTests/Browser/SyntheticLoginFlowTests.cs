using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FlushedYo.Core.AgentBridge;
using FlushedYo.Core.SecretStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlushedYo.IntegrationTests.Browser;

[Collection("RealBrowser")]
public sealed class SyntheticLoginFlowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NavigateSnapshotEnterSubmit_AndPostLoginOriginAreVerified()
    {
        BrowserTestEnvironment.RequireChromium();
        await using var site = new SyntheticSite();
        var submitted = site.SignalWhenRequested("/submit");
        site.MapHtml("/login", """
            <!doctype html><title>Synthetic login</title>
            <form id="login" method="post" action="/submit">
              <input id="secret" name="secret" type="password"><button type="submit">Sign in</button>
            </form>
            """);
        site.MapHtml("/submit", "<!doctype html><title>Submission received</title>");
        site.MapHtml("/post-login", "<!doctype html><title>Post login</title><p>Authenticated synthetic session</p>");
        await using var bridge = site.CreateBridge();

        await bridge.NavigateAsync(site.Url("/login"), Token);
        var snapshot = await bridge.GetSecuritySnapshotAsync(Token);
        try
        {
            await bridge.SubmitCredentialAsync(
                SubmitCredentialArgs.Create("synthetic/password", "#secret", snapshot), Token);
        }
        catch (BridgeException)
        {
            Assert.Fail($"Synthetic submission failed; requests={site.RequestCounts.Count}, parsed={site.FormSecrets.Count}, submitted={submitted.IsCompleted}.");
        }

        await submitted.WaitAsync(Token);
        await bridge.NavigateAsync(site.Url("/post-login"), Token);
        var postLogin = await bridge.GetSecuritySnapshotAsync(Token);

        Assert.Equal(BrowserTestEnvironment.SecretValue, Assert.Single(site.FormSecrets));
        Assert.Equal($"http://127.0.0.1:{site.Port}", postLogin.Origin);
        Assert.DoesNotContain(BrowserTestEnvironment.SecretValue, snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(BrowserTestEnvironment.SecretValue, postLogin.ToString(), StringComparison.Ordinal);
    }
}

internal static class BrowserTestEnvironment
{
    internal const string SecretValue = "SYNTHETIC-REAL-BROWSER-SECRET";

    internal static string RequireChromium()
    {
        var path = FindChromium();
        Assert.SkipWhen(path is null,
            "Chromium is unavailable. Install Playwright Chromium or set FLUSHEDYO_TEST_CHROMIUM_EXECUTABLE to an existing executable.");
        return path!;
    }

    private static string? FindChromium()
    {
        var overridePath = Environment.GetEnvironmentVariable("FLUSHEDYO_TEST_CHROMIUM_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return File.Exists(overridePath) ? overridePath : null;
        }

        var roots = new List<string>();
        var configuredRoot = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            roots.Add(configuredRoot);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        roots.Add(Path.Combine(home, "Library", "Caches", "ms-playwright"));
        roots.Add(Path.Combine(home, ".cache", "ms-playwright"));

        var relativeExecutables = new[]
        {
            Path.Combine("chrome-mac-x64", "Google Chrome for Testing.app", "Contents", "MacOS", "Google Chrome for Testing"),
            Path.Combine("chrome-mac", "Chromium.app", "Contents", "MacOS", "Chromium"),
            Path.Combine("chrome-mac", "Google Chrome for Testing.app", "Contents", "MacOS", "Google Chrome for Testing"),
            Path.Combine("chrome-linux64", "chrome"),
            Path.Combine("chrome-linux", "chrome"),
            Path.Combine("chrome-win64", "chrome.exe"),
            Path.Combine("chrome-win", "chrome.exe"),
        };

        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.Ordinal))
        {
            var found = FindChromiumInRoot(root, relativeExecutables);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string? FindChromiumInRoot(string root, string[] relativeExecutables)
    {
        foreach (var installation in Directory.EnumerateDirectories(root, "chromium-*").OrderByDescending(path => path, StringComparer.Ordinal))
        {
            foreach (var relative in relativeExecutables)
            {
                var candidate = Path.Combine(installation, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

internal sealed class SyntheticSite : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, Func<HttpListenerContext, Task>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _signals = new(StringComparer.Ordinal);
    private readonly Task _listenLoop;

    internal SyntheticSite()
    {
        Port = ReserveLoopbackPort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _listenLoop = ListenAsync();
    }

    internal int Port { get; }

    internal ConcurrentQueue<string> FormSecrets { get; } = new();

    internal ConcurrentDictionary<string, int> RequestCounts { get; } = new(StringComparer.Ordinal);

    internal Uri Url(string path) => new($"http://127.0.0.1:{Port}{path}");

    internal Uri LocalhostUrl(string path) => new($"http://localhost:{Port}{path}");

    internal TrustedBrowserBridge CreateBridge(bool headed = false) => new(
        new AgentBridgeOptions
        {
            AllowedOrigins = [$"http://127.0.0.1:{Port}", $"http://localhost:{Port}"],
            Headless = !headed,
            NavigationTimeout = TimeSpan.FromSeconds(15),
            ActionTimeout = TimeSpan.FromSeconds(10),
            TeardownTimeout = TimeSpan.FromSeconds(15),
            MaxMfaTimeout = TimeSpan.FromSeconds(3),
            BrowserExecutablePath = BrowserTestEnvironment.RequireChromium(),
            AllowBrowserExecutableOverride = true,
        },
        new InMemorySecretStore(new Dictionary<string, string>
        {
            ["synthetic/password"] = BrowserTestEnvironment.SecretValue,
        }),
        NullLogger<TrustedBrowserBridge>.Instance);

    internal void MapHtml(string path, string html) => _handlers[path] = async context =>
    {
        if (context.Request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(_lifetime.Token);
            var secret = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(parts => parts.Length == 2 && parts[0] == "secret")
                .Select(parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')))
                .SingleOrDefault();
            if (secret is not null)
            {
                FormSecrets.Enqueue(secret);
            }
        }

        await WriteHtmlAsync(context, html);
    };

    internal TaskCompletionSource MapGate(string path)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handlers[path] = async context =>
        {
            await gate.Task.WaitAsync(_lifetime.Token);
            await WriteHtmlAsync(context, "ok");
        };
        return gate;
    }

    /// <summary>Maps a synthetic export endpoint that answers with an attachment.</summary>
    internal void MapDownload(string path, DownloadResponse response) => _handlers[path] = async context =>
    {
        var bytes = Encoding.UTF8.GetBytes(response.Content);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/csv";
        context.Response.AddHeader("Content-Disposition", $"attachment; filename={response.FileName}");
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, _lifetime.Token);
        context.Response.Close();
    };

    internal void MapRedirect(string path, Uri destination) => _handlers[path] = context =>
    {
        context.Response.StatusCode = (int)HttpStatusCode.Redirect;
        context.Response.RedirectLocation = destination.AbsoluteUri;
        context.Response.Close();
        return Task.CompletedTask;
    };

    internal Task SignalWhenRequested(string path)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _signals[path] = signal;
        return signal.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _listener.Stop();
        try
        {
            await _listenLoop;
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _lifetime.Dispose();
        _listener.Close();
    }

    private async Task ListenAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (await ProcessRequestAsync(context))
            {
                return;
            }
        }
    }

    private async Task<bool> ProcessRequestAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        RequestCounts.AddOrUpdate(path, 1, (_, count) => count + 1);
        if (_signals.TryRemove(path, out var signal))
        {
            signal.TrySetResult();
        }

        if (_handlers.TryGetValue(path, out var handler))
        {
            try
            {
                await handler(context);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return true;
            }
            catch (HttpListenerException) when (_lifetime.IsCancellationRequested)
            {
                return true;
            }
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.Close();
        }

        return false;
    }

    private async Task WriteHtmlAsync(HttpListenerContext context, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, _lifetime.Token);
        context.Response.Close();
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>One synthetic attachment: the browser-suggested file name and the bytes served.</summary>
internal readonly record struct DownloadResponse(string FileName, string Content);

internal sealed class InMemorySecretStore(IReadOnlyDictionary<string, string> secrets) : ISecretStore
{
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(secrets.TryGetValue(key, out var value) ? value : null);
    }

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(secrets.ContainsKey(key));
    }
}
