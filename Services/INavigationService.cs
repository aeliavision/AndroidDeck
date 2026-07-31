using System;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Navigation;

namespace VcfEditor.Services;

public interface INavigationService
{
    ShellDestination Current { get; }
    event EventHandler<NavigationChangedEventArgs>? Navigated;

    Task NavigateAsync(
        ShellDestination destination,
        CancellationToken cancellationToken = default);
}
