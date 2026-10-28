using System.Runtime.CompilerServices;
using ImageMagick;
using ImageMagick.Configuration;

namespace Glance.Imaging;

/// <summary>How much one untrusted file may make ImageMagick allocate. Set per process (viewer vs. thumbnail host).</summary>
public sealed record MagickLimits(ulong MaxSide, ulong ListLength, ulong? MemoryBytes, ulong DiskBytes)
{
    /// <summary>The viewer: PSB's own maximum side; memory at Magick's default (half of RAM), then at most 16 GB of disk cache.</summary>
    public static readonly MagickLimits Viewer = new(300_000, 10_000, null, 16UL << 30);

    /// <summary>Explorer's thumbnail host runs on every file in a folder someone opens: much tighter, failures just show the icon.</summary>
    public static readonly MagickLimits Thumbnail = new(65_536, 1_000, 512UL << 20, 2UL << 30);
}

/// <summary>
/// Security policy and resource limits for every Magick call (ADR 0007). ImageMagick reads its policy once, on first
/// use, and silently ignores a later <c>Initialize</c> — so every entry point into Magick calls
/// <see cref="EnsureInitialized"/> before touching a Magick type.
/// </summary>
public static class MagickSetup
{
    // Ghostscript (PS/EPS/PDF/AI/XPS) runs as an external process if the user has it installed; MVG/MSVG/MSL/TEXT and the
    // URL coders can read local files or the network from inside an image. None of them is a format Glance opens through
    // Magick: PDF/AI go to Windows' PDF renderer and EPS shows its embedded preview instead (ADR 0009).
    // ImageMagick's own defaults (EPHEMERAL, MVG, MSL, @file and |pipe paths) are repeated here because this replaces them.
    private const string Policy = """
        <policymap>
          <policy domain="coder" rights="none" pattern="{EPHEMERAL,MVG,MSVG,MSL,TEXT,TXT,URL,HTTP,HTTPS,FTP,PS,PS2,PS3,EPS,EPI,EPSI,EPSF,EPT,PDF,PDFA,XPS,AI}" />
          <policy domain="path" rights="none" pattern="@*" />
          <policy domain="path" rights="none" pattern="|*" />
        </policymap>
        """;

    private static readonly Lock Gate = new();
    private static volatile bool _initialized;

    /// <summary>Limits applied at initialization. Set before the first Magick call; later changes have no effect.</summary>
    public static MagickLimits Limits { get; set; } = MagickLimits.Viewer;

    public static void EnsureInitialized()
    {
        if (_initialized)
            return;
        lock (Gate)
        {
            if (_initialized)
                return;
            Initialize(Limits);
            _initialized = true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Initialize(MagickLimits limits)
    {
        var config = ConfigurationFiles.Default;
        config.Policy.Data = Policy;

        // Initialize(config, path) leaves existing files alone, so an older policy would win: write them ourselves.
        // One folder per process kind, each file replaced atomically, so the viewer and the thumbnail host never read a half-written file.
        var host = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "host";
        var folder = Path.Combine(Path.GetTempPath(), "Glance", $"magick-{host}");
        Directory.CreateDirectory(folder);
        foreach (var file in config.All)
        {
            var target = Path.Combine(folder, file.FileName);
            var temp = $"{target}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temp, file.Data);
            File.Move(temp, target, overwrite: true);
        }
        MagickNET.Initialize(folder);

        ResourceLimits.Width = limits.MaxSide;
        ResourceLimits.Height = limits.MaxSide;
        ResourceLimits.ListLength = limits.ListLength;
        if (limits.MemoryBytes is { } memory)
            ResourceLimits.Memory = memory;
        ResourceLimits.Disk = limits.DiskBytes;
        // ponytail: no ResourceLimits.Time — ImageMagick measures it once per process, not per image, so a viewer left open
        // would eventually fail every decode. Per-decode time limits belong with cancellation (Progress event).
    }
}
