using System;
using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VcfEditor.Core;
using VcfEditor.Core.Security;
using VcfEditor.Core.Settings;
using VcfEditor.Features.Backup;
using VcfEditor.Features.Contacts;
using VcfEditor.Features.Files;
using VcfEditor.Features.Gallery;
using VcfEditor.Features.PhoneSession;
using VcfEditor.Navigation;
using VcfEditor.Services;
using VcfEditor.Services.Settings;
using VcfEditor.Services.Performance;
using VcfEditor.ViewModels;
using VcfEditor.Views;

namespace VcfEditor.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAndroidDeckDesktop(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<ISecretStore, WindowsDpapiSecretStore>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<VcfParser>();

        services.AddSingleton<IContactFileWorkflow, ContactFileWorkflow>();
        services.AddSingleton<ContactsViewModel>(provider => new ContactsViewModel(
            provider.GetRequiredService<IContactFileWorkflow>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<ContactsViewModel>>()));
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<IContactEditorWorkflow, ContactEditorWorkflow>();
        services.AddSingleton<IContactMetrics>(provider => provider.GetRequiredService<ContactsViewModel>());
        services.AddSingleton<IUserNotificationService, UserNotificationService>();
        services.AddSingleton<IDiagnosticExportService, DiagnosticExportService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<IShellNavigationRegistry, ShellNavigationRegistry>();
        services.AddSingleton<IPageFactory, PageFactory>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ShellWindowViewModel>();
        services.AddSingleton<IShellConnectionCoordinator, ShellConnectionCoordinator>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IApplicationExceptionHandler, ApplicationExceptionHandler>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddHostedService<UiThreadStallMonitor>();

        services.AddSingleton<DashboardView>();
        services.AddSingleton<ContactsView>();
        services.AddSingleton<GroupsView>();
        services.AddSingleton<SettingsView>();
        services.AddTransient<ShellWindow>();

        services.AddScoped<PhoneSessionContext>();
        services.AddScoped(provider => provider.GetRequiredService<PhoneSessionContext>().Client);
        services.AddScoped(provider => provider.GetRequiredService<PhoneSessionContext>().Client.FileSystem);
        services.AddScoped(provider => provider.GetRequiredService<PhoneSessionContext>().Client.Gallery);
        services.AddScoped(provider => new BackupApi(
            provider.GetRequiredService<PhoneSessionContext>().Client.Transport,
            provider.GetRequiredService<IAppSettingsStore>()));

        services.AddScoped<IFileTransferWorkflow, FileTransferWorkflow>();
        services.AddScoped<ILocalUploadPlanner, LocalUploadPlanner>();
        services.AddScoped<FileBrowserViewModel>();
        services.AddScoped<IFileBrowserInteraction, FileBrowserInteraction>();
        services.AddScoped<FileBrowserView>();

        services.AddScoped<IGalleryTransferWorkflow, GalleryTransferWorkflow>();
        services.AddScoped<GalleryViewModel>();
        services.AddScoped<IGalleryInteraction, GalleryInteraction>();
        services.AddScoped<GalleryView>();

        services.AddScoped<IBackupWorkflow, BackupWorkflow>();
        services.AddScoped<IRestoreWorkflow, RestoreWorkflow>();
        services.AddScoped<IBackupHistoryService, BackupHistoryService>();
        services.AddScoped<IBackupArchiveService, BackupArchiveService>();
        services.AddScoped<BackupViewModel>();
        services.AddScoped<BackupView>();

        services.AddSingleton<IPhoneSessionScopeFactory, PhoneSessionScopeFactory>();
        return services;
    }
}
