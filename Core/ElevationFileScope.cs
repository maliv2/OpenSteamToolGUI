using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenSteamToolGUI.Core;

// Keep checked ancestors open until all privileged I/O (including rollback) finishes.
// String containment alone cannot confine a Windows path containing a junction.
internal sealed class ElevationFileScope : IDisposable
{
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private readonly Dictionary<string, SafeFileHandle> _directories = new(StringComparer.OrdinalIgnoreCase);

    public void PinDirectory(string directory, bool create = false)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (_directories.ContainsKey(full)) return;
        string? parent = Path.GetDirectoryName(full);
        if (parent is not null && !parent.Equals(full, StringComparison.OrdinalIgnoreCase)) PinDirectory(parent, create);
        if (create && !Directory.Exists(full)) Directory.CreateDirectory(full);
        // Directory write sharing is necessary for creating transaction children;
        // deny deletion/renaming, and also verify each opened handle's final path.
        var handle = Open(full, 1, OpenReparsePoint | BackupSemantics, 3);
        try
        {
            var info = Inspect(handle);
            CheckFinalPath(handle, full);
            if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                (info.Attributes & (uint)FileAttributes.Directory) == 0)
                throw new InvalidDataException("Invalid request.");
            _directories.Add(full, handle);
        }
        catch { handle.Dispose(); throw; }
    }

    public byte[] ReadFile(string root, string path, int maximum)
    {
        string full = Path.GetFullPath(path);
        if (!FileTools.Inside(root, full) || full[Path.GetPathRoot(full)!.Length..].Contains(':'))
            throw new InvalidDataException("Content outside request directory.");
        PinDirectory(Path.GetDirectoryName(full)!);
        using var handle = Open(full, 0x80000000, OpenReparsePoint); // GENERIC_READ
        var info = Inspect(handle);
        CheckFinalPath(handle, full);
        if ((info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.NumberOfLinks != 1)
            throw new InvalidDataException("Invalid request.");
        using var input = new FileStream(handle, FileAccess.Read);
        if (input.Length > maximum) throw new InvalidDataException("Content too large.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        return bytes;
    }

    private static SafeFileHandle Open(string path, uint access, uint flags, uint share = 1)
    {
        // Input files deny write/delete sharing; directories deny delete sharing.
        var handle = CreateFile(path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static FileInformation Inspect(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }

    private static void CheckFinalPath(SafeFileHandle handle, string expected)
    {
        var name = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, name, (uint)name.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= name.Capacity) throw new InvalidDataException("Invalid request.");
        string resolved = name.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        if (!Path.TrimEndingDirectorySeparator(resolved).Equals(Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content outside request directory.");
    }

    public void Dispose()
    {
        foreach (var handle in _directories.Values.Reverse()) handle.Dispose();
        _directories.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint VolumeSerialNumber, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
