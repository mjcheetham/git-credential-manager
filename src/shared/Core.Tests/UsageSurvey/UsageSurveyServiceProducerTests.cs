using System.Linq;
using System.Text;
using System.Text.Json;
using GitCredentialManager.UsageSurvey;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class UsageSurveyServiceProducerTests
{
    private static (UsageSurveyService svc, TestCommandContext ctx, UsageSurveyPaths paths) Build(bool enabled)
    {
        var ctx = new TestCommandContext();
        if (enabled)
        {
            ctx.Environment.Variables[Constants.EnvironmentVariables.GcmUsageSurvey] = "1";
        }
        var paths = new UsageSurveyPaths(ctx.FileSystem);
        var svc = new UsageSurveyService(ctx, paths);
        return (svc, ctx, paths);
    }

    [Fact]
    public void RecordGet_When_Disabled_Writes_Nothing()
    {
        var (svc, ctx, paths) = Build(enabled: false);

        svc.RecordGet("github", fromCache: true, authMethod: null);

        Assert.False(ctx.FileSystem.Directories.Contains(paths.EventsDirectory));
        Assert.Empty(ctx.FileSystem.Files);
    }

    [Fact]
    public void RecordGet_Writes_Self_Contained_Jsonl_File_Synchronously()
    {
        var (svc, ctx, paths) = Build(enabled: true);

        svc.RecordGet("github", fromCache: true, authMethod: null);

        // RecordGet must publish the .jsonl file atomically — no Dispose required.
        string[] partialFiles = ctx.FileSystem.Files.Keys
            .Where(k => k.StartsWith(paths.EventsDirectory) && k.EndsWith(".jsonl.partial"))
            .ToArray();
        string[] finalFiles = ctx.FileSystem.Files.Keys
            .Where(k => k.StartsWith(paths.EventsDirectory) && k.EndsWith(".jsonl") && !k.EndsWith(".partial"))
            .ToArray();

        Assert.Empty(partialFiles);
        Assert.Single(finalFiles);

        string content = Encoding.UTF8.GetString(ctx.FileSystem.Files[finalFiles[0]]);
        Assert.EndsWith("\n", content);

        using var doc = JsonDocument.Parse(content);
        Assert.Equal("github", doc.RootElement.GetProperty("provider").GetString());
        Assert.True(doc.RootElement.GetProperty("from_cache").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("auth_method", out _));
    }

    [Fact]
    public void RecordGet_AuthMethod_Is_Captured_For_All_Providers()
    {
        var (svc, ctx, paths) = Build(enabled: true);

        svc.RecordGet("azure-repos", fromCache: false, authMethod: "managed-identity");

        string file = ctx.FileSystem.Files.Keys.First(
            k => k.EndsWith(".jsonl") && !k.EndsWith(".partial"));
        string content = Encoding.UTF8.GetString(ctx.FileSystem.Files[file]);
        using var doc = JsonDocument.Parse(content);

        Assert.Equal("azure-repos", doc.RootElement.GetProperty("provider").GetString());
        Assert.Equal("managed-identity", doc.RootElement.GetProperty("auth_method").GetString());
    }

    [Fact]
    public void RecordGet_Whitespace_AuthMethod_Is_Omitted()
    {
        var (svc, ctx, paths) = Build(enabled: true);

        svc.RecordGet("github", fromCache: false, authMethod: "   ");

        string file = ctx.FileSystem.Files.Keys.First(
            k => k.EndsWith(".jsonl") && !k.EndsWith(".partial"));
        string content = Encoding.UTF8.GetString(ctx.FileSystem.Files[file]);
        using var doc = JsonDocument.Parse(content);

        Assert.False(doc.RootElement.TryGetProperty("auth_method", out _));
    }

    [Fact]
    public void RecordGet_Multiple_Events_Each_Get_Own_File()
    {
        var (svc, ctx, paths) = Build(enabled: true);

        svc.RecordGet("github", fromCache: false, authMethod: "browser");
        svc.RecordGet("gitlab", fromCache: true, authMethod: null);
        svc.RecordGet("generic", fromCache: false, authMethod: "basic");

        string[] finalFiles = ctx.FileSystem.Files.Keys
            .Where(k => k.StartsWith(paths.EventsDirectory) && k.EndsWith(".jsonl") && !k.EndsWith(".partial"))
            .ToArray();

        Assert.Equal(3, finalFiles.Length);
    }

    [Fact]
    public void RecordGet_NullOrEmpty_Provider_Is_Noop()
    {
        var (svc, ctx, _) = Build(enabled: true);

        svc.RecordGet(null, fromCache: false, authMethod: null);
        svc.RecordGet("", fromCache: false, authMethod: null);

        Assert.Empty(ctx.FileSystem.Files);
    }

    [Fact]
    public void IsEnabled_EnvVar_Falsey_Returns_False()
    {
        var ctx = new TestCommandContext();
        ctx.Environment.Variables[Constants.EnvironmentVariables.GcmUsageSurvey] = "0";
        var svc = new UsageSurveyService(ctx);
        Assert.False(svc.IsEnabled);
    }

    [Fact]
    public void IsEnabled_Default_Is_False()
    {
        var ctx = new TestCommandContext();
        var svc = new UsageSurveyService(ctx);
        Assert.False(svc.IsEnabled);
    }
}
