using System.Windows;
using System.Windows.Threading;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class App : Application
{
    public App()
    {
        // Implicit styles use the exact runtime type. Dialog subclasses of Window
        // therefore need the shared base style explicitly, unless they supply one.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window && window is not StartupLoadingWindow && window.Style is null &&
                    window.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
                    window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
            }));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window) Services.ModalBackdrop.Attach(window); }));
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Keep the process alive while preferences load, without creating an
        // unthemed window. SQLite remains authoritative over the JSON backup.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartupLoadingWindow? splash = null;
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        delay.Tick += (_, _) => { delay.Stop(); splash ??= new StartupLoadingWindow(); splash.Show(); };
        delay.Start();
        try
        {
            splash?.SetStatus("Protecting saved race data…");
            await DataProtectionService.PrepareAsync();
            var settingsService = new SettingsService();
            var database = new RaceControlDatabaseService();
            splash?.SetStatus("Opening race database…");
            await database.InitializeAsync();
            var databaseSettings = await database.LoadApplicationStateAsync();
            var settings = databaseSettings ?? await settingsService.LoadAsync();
            // Imported tones used absolute paths in earlier versions. Point only copied
            // application-owned audio at its new location; external files stay unchanged.
            foreach (var property in typeof(IMRCRaceControl.Models.AppSettings).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("ToneFile",StringComparison.Ordinal)))
            {
                var oldPath = property.GetValue(settings) as string;
                var legacyPrefix = ApplicationDataPaths.LegacyDirectory + Path.DirectorySeparatorChar;
                if (oldPath?.StartsWith(legacyPrefix,StringComparison.OrdinalIgnoreCase) == true)
                {
                    var imported = Path.Combine(ApplicationDataPaths.DataDirectory,oldPath[legacyPrefix.Length..]);
                    if (File.Exists(imported)) property.SetValue(settings,imported);
                }
            }
            SettingsService.Normalize(settings);
            if (databaseSettings is null) await database.SaveApplicationStateAsync(settings);
            string? startupWarning = null;

            ThemeService.Apply(settings.Theme);
            ThemeService.ApplyDashboard(settings);
            // Render a loading screen before the synchronous dashboard construction.
            delay.Stop();
            splash ??= new StartupLoadingWindow();
            splash.SetStatus("Preparing dashboard…");
            splash.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var window = new MainWindow(settings, settingsService, database, startupWarning);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            window.Show();
            splash.Close();
            _ = window.CheckApplicationUpdatesAsync(manual: false);
        }
        catch (Exception ex)
        {
            delay.Stop(); splash?.Close();
            var path = AppCrashLogger.Log("Application startup", ex);
            MessageBox.Show($"Race Control could not start.\n\n{ex.Message}\n\nYour data has not been reset.\nBackups: {ApplicationDataPaths.BackupDirectory}\n\nDiagnostic log:\n{path}",
                "IMRC Race Control", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var path = AppCrashLogger.Log("Unhandled WPF UI exception", e.Exception);

        MessageBox.Show(
            $"Race Control caught an unexpected error instead of closing silently.\n\n" +
            $"{e.Exception.Message}\n\nDiagnostic log:\n{path}",
            "IMRC Race Control",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Most UI/event-handler exceptions are recoverable. Database and
        // persistence operations already have their own guarded save paths.
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            AppCrashLogger.Log("Unhandled AppDomain exception", exception);
        else
            AppCrashLogger.Log("Unhandled AppDomain exception", new Exception(e.ExceptionObject?.ToString()));
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppCrashLogger.Log("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
