using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WpfChatClient.Services;
using WpfChatClient.Core.Interfaces;
using WpfChatClient.ViewModels;
using WpfChatClient.Infrastructure;

namespace WpfChatClient;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        var services = new ServiceCollection();

        // Register Infrastructure
        services.AddSingleton<MessageCache>();

        // Register Services
        services.AddSingleton<IChatService, ChatService>();
        services.AddSingleton<IEmojiService, EmojiService>();
        services.AddSingleton<IStickerService, StickerService>();
        services.AddSingleton<IFileTransferService, FileTransferService>();

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<ConnectViewModel>();
        services.AddSingleton<ChatViewModel>();

        // Register Windows
        services.AddSingleton<MainWindow>(s => new MainWindow
        {
            DataContext = s.GetRequiredService<MainViewModel>()
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Perform async initialization in the background to not block UI startup
        _ = InitializeAppAsync();
    }

    private async Task InitializeAppAsync()
    {
        try
        {
            var messageCache = _serviceProvider.GetRequiredService<MessageCache>();
            await messageCache.InitializeAsync();

            // Resolving the singleton ChatViewModel will trigger its history loading
            _serviceProvider.GetRequiredService<ChatViewModel>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[APP] Initialization error: {ex.Message}");
            MessageBox.Show($"Failed to initialize application: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.GetRequiredService<IChatService>().Disconnect();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Console.WriteLine($"[CRASH] Dispatcher Unhandled Exception: {e.Exception}");
        MessageBox.Show($"A critical error occurred: {e.Exception.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // Prevent app from closing
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Console.WriteLine($"[CRASH] Domain Unhandled Exception: {ex}");
        if (ex != null)
        {
            MessageBox.Show($"A fatal error occurred: {ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
