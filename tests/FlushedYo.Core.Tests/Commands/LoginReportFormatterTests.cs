using System.Text.Json;
using FlushedYo.Core.Commands;

namespace FlushedYo.Core.Tests.Commands;

/// <summary>Table and JSON rendering for login (spec: login_credential_preflight).</summary>
public sealed class LoginReportFormatterTests
{
    private static LoginReport Report() => new(
    [
        new PluginLoginResult(
            "fidelity",
            [
                new CredentialCheck("username", "fidelity/username", CredentialStatus.Ok),
                new CredentialCheck("password", "fidelity/password", CredentialStatus.Missing),
            ]),
        new PluginLoginResult(
            "schwab",
            [
                new CredentialCheck("username", null, CredentialStatus.NotConfigured),
                new CredentialCheck("password", null, CredentialStatus.NotConfigured),
            ]),
    ]);

    [Fact]
    public void EmptyReport_RendersTheNoPluginsMessage()
    {
        Assert.Equal("No plugins installed.", LoginReportFormatter.FormatTable(new LoginReport([])));
    }

    [Fact]
    public void Table_RendersStatusesAndDashesForUnconfiguredKeys()
    {
        Assert.Equal(
            "PLUGIN    CREDENTIAL  KEY                STATUS\n" +
            "fidelity  username    fidelity/username  ok\n" +
            "fidelity  password    fidelity/password  missing\n" +
            "schwab    username    -                  not-configured\n" +
            "schwab    password    -                  not-configured",
            LoginReportFormatter.FormatTable(Report()));
    }

    [Fact]
    public void Json_HasTheDocumentedShape()
    {
        using var document = JsonDocument.Parse(LoginReportFormatter.FormatJson(Report()));
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        var plugins = root.GetProperty("plugins");
        Assert.Equal(2, plugins.GetArrayLength());

        var first = plugins[0];
        Assert.Equal("fidelity", first.GetProperty("plugin").GetString());
        var credentials = first.GetProperty("credentials");
        Assert.Equal("username", credentials[0].GetProperty("credential").GetString());
        Assert.Equal("fidelity/username", credentials[0].GetProperty("key").GetString());
        Assert.Equal("ok", credentials[0].GetProperty("status").GetString());
        Assert.Equal("missing", credentials[1].GetProperty("status").GetString());

        var unconfigured = plugins[1].GetProperty("credentials")[0];
        Assert.Equal(JsonValueKind.Null, unconfigured.GetProperty("key").ValueKind);
        Assert.Equal("notConfigured", unconfigured.GetProperty("status").GetString());
    }

    [Fact]
    public void Json_OkIsTrueWhenNothingIsMissing()
    {
        var report = new LoginReport(
        [
            new PluginLoginResult("a", [new CredentialCheck("username", "a/u", CredentialStatus.Ok), new CredentialCheck("password", null, CredentialStatus.NotConfigured)]),
        ]);

        using var document = JsonDocument.Parse(LoginReportFormatter.FormatJson(report));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Json_ForAnEmptyReport_IsOkWithNoPlugins()
    {
        using var document = JsonDocument.Parse(LoginReportFormatter.FormatJson(new LoginReport([])));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("plugins").GetArrayLength());
    }
}
