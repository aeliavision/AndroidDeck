namespace VcfEditor.Navigation;

public sealed record ShellCapabilitySnapshot(
    bool IsPhoneConnected,
    bool SupportsFiles,
    bool SupportsGallery,
    bool SupportsBackup,
    bool RequiresAllFilesAccess,
    bool RequiresMediaPermissions,
    string? CapabilityError)
{
    public static ShellCapabilitySnapshot Disconnected { get; } = new(
        false,
        false,
        false,
        false,
        false,
        false,
        null);
}
