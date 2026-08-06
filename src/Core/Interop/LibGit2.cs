using System;
using System.Runtime.InteropServices;

namespace GitCredentialManager.Interop;

public static partial class LibGit2
{
    private const string LibraryName = LibGit2Info.FileName;
    public const string Version = "2.0.0";

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int git_repository_discover(
        out git_buf @out,
        string start_path,
        [MarshalAs(UnmanagedType.Bool)] bool across_fs,
        string ceiling_dirs
    );

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int git_libgit2_init();

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int git_libgit2_shutdown();

    [LibraryImport(LibraryName)]
    public static partial int git_remote_list(out git_strarray @out, IntPtr repo);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int git_remote_lookup(
        out IntPtr remote,
        IntPtr repo,
        string name
    );

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial string git_remote_url(IntPtr remote);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial string git_remote_pushurl(IntPtr remote);

    [LibraryImport(LibraryName)]
    public static partial void git_remote_free(IntPtr remote);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int git_repository_open(out IntPtr @out, string path);

    [LibraryImport(LibraryName)]
    public static partial void git_repository_free(IntPtr repo);

    [LibraryImport(LibraryName)]
    public static partial int git_repository_config(out IntPtr @out, IntPtr repo);

    [LibraryImport(LibraryName)]
    public static partial int git_config_open_default(out IntPtr @out);

    [LibraryImport(LibraryName)]
    public static partial int git_config_foreach(
        IntPtr cfg,
        git_config_foreach_cb callback,
        IntPtr payload
    );

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int git_config_get_entry(
        out git_config_entry @out,
        IntPtr cfg,
        string name
    );

    [LibraryImport(LibraryName)]
    public static partial void git_config_free(IntPtr cfg);

    [LibraryImport(LibraryName)]
    public static unsafe partial void git_config_entry_free(git_config_entry* entry);

    [LibraryImport(LibraryName)]
    public static unsafe partial void git_strarray_dispose(ref git_strarray array);
}

[StructLayout(LayoutKind.Sequential)]
public struct git_strarray
{
    public IntPtr strings;
    public long count;

    public string[] GetStrings()
    {
        string[] arr = new string[count];
        for (long i = 0; i < count; i++)
        {
            IntPtr ptr = Marshal.ReadIntPtr(strings, (int)(i * IntPtr.Size));
            arr[i] = Marshal.PtrToStringUTF8(ptr);
        }
        return arr;
    }
}

public unsafe delegate int git_config_foreach_cb(git_config_entry* entry, IntPtr payload);

[StructLayout(LayoutKind.Sequential)]
public struct git_buf
{
    public unsafe byte *ptr;
    public long asize, size;
}

[StructLayout(LayoutKind.Sequential)]
public struct git_config_entry
{
    /// <summary>
    /// Name of the configuration entry (normalized).
    /// </summary>
    public IntPtr name;

    /// <summary>
    /// Literal (string) value of the entry.
    /// </summary>
    public IntPtr value;

    /// <summary>
    /// The type of backend that this entry exists in (eg, "file").
    /// </summary>
    public IntPtr backend_type;

    /// <summary>
    /// The path to the origin of this entry. For config files, this is the path to the file.
    /// </summary>
    public IntPtr origin_path;

    /// <summary>
    /// Depth of includes where this variable was found.
    /// </summary>
    public uint include_depth;

    /// <summary>
    /// Configuration level for the file this was found in.
    /// </summary>
    public git_config_level_t level;
}

public enum git_config_level_t
{
    GIT_CONFIG_LEVEL_SYSTEM = 2,
    GIT_CONFIG_LEVEL_XDG = 3,
    GIT_CONFIG_LEVEL_GLOBAL = 4,
    GIT_CONFIG_LEVEL_LOCAL = 5,
    GIT_CONFIG_LEVEL_WORKTREE = 6,
    GIT_CONFIG_LEVEL_APP = 7,
    GIT_CONFIG_HIGHEST_LEVEL = -1
}
