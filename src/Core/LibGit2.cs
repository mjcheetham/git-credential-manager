using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GitCredentialManager.Interop;
using static GitCredentialManager.Interop.LibGit2;

namespace GitCredentialManager;

public class LibGit2 : IGit
{
    public GitVersion Version => new(Interop.LibGit2.Version);

    public ChildProcess CreateProcess(string args)
    {
        throw new InvalidOperationException();
    }

    public bool IsInsideRepository()
    {
        return GetCurrentRepository() is null;
    }

    public unsafe string GetCurrentRepository()
    {
        string cwd = Environment.CurrentDirectory;
        if (git_repository_discover(out git_buf buf, cwd, false, null) == 0)
        {
            return EncodingEx.UTF8NoBom.GetString(buf.ptr, (int)buf.size);
        }

        return null;
    }

    public IEnumerable<GitRemote> GetRemotes()
    {
        IntPtr repo = GetRepository();
        if (repo != IntPtr.Zero)
        {
            if (git_remote_list(out git_strarray arr, repo) == 0)
            {
                foreach (string name in arr.GetStrings())
                {
                    if (git_remote_lookup(out IntPtr remote, repo, name) == 0)
                    {
                        string fetchUrl = git_remote_url(remote);
                        string pushUrl = git_remote_pushurl(remote);
                        yield return new GitRemote(name, fetchUrl, pushUrl);
                        git_remote_free(remote);
                    }
                }

                git_strarray_dispose(ref arr);
            }
        }

        git_repository_free(repo);
    }

    public IGitConfiguration GetConfiguration()
    {
        // Repo may be IntPtr.Zero if we are not inside a repo - this is OK!
        IntPtr repo = GetRepository();
        return new LibGit2Configuration(repo);
    }

    public Task<IDictionary<string, string>> InvokeHelperAsync(string args, IDictionary<string, string> standardInput)
    {
        throw new NotImplementedException();
    }

    private IntPtr GetRepository()
    {
        var repoPath = GetCurrentRepository() ?? throw new InvalidOperationException("Not inside a Git repository!");
        int result = git_repository_open(out IntPtr repo, repoPath);
        if (result != 0)
        {
            return IntPtr.Zero;
        }

        return repo;
    }
}

public class LibGit2Configuration : DisposableObject, IGitConfiguration
{
    private readonly IntPtr _repo;
    private readonly Lazy<IntPtr> _config;

    public LibGit2Configuration(IntPtr repo)
    {
        _repo = repo;
        _config = new Lazy<IntPtr>(CreateConfig);
    }

    private IntPtr CreateConfig()
    {
        int result = _repo == IntPtr.Zero
            ? git_config_open_default(out IntPtr cfg)
            : git_repository_config(out cfg, _repo);

        return result == 0 ? cfg : IntPtr.Zero;
    }

    public unsafe void Enumerate(GitConfigurationLevel level, GitConfigurationEnumerationCallback cb)
    {
        git_config_foreach(_config.Value, OnEntry, IntPtr.Zero);
        return;

        int OnEntry(git_config_entry* entry, IntPtr _)
        {
            if (IsLevelMatch(level, entry->level))
            {
                string name = Marshal.PtrToStringUTF8(entry->name);
                string value = Marshal.PtrToStringUTF8(entry->value);
                if (!cb(new GitConfigurationEntry(name, value)))
                {
                    return 1;
                }
            }

            return 0;
        }
    }

    public bool TryGet(GitConfigurationLevel level, GitConfigurationType type, string name, out string value)
    {
        if (git_config_get_entry(out git_config_entry entry, _config.Value, name) == 0)
        {
            string rawValue = Marshal.PtrToStringUTF8(entry.value);
            value = ConvertToType(rawValue, type);
            return true;
        }

        value = null;
        return false;
    }

    public void Set(GitConfigurationLevel level, string name, string value)
    {
        throw new NotImplementedException();
    }

    private static git_config_level_t ConvertLevel(GitConfigurationLevel level)
    {
        return level switch
        {
            GitConfigurationLevel.System => git_config_level_t.GIT_CONFIG_LEVEL_SYSTEM,
            GitConfigurationLevel.Global => git_config_level_t.GIT_CONFIG_LEVEL_GLOBAL,
            GitConfigurationLevel.Local => git_config_level_t.GIT_CONFIG_LEVEL_LOCAL,
            _ => throw new InvalidOperationException(),
        };
    }

    public void Add(GitConfigurationLevel level, string name, string value)
    {
        throw new NotImplementedException();
    }

    public void Unset(GitConfigurationLevel level, string name)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetAll(GitConfigurationLevel level, GitConfigurationType type, string name)
    {
        var entries = new List<GitConfigurationEntry>();

        Enumerate(level, OnEntry);
        bool OnEntry(GitConfigurationEntry entry)
        {
            if (GitConfigurationKeyComparer.Instance.Equals(name, entry.Key))
            {
                entries.Add(entry);
            }

            return true;
        }

        foreach (var entry in entries)
        {
            yield return ConvertToType(entry.Value, type);
        }
    }

    public IEnumerable<string> GetRegex(GitConfigurationLevel level, GitConfigurationType type, string nameRegex, string valueRegex)
    {
        throw new NotImplementedException();
    }

    public void ReplaceAll(GitConfigurationLevel level, string nameRegex, string valueRegex, string value)
    {
        throw new NotImplementedException();
    }

    public void UnsetAll(GitConfigurationLevel level, string name, string valueRegex)
    {
        throw new NotImplementedException();
    }

    protected override void ReleaseManagedResources()
    {
        if (_repo != IntPtr.Zero)
            git_repository_free(_repo);

        if (_config.IsValueCreated && _config.Value != IntPtr.Zero)
            git_config_free(_config.Value);
    }

    private static bool IsLevelMatch(GitConfigurationLevel query, git_config_level_t level)
    {
        switch (query)
        {
            case GitConfigurationLevel.All:
                return true;
            case GitConfigurationLevel.System:
                return level is git_config_level_t.GIT_CONFIG_LEVEL_SYSTEM or git_config_level_t.GIT_CONFIG_LEVEL_XDG;
            case GitConfigurationLevel.Global:
                return level is git_config_level_t.GIT_CONFIG_LEVEL_GLOBAL or git_config_level_t.GIT_CONFIG_LEVEL_XDG;
            case GitConfigurationLevel.Local:
                return level is git_config_level_t.GIT_CONFIG_LEVEL_LOCAL or git_config_level_t.GIT_CONFIG_LEVEL_WORKTREE;
            case GitConfigurationLevel.Unknown:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(query), query, "Unknown configuration level");
        }
    }

    private string ConvertToType(string value, GitConfigurationType type)
    {
        switch (type)
        {
            case GitConfigurationType.Raw:
                return value;

            case GitConfigurationType.Bool:
                var b = value.ToBooleany();
                return b is not null ? b.ToString() : string.Empty;

            case GitConfigurationType.Path:
                return value; // todo

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
}
