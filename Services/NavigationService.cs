using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VcfEditor.Navigation;

namespace VcfEditor.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IPageFactory _pageFactory;

    public NavigationService(IPageFactory pageFactory)
    {
        ArgumentNullException.ThrowIfNull(pageFactory);
        _pageFactory = pageFactory;
    }

    public ShellDestination Current { get; private set; } = ShellDestination.Dashboard;

    public event EventHandler<NavigationChangedEventArgs>? Navigated;

    public async Task NavigateAsync(
        ShellDestination destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var page = _pageFactory.GetPage(destination)
            ?? throw new InvalidOperationException(
                $"The {destination} page is unavailable for the current phone connection.");

        await _pageFactory.InitializePageAsync(destination, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = Current;
        Current = destination;

        // Fire the Navigated event on the UI thread.
        // InitializePageAsync may internally use ConfigureAwait(false), so the
        // continuation above can be on a thread-pool thread.  ShellWindowViewModel
        // sets WPF-bound properties in its OnNavigated handler, which requires the
        // UI thread — marshal back here to prevent InvalidOperationException.
        var dispatcher = Application.Current?.Dispatcher;
        var args = new NavigationChangedEventArgs(previous, destination, page, previous == destination);

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Navigated?.Invoke(this, args);
        }
        else
        {
            await dispatcher.InvokeAsync(() => Navigated?.Invoke(this, args));
        }
    }
}
