using System.Runtime.InteropServices;

namespace Glance.Core;

public static class RecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    /// <summary>Moves a file to the Recycle Bin, never deletes permanently, never shows shell UI.</summary>
    /// <exception cref="IOException">The shell refused (file in use, no recycle bin on that volume, …).</exception>
    public static void Send(string path)
    {
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0",   // list is double-null-terminated; marshaller adds the second
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var result = SHFileOperationW(ref op);
        if (result != 0 || op.fAnyOperationsAborted != 0)
            throw new IOException($"Could not move '{path}' to the Recycle Bin (0x{result:X}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

#pragma warning disable SYSLIB1054 // struct with string fields isn't supported by LibraryImport
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);
#pragma warning restore SYSLIB1054
}
