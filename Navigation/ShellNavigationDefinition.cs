namespace VcfEditor.Navigation;

public sealed record ShellNavigationDefinition(
    string Key,
    ShellDestination Destination,
    string Label,
    string IconGlyph,
    string GroupKey,
    string GroupLabel,
    int GroupOrder,
    int ItemOrder,
    string AccessKey,
    ShellCapability RequiredCapability = ShellCapability.None);
