namespace VcfEditor.Navigation;

public sealed record ShellNavigationAvailability(bool IsVisible, bool IsEnabled, string? DisabledReason)
{
    public static ShellNavigationAvailability Available { get; } = new(true, true, null);
}
