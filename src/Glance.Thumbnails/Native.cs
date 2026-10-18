using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Glance.Thumbnails;

/// <summary>COM server entry points exported from the NativeAOT DLL.</summary>
public static unsafe class Exports
{
    // Must match desktop2:ThumbnailHandler / com:Class in Package.appxmanifest.
    public static readonly Guid Clsid = new("fa45545e-4edc-4912-90ad-24efae6273d0");

    internal static readonly StrategyBasedComWrappers ComWrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static int DllGetClassObject(Guid* clsid, Guid* iid, void** result)
    {
        *result = null;
        if (*clsid != Clsid)
            return HResult.CLASS_E_CLASSNOTAVAILABLE;
        return QueryInterface(new ClassFactory(), *iid, result);
    }

    // S_FALSE: a NativeAOT image can't be unloaded; the surrogate keeps us until it exits.
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow() => 1;

    internal static int QueryInterface(object instance, Guid iid, void** result)
    {
        var unknown = ComWrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        var hr = Marshal.QueryInterface(unknown, iid, out var ptr);
        Marshal.Release(unknown);
        *result = (void*)ptr;
        return hr;
    }
}

[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
    public unsafe int CreateInstance(nint outer, Guid* iid, void** result)
    {
        *result = null;
        return outer != 0 ? HResult.CLASS_E_NOAGGREGATION : Exports.QueryInterface(new ThumbnailProvider(), *iid, result);
    }

    public int LockServer(int lockServer) => 0;
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal unsafe partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, Guid* iid, void** result);

    [PreserveSig]
    int LockServer(int lockServer);
}

/// <summary>Reads an IStream through its vtable: we only need Read and Stat, so no full IStream projection.</summary>
internal static unsafe class ComStream
{
    private const int StatReadFlag = 0;   // STATFLAG_DEFAULT: include the name (gives us the extension)

    /// <summary>Bigger files get Explorer's icon: a thumbnail isn't worth holding hundreds of MB in the surrogate.</summary>
    public const long MaxBytes = 512L << 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct STATSTG
    {
        public char* pwcsName;
        public uint type;
        public ulong cbSize;
        public long mtime, ctime, atime;
        public uint grfMode, grfLocksSupported;
        public Guid clsid;
        public uint grfStateBits, reserved;
    }

    public static (byte[] Data, string Extension) ReadAll(nint stream)
    {
        var vtable = *(void***)stream;
        var stat = (delegate* unmanaged[Stdcall]<nint, STATSTG*, int, int>)vtable[12];
        var read = (delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)vtable[3];

        STATSTG s;
        Marshal.ThrowExceptionForHR(stat(stream, &s, StatReadFlag));
        var extension = s.pwcsName is null ? "" : Path.GetExtension(new string(s.pwcsName));
        Marshal.FreeCoTaskMem((nint)s.pwcsName);

        // ponytail: whole file in memory, capped at MaxBytes; RAW previews could be read by seeking to the IFD offsets instead.
        if (s.cbSize > MaxBytes)
            throw new InvalidDataException($"{s.cbSize} bytes is over the {MaxBytes}-byte thumbnail limit.");
        using var buffer = new MemoryStream((int)s.cbSize);
        var chunk = new byte[1 << 20];
        fixed (byte* p = chunk)
        {
            while (true)
            {
                uint got;
                var hr = read(stream, p, (uint)chunk.Length, &got);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
                if (got == 0)
                    break;
                if (buffer.Length + got > MaxBytes)   // the size from Stat is only a hint
                    throw new InvalidDataException($"Stream is over the {MaxBytes}-byte thumbnail limit.");
                buffer.Write(chunk, 0, (int)got);
            }
        }
        return (buffer.ToArray(), extension);
    }
}

/// <summary>Top-down 32bpp DIB section from premultiplied BGRA, as WTSAT_ARGB thumbnails expect.</summary>
internal static unsafe partial class Dib
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    public static nint Create(byte[] bgra, int width, int height)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height,   // negative: top-down rows, same order as our buffer
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        var bitmap = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap != 0)
            bgra.AsSpan(0, width * height * 4).CopyTo(new Span<byte>(bits, width * height * 4));
        return bitmap;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);
}

internal static unsafe partial class Native
{
    private static int _done;

    /// <summary>
    /// The surrogate (dllhost.exe) doesn't search our package folder for DLLs: point the loader at the directory
    /// this DLL lives in, where Magick.Native-*.dll sits next to us.
    /// </summary>
    public static void EnsureMagickResolvable()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1)
            return;
        nint module;
        if (!GetModuleHandleExW(0x4 | 0x2, (nint)(delegate* unmanaged<int>)&Exports.DllCanUnloadNow, &module))   // FROM_ADDRESS | UNCHANGED_REFCOUNT
            return;
        var path = stackalloc char[1024];
        var length = GetModuleFileNameW(module, path, 1024);
        if (length > 0 && Path.GetDirectoryName(new string(path, 0, (int)length)) is { } dir)
            SetDllDirectoryW(dir);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, nint* module);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetModuleFileNameW(nint module, char* path, uint size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectoryW(string path);
}
