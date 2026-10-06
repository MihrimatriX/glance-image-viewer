using Glance.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Glance.App;

public partial class App : Application
{
    // ponytail: plain DI container, no Generic Host — Host startup cost hurts the cold-start budget (see ADR 0001).
    public static IServiceProvider Services { get; } = new ServiceCollection()
        .AddSingleton<IImageDecoder, WicDecoder>()
        .AddSingleton<DecoderRouter>()
        .AddTransient<MainViewModel>()
        .BuildServiceProvider();

    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var vm = Services.GetRequiredService<MainViewModel>();
        _window = new MainWindow(vm);
        _window.Activate();

        if (Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() is { } path)
            _ = vm.OpenAsync(path);
    }
}
