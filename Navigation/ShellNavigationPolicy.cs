namespace VcfEditor.Navigation;

public static class ShellNavigationPolicy
{
    public static ShellNavigationAvailability Evaluate(
        ShellNavigationDefinition definition,
        ShellCapabilitySnapshot snapshot)
    {
        if (definition.RequiredCapability == ShellCapability.None)
            return ShellNavigationAvailability.Available;

        // Phone not connected — disable all capability-gated items.
        if (!snapshot.IsPhoneConnected)
        {
            var disconnectedReason = definition.RequiredCapability switch
            {
                ShellCapability.Files => "Connect a phone to browse files.",
                ShellCapability.Gallery => "Connect a phone to browse photos and videos.",
                ShellCapability.Backup => "Connect a phone to create or restore backups.",
                _ => "Connect a phone to open this section."
            };
            return new(true, false, disconnectedReason);
        }

        // Phone IS connected — check what the companion app supports.
        var supported = definition.RequiredCapability switch
        {
            ShellCapability.Files => snapshot.SupportsFiles,
            ShellCapability.Gallery => snapshot.SupportsGallery,
            ShellCapability.Backup => snapshot.SupportsBackup,
            _ => true
        };

        if (supported)
            return ShellNavigationAvailability.Available;

        // Files/Gallery: missing Android permission — allow navigation.
        // The screen itself shows a "Grant permission" empty state.
        // Only block when the app fundamentally doesn't support the feature.
        var isPermissionIssue = definition.RequiredCapability switch
        {
            ShellCapability.Files => snapshot.RequiresAllFilesAccess,
            ShellCapability.Gallery => snapshot.RequiresMediaPermissions,
            _ => false
        };

        if (isPermissionIssue)
            return ShellNavigationAvailability.Available;

        // Capability error during discovery (e.g. timeout) — allow navigation
        // so the user can see and retry from within the screen.
        if (!string.IsNullOrWhiteSpace(snapshot.CapabilityError))
            return ShellNavigationAvailability.Available;

        // The companion app genuinely doesn't support this feature at all.
        var reason = definition.RequiredCapability switch
        {
            ShellCapability.Backup =>
                "The connected companion app does not support Backup & Restore. Update it, then reconnect.",
            _ => $"{definition.Label} is not supported by the connected companion app. Update the companion app."
        };

        return new(true, false, reason);
    }
}
