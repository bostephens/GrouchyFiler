using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using GrouchyFiler.Models;

namespace GrouchyFiler.Services;

internal static class FileCleanup
{
    // Keep every ancestor pinned against rename/removal, and never traverse a reparse point.
    internal static string? Delete(string path, RootConfig? root = null, CancellationToken cancellation = default, Action? beforeDeleteForTesting = null)
    {
        path = Path.GetFullPath(path);
        if (root is not null && !path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Cleanup target is outside the configured folder.");
        var parents = new Stack<string>();
        for (string? parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent)) parents.Push(parent);
        var handles = new List<SafeFileHandle>();
        try
        {
            while (parents.TryPop(out var parent))
            {
                cancellation.ThrowIfCancellationRequested();
                var directory = Open(parent, 0x80, 1, 0x02200000); // attributes, share read only, backup + open reparse point
                handles.Add(directory);
                var info = Information(directory);
                if ((info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
                    throw new UnauthorizedAccessException("Cleanup cannot follow symbolic links or junctions.");
            }
            cancellation.ThrowIfCancellationRequested();
            using var file = Open(path, 0x80010000, 0, 0x00200000); // read + delete, exclusive, open reparse point
            var metadata = Information(file);
            if ((metadata.Attributes & (0x400 | 0x10)) != 0)
                throw new UnauthorizedAccessException("Cleanup deletes ordinary files only.");
            long length = ((long)metadata.SizeHigh << 32) | metadata.SizeLow;
            var modified = DateTime.FromFileTimeUtc(((long)metadata.WriteHigh << 32) | metadata.WriteLow);
            if (root is not null)
            {
                string? reason = WatcherService.FilterReason(root, path, length, modified, DateTime.UtcNow);
                if (reason is not null) return reason;
            }
            cancellation.ThrowIfCancellationRequested();
            // The checked object stays exclusively open until Windows deletes it on handle close.
            beforeDeleteForTesting?.Invoke();
            cancellation.ThrowIfCancellationRequested();
            byte delete = 1;
            if (!SetFileInformationByHandle(file, 4, ref delete, 1)) ThrowIo();
            return null;
        }
        finally { for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); }
    }

    private static SafeFileHandle Open(string path, uint access, uint share, uint flags)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); ThrowIo(); }
        return handle;
    }
    private static FileInformation Information(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) ThrowIo();
        return info;
    }
    private static void ThrowIo() => throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, uint size);
}

