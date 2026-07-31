using System;
using VcfEditor.Navigation;

namespace VcfEditor.Services;

public sealed class NavigationChangedEventArgs : EventArgs
{
    public NavigationChangedEventArgs(
        ShellDestination previous,
        ShellDestination current,
        object page,
        bool isReselection)
    {
        Previous = previous;
        Current = current;
        Page = page;
        IsReselection = isReselection;
    }

    public ShellDestination Previous { get; }
    public ShellDestination Current { get; }
    public object Page { get; }
    public bool IsReselection { get; }
}
