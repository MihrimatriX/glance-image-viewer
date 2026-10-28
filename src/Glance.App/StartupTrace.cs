using System.Diagnostics;

namespace Glance.App;

/// <summary>
/// Cold-start measurement for <c>benchmarks/measure-startup.ps1</c>: with <c>GLANCE_STARTUP_TRACE</c> set to a file path,
/// appends the milliseconds from process creation to the first drawn image (the 400 ms budget, ADR 0001). Off otherwise.
/// </summary>
internal static class StartupTrace
{
    private static readonly string? Target = Environment.GetEnvironmentVariable("GLANCE_STARTUP_TRACE");
    private static bool _done;

    public static void FirstImageDrawn()
    {
        if (_done || Target is null)
            return;
        _done = true;
        try
        {
            using var process = Process.GetCurrentProcess();
            var ms = (DateTime.Now - process.StartTime).TotalMilliseconds;
            File.AppendAllText(Target, $"{ms:0}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A measurement aid must never take the viewer down.
        }
    }
}
