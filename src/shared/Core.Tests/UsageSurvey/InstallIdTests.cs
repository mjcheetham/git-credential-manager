using System;
using System.IO;
using GitCredentialManager.UsageSurvey;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class InstallIdTests
{
    private static (InstallId, TestFileSystem, UsageSurveyPaths) Build()
    {
        var fs = new TestFileSystem();
        var paths = new UsageSurveyPaths(fs);
        var id = new InstallId(fs, paths, new NullTrace());
        return (id, fs, paths);
    }

    [Fact]
    public void TryGet_NoFile_ReturnsNull()
    {
        var (id, _, _) = Build();
        Assert.Null(id.TryGet());
    }

    [Fact]
    public void GetOrCreate_Creates_And_Persists_Id()
    {
        var (id, fs, paths) = Build();

        Guid first = id.GetOrCreate();
        Assert.NotEqual(Guid.Empty, first);

        // File now exists.
        Assert.True(fs.Files.ContainsKey(paths.InstallIdFile));

        // Second call returns the same id.
        Guid second = id.GetOrCreate();
        Assert.Equal(first, second);

        // TryGet sees it too.
        Assert.Equal(first, id.TryGet());
    }

    [Fact]
    public void Reset_Generates_New_Id()
    {
        var (id, _, _) = Build();

        Guid first = id.GetOrCreate();
        Guid second = id.Reset();

        Assert.NotEqual(first, second);
        Assert.Equal(second, id.TryGet());
    }

    [Fact]
    public void TryGet_MalformedFile_ReturnsNull()
    {
        var (id, fs, paths) = Build();

        fs.Directories.Add(paths.UsageSurveyDirectory);
        fs.Files[paths.InstallIdFile] = System.Text.Encoding.UTF8.GetBytes("not-a-guid");

        Assert.Null(id.TryGet());
    }
}
