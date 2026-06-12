using System;
using System.Text.Json;
using GitCredentialManager.UsageSurvey;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class UsageSurveyEventTests
{
    [Fact]
    public void Serializes_All_Fields_With_Stable_Names()
    {
        var evt = new UsageSurveyEvent
        {
            Event = "get",
            EventVersion = 1,
            Timestamp = "2026-06-09T15:58:50Z",
            InstallId = "3f2dceae-0000-4000-8000-00000000b9c1",
            GcmVersion = "2.6.1",
            Os = "macos",
            OsVersion = "14.5",
            Arch = "arm64",
            Provider = "github",
            AuthMethod = null,
            FromCache = true,
        };

        string json = JsonSerializer.Serialize(evt, UsageSurveyEventJsonContext.Default.UsageSurveyEvent);
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Exact schema: closed allow-list.
        Assert.Equal("get", root.GetProperty("event").GetString());
        Assert.Equal(1, root.GetProperty("event_version").GetInt32());
        Assert.Equal("2026-06-09T15:58:50Z", root.GetProperty("ts").GetString());
        Assert.Equal("3f2dceae-0000-4000-8000-00000000b9c1", root.GetProperty("install_id").GetString());
        Assert.Equal("2.6.1", root.GetProperty("gcm_version").GetString());
        Assert.Equal("macos", root.GetProperty("os").GetString());
        Assert.Equal("14.5", root.GetProperty("os_version").GetString());
        Assert.Equal("arm64", root.GetProperty("arch").GetString());
        Assert.Equal("github", root.GetProperty("provider").GetString());
        Assert.True(root.GetProperty("from_cache").GetBoolean());

        // No global "schema" envelope field.
        Assert.False(root.TryGetProperty("schema", out _));
        // auth_method is omitted when null.
        Assert.False(root.TryGetProperty("auth_method", out _));

        // Closed allow-list: 10 fields when auth_method omitted.
        int propertyCount = 0;
        foreach (var _ in root.EnumerateObject()) propertyCount++;
        Assert.Equal(10, propertyCount);
    }

    [Fact]
    public void Serializes_AuthMethod_When_Non_Null()
    {
        var evt = new UsageSurveyEvent
        {
            Event = "get",
            EventVersion = Constants.UsageSurvey.GetEventVersion,
            Timestamp = "2026-06-09T15:58:50Z",
            InstallId = Guid.Empty.ToString("D"),
            GcmVersion = "0.0.0",
            Os = "windows",
            OsVersion = "10.0.22631",
            Arch = "x64",
            Provider = "azure-repos",
            AuthMethod = "managed-identity",
            FromCache = false,
        };

        string json = JsonSerializer.Serialize(evt, UsageSurveyEventJsonContext.Default.UsageSurveyEvent);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("managed-identity", doc.RootElement.GetProperty("auth_method").GetString());
    }
}
