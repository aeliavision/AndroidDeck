using FluentAssertions;
using VcfEditor.Core;

namespace VcfEditor.UI.Tests;

public sealed class ShellCapabilityStateTests
{
    [Fact]
    public void FromStatusJsonUsesConservativeLegacyCapabilitiesWhenFieldsAreMissing()
    {
        const string json = """{"deviceName":"Pixel"}""";

        var state = CapabilityState.FromStatusJson(json);

        state.SupportsFiles.Should().BeTrue();
        state.SupportsGallery.Should().BeTrue();
        state.SupportsBackup.Should().BeFalse();
        state.RequiresAllFilesAccess.Should().BeFalse();
        state.RequiresMediaPermissions.Should().BeFalse();
    }

    [Fact]
    public void FromStatusJsonMapsModernCapabilityFields()
    {
        const string json = """
            {
              "supportsFiles": true,
              "supportsGallery": false,
              "supportsBackup": true,
              "requiresAllFilesAccess": true,
              "requiresMediaPermissions": false
            }
            """;

        var state = CapabilityState.FromStatusJson(json);

        state.Should().Be(new CapabilityState(
            SupportsFiles: true,
            SupportsGallery: false,
            SupportsBackup: true,
            RequiresAllFilesAccess: true,
            RequiresMediaPermissions: false));
    }

    [Fact]
    public void FromStatusJsonRejectsEmptyPayload()
    {
        var act = () => CapabilityState.FromStatusJson("  ");

        act.Should().Throw<ArgumentException>();
    }
}
