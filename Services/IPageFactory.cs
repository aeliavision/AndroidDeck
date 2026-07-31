using System;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Navigation;
using VcfEditor.Views;

namespace VcfEditor.Services;

public interface IPageFactory : IDisposable
{
    event EventHandler? MetricsChanged;

    DashboardView DashboardView { get; }
    ContactsView ContactsView { get; }
    int GalleryItemCount { get; }
    bool IsGalleryMetricsLoaded { get; }
    int BackupHistoryCount { get; }

    object? GetPage(ShellDestination destination);
    void SetPhoneClient(PhoneApiClient? client);
    void UpdateCapabilities(ShellCapabilitySnapshot capabilities);
    Task InitializePageAsync(
        ShellDestination destination,
        CancellationToken cancellationToken = default);
}
