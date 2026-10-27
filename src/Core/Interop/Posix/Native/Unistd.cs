using System.Runtime.InteropServices;

namespace GitCredentialManager.Interop.Posix.Native
{
    public static class Unistd
    {
        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int read(int fd, byte[] buf, int count);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int write(int fd, byte[] buf, int size);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int close(int fd);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int getpid();

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int getppid();

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int geteuid();

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern long pathconf(string path, PathConfName name);
    }

    public enum PathConfName
    {
        _PC_LINK_MAX             = 1,
        _PC_MAX_CANON            = 2,
        _PC_MAX_INPUT            = 3,
        _PC_NAME_MAX             = 4,
        _PC_PATH_MAX             = 5,
        _PC_PIPE_BUF             = 6,
        _PC_CHOWN_RESTRICTED     = 7,
        _PC_NO_TRUNC             = 8,
        _PC_VDISABLE             = 9,
        _PC_NAME_CHARS_MAX       = 10,
        _PC_CASE_SENSITIVE       = 11,
        _PC_CASE_PRESERVING      = 12,
        _PC_EXTENDED_SECURITY_NP = 13,
        _PC_AUTH_OPAQUE_NP       = 14,
        _PC_2_SYMLINKS           = 15,    /* Symlink supported in directory */
        _PC_ALLOC_SIZE_MIN       = 16,    /* Minimum storage actually allocated */
        _PC_ASYNC_IO             = 17,    /* Async I/O [AIO] supported? */
        _PC_FILESIZEBITS         = 18,    /* # of bits to represent file size */
        _PC_PRIO_IO              = 19,    /* Priority I/O [PIO] supported? */
        _PC_REC_INCR_XFER_SIZE   = 20,    /* Recommended increment for next two */
        _PC_REC_MAX_XFER_SIZE    = 21,    /* Recommended max file transfer size */
        _PC_REC_MIN_XFER_SIZE    = 22,    /* Recommended min file transfer size */
        _PC_REC_XFER_ALIGN       = 23,    /* Recommended buffer alignment */
        _PC_SYMLINK_MAX          = 24,    /* Max # of bytes in symlink name */
        _PC_SYNC_IO              = 25,    /* Sync I/O [SIO] supported? */
        _PC_XATTR_SIZE_BITS      = 26,    /* # of bits to represent maximum xattr size */
        _PC_MIN_HOLE_SIZE        = 27,    /* Recommended minimum hole size for sparse files */
    }
}
