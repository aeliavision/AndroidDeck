using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Core.Settings;
using VcfEditor.Hosting;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.Views;

namespace VcfEditor;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VcfEditor",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        var logFilePath = Path.Combine(logDirectory, $"VcfEditor_{DateTime.Now:yyyyMMdd}.log");

        _host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider((context, options) =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new FileLoggerProvider(logFilePath));
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<Application>(this);
                services.AddAndroidDeckDesktop(context.Configuration);
            })
            .Build();

        _host.StartAsync().GetAwaiter().GetResult();

        var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();
        AppLoggerFactory.Initialize(loggerFactory);
        var logger = loggerFactory.CreateLogger<App>();
        LogMessages.ApplicationStarted(logger);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                LogMessages.AppDomainUnhandledException(logger, exception);
            else
                LogMessages.AppDomainUnhandledExceptionObject(logger, args.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogMessages.UnobservedTaskException(
                logger,
                args.Exception.InnerException ?? args.Exception);
            args.SetObserved();
        };

        var themeService = _host.Services.GetRequiredService<IThemeService>();
        var settingsStore = _host.Services.GetRequiredService<IAppSettingsStore>();
        themeService.Apply(settingsStore.GetTheme());

        var applicationExceptionHandler =
            _host.Services.GetRequiredService<IApplicationExceptionHandler>();
        DispatcherUnhandledException += (_, args) =>
        {
            var canContinue = applicationExceptionHandler.Handle(args.Exception);
            args.Handled = canContinue;
            if (!canContinue)
                Shutdown(-1);
        };

        var shell = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow = shell;
        shell.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var logger = _host.Services.GetService<ILogger<App>>();
            if (logger is not null)
                LogMessages.ApplicationExiting(logger);

            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            finally
            {
                AppLoggerFactory.Shutdown();
                _host.Dispose();
                _host = null;
            }
        }

        base.OnExit(e);
    }
}
