using System.Text.RegularExpressions;
using Glance.Core;
using Glance.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Glance.App;

public partial class App : Application
{
    // ponytail: plain DI container, no Generic Host — Host startup cost hurts the cold-start budget (ADR 0001).
    public static IServiceProvider Services { get; } = new ServiceCollection()
        .AddSingleton<IImageDecoder, WicDecoder>()
        .AddSingleton<IImageDecoder, MagickDecoder>()   // registering doesn't load Magick (ADR 0003)
        .AddSingleton<DecoderRouter>()
        .AddSingleton<ImageLoader>()
        .AddSingleton<MainViewModel>()
        .BuildServiceProvider();

    private readonly AppActivationArguments _activation;
    private MainWindow? _window;

    public App(AppActivationArguments activation, AppInstance instance)
    {
        _activation = activation;
        InitializeComponent();

        if (IsPackaged)
            Settings.DataFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        UnhandledException += (_, e) => Log.Error("Unhandled exception", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Unobserved task exception", e.Exception);

        instance.Activated += (_, args) => _window?.DispatcherQueue.TryEnqueue(async () =>
        {
            _window.Activate();
            if (FileFrom(args) is { } path)
                await ViewModel.OpenAsync(path);
        });
    }

    private static MainViewModel ViewModel => Services.GetRequiredService<MainViewModel>();

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(ViewModel);
        _window.Activate();

        var path = FileFrom(_activation);
        if (path is not null)
            await ViewModel.OpenAsync(path);

        var settings = Settings.Load();
        if (!settings.FirstRunDone)
        {
            (settings with { FirstRunDone = true }).Save();
            // Defaults can only be chosen for a registered (packaged) app; the portable build skips this.
            if (IsPackaged)
                await _window.ShowOnboardingAsync();
        }
    }

    /// <summary>File from a file-association activation, or from the command line of a plain launch.</summary>
    private static string? FileFrom(AppActivationArguments args) => args.Data switch
    {
        IFileActivatedEventArgs file => file.Files.FirstOrDefault()?.Path,
        ILaunchActivatedEventArgs launch => FirstExistingPath(launch.Arguments),
        _ => null,
    };

    // Arguments may or may not start with the exe; take the first token that is an existing file or folder.
    private static string? FirstExistingPath(string? commandLine) =>
        commandLine is null ? null
        : Regex.Matches(commandLine, "\"([^\"]*)\"|(\\S+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .FirstOrDefault(t => !t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && (File.Exists(t) || Directory.Exists(t)));

    private static bool IsPackaged
    {
        get
        {
            try
            {
                return Windows.ApplicationModel.Package.Current is not null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
