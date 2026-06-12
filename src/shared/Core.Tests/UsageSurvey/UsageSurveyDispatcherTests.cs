using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.UsageSurvey;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class UsageSurveyDispatcherTests
{
    private sealed class FakeUploader : IUsageSurveyUploader
    {
        public List<string> Sent { get; } = new();
        public bool ReturnSuccess { get; set; } = true;

        public Task<bool> UploadAsync(string jsonLine, CancellationToken ct)
        {
            if (ReturnSuccess)
            {
                Sent.Add(jsonLine);
            }
            return Task.FromResult(ReturnSuccess);
        }
    }

    private static (TestFileSystem, UsageSurveyPaths, FakeUploader, UsageSurveyDispatcher) Build()
    {
        var fs = new TestFileSystem();
        var paths = new UsageSurveyPaths(fs);
        var up = new FakeUploader();
        var disp = new UsageSurveyDispatcher(fs, paths, new NullTrace(), up);
        return (fs, paths, up, disp);
    }

    private static void AddQueueFile(TestFileSystem fs, UsageSurveyPaths paths, string name, string content)
    {
        fs.Directories.Add(paths.EventsDirectory);
        fs.Files[Path.Combine(paths.EventsDirectory, name)] = Encoding.UTF8.GetBytes(content);
    }

    [Fact]
    public async Task DrainOnce_NoDirectory_Returns_Zero()
    {
        var (_, _, _, disp) = Build();
        int n = await disp.DrainOnceAsync(CancellationToken.None);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task DrainOnce_Empty_Directory_Returns_Zero()
    {
        var (fs, paths, _, disp) = Build();
        fs.Directories.Add(paths.EventsDirectory);
        int n = await disp.DrainOnceAsync(CancellationToken.None);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task DrainOnce_Ships_And_Archives_Successful_Files()
    {
        var (fs, paths, up, disp) = Build();
        AddQueueFile(fs, paths, "20260609T000000000-1-1.jsonl", "{\"event\":\"get\"}\n{\"event\":\"get\"}\n");
        AddQueueFile(fs, paths, "20260609T000000000-2-1.jsonl", "{\"event\":\"get\"}\n");

        await disp.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(3, up.Sent.Count);
        // Both files moved out of events/ ...
        Assert.DoesNotContain(fs.Files.Keys,
            k => k.StartsWith(paths.EventsDirectory) && k.EndsWith(".jsonl"));
        // ... and into sent/.
        Assert.Equal(2, System.Linq.Enumerable.Count(fs.Files.Keys,
            k => k.StartsWith(paths.SentDirectory) && k.EndsWith(".jsonl")));
    }

    [Fact]
    public async Task DrainOnce_Failed_Upload_Leaves_File()
    {
        var (fs, paths, up, disp) = Build();
        up.ReturnSuccess = false;
        AddQueueFile(fs, paths, "20260609T000000-1.jsonl", "{\"event\":\"get\"}\n");

        await disp.DrainOnceAsync(CancellationToken.None);

        Assert.Empty(up.Sent);
        // File still present for retry next pass.
        Assert.Contains(fs.Files.Keys, k => k.EndsWith(".jsonl"));
    }

    [Fact]
    public async Task DrainOnce_Skips_Partial_Files()
    {
        var (fs, paths, up, disp) = Build();
        AddQueueFile(fs, paths, "20260609T000000-1.jsonl.partial", "{\"event\":\"get\"}\n");

        await disp.DrainOnceAsync(CancellationToken.None);

        Assert.Empty(up.Sent);
        Assert.Contains(fs.Files.Keys, k => k.EndsWith(".jsonl.partial"));
    }

    [Fact]
    public async Task DrainOnce_Skips_Blank_Lines()
    {
        var (fs, paths, up, disp) = Build();
        AddQueueFile(fs, paths, "20260609T000000-1.jsonl", "\n{\"event\":\"get\"}\n\n");

        await disp.DrainOnceAsync(CancellationToken.None);

        Assert.Single(up.Sent);
    }

    [Fact]
    public async Task RunAsync_Exits_Quickly_When_Idle_Timeout_Is_Zero()
    {
        var (fs, paths, _, _) = Build();
        var up = new FakeUploader();
        var disp = new UsageSurveyDispatcher(fs, paths, new NullTrace(), up)
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            IdleTimeout = TimeSpan.Zero,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await disp.RunAsync(cts.Token);

        // pidfile should be released after exit.
        Assert.False(fs.Files.ContainsKey(paths.DispatcherPidFile));
    }
}
