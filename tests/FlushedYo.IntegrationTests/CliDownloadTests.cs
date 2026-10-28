using System.Text.Json;

namespace FlushedYo.IntegrationTests;

/// <summary>End-to-end <c>download</c> tests against the built CLI with stub plugins.</summary>
public sealed class CliDownloadTests
{
    private static readonly TimeSpan PerPluginTimeout = TimeSpan.FromSeconds(15);

    private static async Task<PluginResult[]> ParseDownloadJsonAsync(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        return root.GetProperty("plugins").EnumerateArray().Select(plugin => new PluginResult
        {
            Plugin = plugin.GetProperty("plugin").GetString()!,
            Status = plugin.GetProperty("status").GetString()!,
            Inserted = plugin.TryGetProperty("inserted", out var ins) && ins.ValueKind != JsonValueKind.Null ? ins.GetInt32() : (int?)null,
            Skipped = plugin.TryGetProperty("skipped", out var skp) && skp.ValueKind != JsonValueKind.Null ? skp.GetInt32() : (int?)null,
            Rejected = plugin.TryGetProperty("rejected", out var rej) && rej.ValueKind != JsonValueKind.Null ? rej.GetInt32() : (int?)null,
            Detail = plugin.TryGetProperty("detail", out var det) && det.ValueKind != JsonValueKind.Null ? det.GetString() : null,
        }).ToArray();
    }

    [Fact]
    public async Task FirstRun_Stores_Transactions_SecondRun_Skips()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        // First run: 3 inserted, 0 skipped.
        var first = await workspace.RunAsync(["download", "--json"]);
        Assert.Equal(0, first.ExitCode);
        if (first.ExitCode != 0) { System.IO.File.WriteAllText("/tmp/debug_full.txt", $"EXIT: {first.ExitCode}\nSTDOUT:{first.StdOut}\nSTDERR:{first.StdErr}"); }
        var results = await ParseDownloadJsonAsync(first.StdOut);
        var fidelity = Assert.Single(results);
        Assert.Equal("succeeded", fidelity.Status);
        Assert.Equal(3, fidelity.Inserted);
        Assert.Equal(0, fidelity.Skipped);
        Assert.Equal(0, fidelity.Rejected);
        Assert.Empty(first.StdErr);

        // Second run: 0 inserted, 3 skipped. A third behaves the same.
        var second = await workspace.RunAsync(["download", "--json"]);
        Assert.Equal(0, second.ExitCode);
        results = await ParseDownloadJsonAsync(second.StdOut);
        fidelity = Assert.Single(results);
        Assert.Equal("succeeded", fidelity.Status);
        Assert.Equal(0, fidelity.Inserted);
        Assert.Equal(3, fidelity.Skipped);

        var third = await workspace.RunAsync(["download", "--json"]);
        Assert.Equal(0, third.ExitCode);
        results = await ParseDownloadJsonAsync(third.StdOut);
        fidelity = Assert.Single(results);
        Assert.Equal(0, fidelity.Inserted);
        Assert.Equal(3, fidelity.Skipped);
    }

    [Fact]
    public async Task NoSecretStoreContact_WhenPluginDoesNotAsk_ForSecrets()
    {
        // Default provider stays BitwardenMcp (the production default). The stub plugin below has
        // no secretKey, so SecretStore is never called and no MCP/npx process starts.
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StdErr); // no MCP startup, no errors
    }

    [Fact]
    public async Task TableOutput_HasHeadersAndRow()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["download"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("PLUGIN", result.StdOut);
        Assert.Contains("STATUS", result.StdOut);
        Assert.Contains("INSERTED", result.StdOut);
        Assert.Contains("SKIPPED", result.StdOut);
        Assert.Contains("REJECTED", result.StdOut);
        Assert.Contains("DETAIL", result.StdOut);
        Assert.Contains("fidelity", result.StdOut);
        Assert.Contains("succeeded", result.StdOut);
        Assert.Empty(result.StdErr);
    }

    [Fact]
    public async Task InstitutionMismatches_Are_Rejected_And_Counted()
    {
        using var workspace = new CliWorkspace();
        var stub = $$"""
        {
          "transactions": [
            { "externalId": "tx-0", "amount": -0.50, "date": "2026-01-15", "payee": "Payee 0", "type": "debit" },
            { "institution": "other", "externalId": "tx-1", "amount": -1.50, "date": "2026-01-15", "payee": "Payee 1", "type": "debit" },
            { "institution": "other", "externalId": "tx-2", "amount": -2.50, "date": "2026-01-15", "payee": "Payee 2", "type": "debit" }
          ]
        }
        """;
        workspace.AddStubPlugin("fidelity", stubJson: stub);

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(0, result.ExitCode);
        var results = await ParseDownloadJsonAsync(result.StdOut);
        var fidelity = Assert.Single(results);
        Assert.Equal(1, fidelity.Inserted);
        Assert.Equal(2, fidelity.Rejected);
        Assert.DoesNotContain("Payee", result.StdOut + result.StdErr); // no payee values in output

        // Status shows storedTransactions == 1.
        var status = await workspace.RunAsync(["status", "--json"]);
        Assert.Equal(0, status.ExitCode);
        using var doc = JsonDocument.Parse(status.StdOut);
        var stored = doc.RootElement.GetProperty("plugins")[0].GetProperty("storedTransactions").GetInt64();
        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task FailureIsolation_OneFails_OthersStillStore()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: """{ "throw": "SECRET-IN-MESSAGE" }""");
        workspace.AddStubPlugin("bravo", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(2, result.ExitCode);
        var results = await ParseDownloadJsonAsync(result.StdOut);
        var alpha = results.First(r => r.Plugin == "alpha");
        var bravo = results.First(r => r.Plugin == "bravo");
        Assert.Equal("failed", alpha.Status);
        Assert.Equal("Plugin threw InvalidOperationException.", alpha.Detail);
        Assert.Equal("succeeded", bravo.Status);
        Assert.Equal(3, bravo.Inserted);

        Assert.DoesNotContain("SECRET-IN-MESSAGE", result.StdOut + result.StdErr);

        // Follow-up status confirms bravo stored 3 and alpha's last run failed.
        var status = await workspace.RunAsync(["status", "--json"]);
        Assert.Equal(0, status.ExitCode);
        using var doc = JsonDocument.Parse(status.StdOut);
        var plugins = doc.RootElement.GetProperty("plugins").EnumerateArray().ToArray();
        var statusAlpha = plugins.First(p => p.GetProperty("plugin").GetString() == "alpha");
        var statusBravo = plugins.First(p => p.GetProperty("plugin").GetString() == "bravo");
        Assert.Equal("failed", statusAlpha.GetProperty("lastRun").GetProperty("outcome").GetString());
        Assert.Equal(3, statusBravo.GetProperty("storedTransactions").GetInt64());
    }

    [Fact]
    public async Task DashPlugin_Runs_Only_That_Plugin()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));
        workspace.AddStubPlugin("bravo", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["download", "--plugin", "bravo", "--json"]);

        Assert.Equal(0, result.ExitCode);
        var results = await ParseDownloadJsonAsync(result.StdOut);
        var plugins = results.Select(r => r.Plugin).ToArray();
        Assert.Contains("bravo", plugins);
        Assert.DoesNotContain("alpha", plugins);

        var status = await workspace.RunAsync(["status", "--json"]);
        using var doc = JsonDocument.Parse(status.StdOut);
        var lastRuns = doc.RootElement.GetProperty("plugins")
            .EnumerateArray()
            .Where(p => p.GetProperty("plugin").GetString() == "bravo")
            .Select(p => p.GetProperty("lastRun"))
            .ToArray();
        Assert.Single(lastRuns);
        Assert.Equal(3, lastRuns[0].GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task UnknownPlugin_ExitsOne_NoLedgerCreated()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));

        var result = await workspace.RunAsync(["download", "--plugin", "nope", "--json"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("was not found", result.StdErr);
        Assert.False(workspace.LedgerExists);
    }

    [Fact]
    public async Task DescriptorOnlyPlugin_ReportedButDoesNotBlock()
    {
        using var workspace = new CliWorkspace();
        workspace.AddDescriptorOnlyPlugin("notes");
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        var allResult = await workspace.RunAsync(["download", "--json"]);
        Assert.Equal(0, allResult.ExitCode); // descriptor-only is not-runnable, not a failure
        var results = await ParseDownloadJsonAsync(allResult.StdOut);
        var notes = results.First(r => r.Plugin == "notes");
        Assert.Equal("notRunnable", notes.Status);
        Assert.Equal("declares no executable assembly", notes.Detail);

        // With --plugin it is a usage error (exit 1).
        var targeted = await workspace.RunAsync(["download", "--plugin", "notes"]);
        Assert.Equal(1, targeted.ExitCode);
        Assert.Contains("cannot be downloaded", targeted.StdErr);
    }

    [Fact]
    public async Task NewFolder_PickedUp_WithoutRebuild_ConcernA()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));

        var first = await workspace.RunAsync(["download", "--json"]);
        Assert.Single(await ParseDownloadJsonAsync(first.StdOut));

        // Add a new plugin folder only — no rebuild, no code change.
        workspace.AddStubPlugin("bravo", stubJson: StubPluginFixture.TransactionsJson(3));

        var second = await workspace.RunAsync(["download", "--json"]);
        Assert.Equal(0, second.ExitCode);
        var results = await ParseDownloadJsonAsync(second.StdOut);
        Assert.Equal(2, results.Length);
        Assert.Contains(results, r => r.Plugin == "alpha");
        Assert.Contains(results, r => r.Plugin == "bravo");
        // alpha's transactions were already stored in the first run → skipped, not inserted.
        var alpha = results.First(r => r.Plugin == "alpha");
        Assert.Equal(0, alpha.Inserted);
        Assert.Equal(3, alpha.Skipped);
        var bravo = results.First(r => r.Plugin == "bravo");
        Assert.Equal(3, bravo.Inserted);
    }

    [Fact]
    public async Task PluginLoadFailure_Is_APluginFailure()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("alpha", stubJson: StubPluginFixture.TransactionsJson(3));
        // type is not a real type in the stub assembly → load failure.
        workspace.AddStubPlugin("broken", type: "Nope.Missing");

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(2, result.ExitCode);
        var results = await ParseDownloadJsonAsync(result.StdOut);
        var broken = results.First(r => r.Plugin == "broken");
        Assert.Equal("failed", broken.Status);
        Assert.Contains("was not found in assembly", broken.Detail ?? "");
        var alpha = results.First(r => r.Plugin == "alpha");
        Assert.Equal("succeeded", alpha.Status);
        Assert.Equal(3, alpha.Inserted);
    }

    [Fact]
    public async Task SecretRead_By_Plugin_Is_Never_Printed()
    {
        using var workspace = new CliWorkspace();
        // The stub plugin checks whether "stub/username" exists; the env var holds the value
        // that must never appear in output.
        workspace.AddStubPlugin("stub", stubJson: """{ "secretKey": "stub/username" }""");

        var env = workspace.Env(CliWorkspace.EnvironmentSecretStore(("STUB_USERNAME", "S3CR3T-VALUE")));
        var result = await workspace.RunAsync(["download", "--json"], env);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("S3CR3T-VALUE", result.StdOut + result.StdErr);

        // Status confirms the secret-check transaction was stored (storedTransactions == 1).
        var status = await workspace.RunAsync(["status", "--json"], env);
        Assert.Equal(0, status.ExitCode);
        using var doc = JsonDocument.Parse(status.StdOut);
        var stored = doc.RootElement.GetProperty("plugins")[0].GetProperty("storedTransactions").GetInt64();
        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task Idempotency_Across_Processes()
    {
        using var workspace = new CliWorkspace();
        workspace.AddStubPlugin("fidelity", stubJson: StubPluginFixture.TransactionsJson(3));

        for (var run = 0; run < 3; run++)
        {
            await workspace.RunAsync(["download", "--json"]);
        }

        var status = await workspace.RunAsync(["status", "--json"]);
        Assert.Equal(0, status.ExitCode);
        using var doc = JsonDocument.Parse(status.StdOut);
        var stored = doc.RootElement.GetProperty("plugins")[0].GetProperty("storedTransactions").GetInt64();
        Assert.Equal(3, stored);
    }

    [Fact]
    public async Task CosmeticDuplicates_Are_Deduplicated()
    {
        using var workspace = new CliWorkspace();
        // Two transactions with the same logical identity but different payee casing/whitespace.
        // The canonical hasher upper-cases and collapses payee whitespace, so they share a hash.
        var stub = $$"""
        {
          "transactions": [
            { "externalId": "d1", "amount": -1.50, "date": "2026-01-15", "payee": "CORNER STORE", "type": "debit" },
            { "externalId": "d1", "amount": -1.50, "date": "2026-01-15", "payee": "corner  store", "type": "debit" }
          ]
        }
        """;
        workspace.AddStubPlugin("fidelity", stubJson: stub);

        var result = await workspace.RunAsync(["download", "--json"]);

        Assert.Equal(0, result.ExitCode);
        var results = await ParseDownloadJsonAsync(result.StdOut);
        var fidelity = Assert.Single(results);
        Assert.Equal(1, fidelity.Inserted);
        Assert.Equal(1, fidelity.Skipped);
    }

    private sealed class PluginResult
    {
        public string Plugin { get; init; } = "";
        public string Status { get; init; } = "";
        public int? Inserted { get; init; }
        public int? Skipped { get; init; }
        public int? Rejected { get; init; }
        public string? Detail { get; init; }
    }
}
