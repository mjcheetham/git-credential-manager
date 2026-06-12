using System.Diagnostics;
using System.Text;
using GitCredentialManager.UsageSurvey;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class DispatcherPidFileTests
{
    private static (TestFileSystem, UsageSurveyPaths) Build()
    {
        var fs = new TestFileSystem();
        var paths = new UsageSurveyPaths(fs);
        return (fs, paths);
    }

    [Fact]
    public void IsActive_NoFile_ReturnsFalse()
    {
        var (fs, paths) = Build();
        Assert.False(DispatcherPidFile.IsActive(fs, paths, new NullTrace()));
    }

    [Fact]
    public void IsActive_StalePid_ReturnsFalse()
    {
        var (fs, paths) = Build();
        fs.Directories.Add(paths.UsageSurveyDirectory);
        // A non-existent pid (high number unlikely to be alive).
        fs.Files[paths.DispatcherPidFile] = Encoding.UTF8.GetBytes("2147483646\n");

        Assert.False(DispatcherPidFile.IsActive(fs, paths, new NullTrace()));
    }

    [Fact]
    public void IsActive_OurOwnPid_ReturnsTrue()
    {
        var (fs, paths) = Build();
        fs.Directories.Add(paths.UsageSurveyDirectory);
        int self = Process.GetCurrentProcess().Id;
        fs.Files[paths.DispatcherPidFile] = Encoding.UTF8.GetBytes(self.ToString() + "\n");

        Assert.True(DispatcherPidFile.IsActive(fs, paths, new NullTrace()));
    }

    [Fact]
    public void IsActive_GarbageFile_ReturnsFalse()
    {
        var (fs, paths) = Build();
        fs.Directories.Add(paths.UsageSurveyDirectory);
        fs.Files[paths.DispatcherPidFile] = Encoding.UTF8.GetBytes("not-a-pid");

        Assert.False(DispatcherPidFile.IsActive(fs, paths, new NullTrace()));
    }

    [Fact]
    public void TryAcquire_Writes_Our_Pid_And_Release_Removes_It()
    {
        var (fs, paths) = Build();

        Assert.True(DispatcherPidFile.TryAcquire(fs, paths, new NullTrace()));
        Assert.True(fs.Files.ContainsKey(paths.DispatcherPidFile));

        DispatcherPidFile.Release(fs, paths, new NullTrace());
        Assert.False(fs.Files.ContainsKey(paths.DispatcherPidFile));
    }

    [Fact]
    public void Release_Does_Not_Remove_Other_Pid()
    {
        var (fs, paths) = Build();
        fs.Directories.Add(paths.UsageSurveyDirectory);
        fs.Files[paths.DispatcherPidFile] = Encoding.UTF8.GetBytes("99999\n");

        DispatcherPidFile.Release(fs, paths, new NullTrace());
        Assert.True(fs.Files.ContainsKey(paths.DispatcherPidFile));
    }
}
