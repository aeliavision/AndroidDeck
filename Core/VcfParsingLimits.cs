namespace VcfEditor.Core;

internal static class VcfParsingLimits
{
    public const int MaxLineCharacters = 1_048_576;
    public const long MaxInputCharacters = 512L * 1024 * 1024;
    public const long MaxFileBytes = 512L * 1024 * 1024;
    public const int MaxContactCount = 1_000_000;
}
