using System.Runtime.InteropServices;

namespace Glance.Core;

public static unsafe partial class RecycleBin
{
    private const uint CLSCTX_ALL = 0x17;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    // FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI | FOF_WANTNUKEWARNING | FOFX_RECYCLEONDELETE
    // Guarantees Recycle Bin only: if the volume has no Recycle Bin (e.g. USB/UNC), aborts safely instead of permanently deleting.
    private const uint RecycleFlags = 0x0004 | 0x0010 | 0x0040 | 0x0400 | 0x4000 | 0x00080000;

    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>Moves a file to the Recycle Bin via COM <c>IFileOperation</c> (long-path safe), never deletes permanently, never shows shell UI.</summary>
    /// <exception cref="IOException">The shell refused (file in use, no recycle bin on that volume, …).</exception>
    public static void Send(string path)
    {
        var full = Path.GetFullPath(path);
        // IFileOperation requires STA; thread-pool threads are MTA, so run on a short-lived STA thread when needed.
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            SendOnSta(full);
            return;
        }
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { SendOnSta(full); }
            catch (Exception ex) { error = ex; }
        });
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }

    private static void SendOnSta(string fullPath)
    {
        var init = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        var uninit = init is 0 or 1;   // S_OK or S_FALSE
        if (!uninit && init != RPC_E_CHANGED_MODE)
            throw new IOException($"COM initialization failed (0x{init:X8}).");

        nint op = 0, item = 0;
        try
        {
            var clsid = ClsidFileOperation;
            var iidOp = IidFileOperation;
            var hr = CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iidOp, &op);
            if (hr < 0 || op == 0)
                throw new IOException($"Could not create IFileOperation (0x{hr:X8}).");

            var iidItem = IidShellItem;
            hr = SHCreateItemFromParsingName(fullPath, 0, &iidItem, &item);
            if (hr < 0 || item == 0)
                throw new IOException($"Could not open '{fullPath}' for Recycle Bin (0x{hr:X8}).");

            // IFileOperation vtable: 5 = SetOperationFlags, 18 = DeleteItem, 21 = PerformOperations, 22 = GetAnyOperationsAborted.
            var vt = *(void***)op;
            var setFlags = (delegate* unmanaged[Stdcall]<nint, uint, int>)vt[5];
            var deleteItem = (delegate* unmanaged[Stdcall]<nint, nint, nint, int>)vt[18];
            var perform = (delegate* unmanaged[Stdcall]<nint, int>)vt[21];
            var aborted = (delegate* unmanaged[Stdcall]<nint, int*, int>)vt[22];

            hr = setFlags(op, RecycleFlags);
            if (hr < 0)
                throw new IOException($"Could not configure Recycle Bin operation (0x{hr:X8}).");

            hr = deleteItem(op, item, 0);
            if (hr < 0)
                throw new IOException($"Could not queue '{fullPath}' for Recycle Bin (0x{hr:X8}).");

            hr = perform(op);
            int anyAborted = 0;
            _ = aborted(op, &anyAborted);
            if (hr < 0 || anyAborted != 0)
                throw new IOException($"Could not move '{fullPath}' to the Recycle Bin (0x{hr:X8}).");
        }
        finally
        {
            if (item != 0)
                Release(item);
            if (op != 0)
                Release(op);
            if (uninit)
                CoUninitialize();
        }
    }

    private static void Release(nint unknown)
    {
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unknown)[2];
        release(unknown);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* rclsid, nint outer, uint clsContext, Guid* riid, nint* result);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindCtx, Guid* riid, nint* item);
}
