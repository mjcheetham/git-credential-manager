using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.UsageSurvey;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class UsageSurveyDispatcherArchiveTests
{
    private sealed class AlwaysOkUploader : IUsageSurveyUploader
    {
        public int Count { get; private set; }
        public Task<bool> UploadAsync(string jsonLine, CancellationToken ct)
        {
            Count++;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task DrainOnce_Successfully_Shipped_Files_Are_Moved_To_Sent()
    {
        var fs = new TestFileSystem();
        var paths = new UsageSurveyPaths(fs);
        var up = new AlwaysOkUploader();
        var disp = new UsageSurveyDispatcher(fs, paths, new NullTrace(), up);

        fs.Directories.Add(paths.EventsDirectory);
        string srcName = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-123-1.jsonl";
        string src = Path.Combine(paths.EventsDirectory, srcName);
        fs.Files[src] = Encoding.UTF8.GetBytes("{\"event\":\"get\"}\n");

        await disp.DrainOnceAsync(CancellationToken.None);

        Assert.False(fs.Files.ContainsKey(src));
        Assert.True(fs.Files.ContainsKey(Path.Combine(paths.SentDirectory, srcName)));
    }

    [Theory]
    [InlineData("20260609T120000000-1234-1.jsonl", true)]
    [InlineData("20260609T120000000-1234.jsonl", true)]
    [InlineData("garbage.jsonl", false)]
    [InlineData("20260609T120000000.jsonl", false)] // no dash separator
    [InlineData("", false)]
    public void TryParseEventTimestamp(string fileName, bool expectedSuccess)
    {
        bool ok = UsageSurveyDispatcher.TryParseEventTimestamp(fileName, out DateTimeOffset _);
        Assert.Equal(expectedSuccess, ok);
    }
}
