using System.Collections.Generic;

namespace VcfEditor.Navigation;

public sealed class ShellNavigationRegistry : IShellNavigationRegistry
{
    private static readonly IReadOnlyList<ShellNavigationDefinition> Items =
    [
        new("dashboard", ShellDestination.Dashboard, "Dashboard", "\uE80F", "overview", "Overview", 0, 0, "Alt+D"),
        new("contacts", ShellDestination.Contacts, "Contacts", "\uE77B", "phone-data", "Phone Data", 1, 0, "Alt+C"),
        new("files", ShellDestination.FileBrowser, "Files", "\uE8B7", "phone-data", "Phone Data", 1, 1, "Alt+F", ShellCapability.Files),
        new("gallery", ShellDestination.Gallery, "Gallery", "\uE91B", "phone-data", "Phone Data", 1, 2, "Alt+G", ShellCapability.Gallery),
        new("backup", ShellDestination.Backup, "Backup & Restore", "\uE74E", "transfers", "Transfers", 2, 0, "Alt+B", ShellCapability.Backup),
        new("settings", ShellDestination.Settings, "Settings", "\uE713", "system", "System", 3, 0, "Alt+S")
    ];

    public IReadOnlyList<ShellNavigationDefinition> Definitions => Items;
}
