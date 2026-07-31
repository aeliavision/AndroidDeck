using System.Linq;
using FluentAssertions;
using VcfEditor.Navigation;
using Xunit;

namespace VcfEditor.UI.Tests.Navigation;

public sealed class ShellNavigationPolicyTests
{
    [Fact]
    public void LocalDestinationsRemainAvailableWithoutAPhone()
    {
        var definitions = new ShellNavigationRegistry().Definitions;
        var snapshot = ShellCapabilitySnapshot.Disconnected;

        ShellNavigationPolicy.Evaluate(definitions.Single(x => x.Destination == ShellDestination.Dashboard), snapshot)
            .IsEnabled.Should().BeTrue();
        ShellNavigationPolicy.Evaluate(definitions.Single(x => x.Destination == ShellDestination.Contacts), snapshot)
            .IsEnabled.Should().BeTrue();
        ShellNavigationPolicy.Evaluate(definitions.Single(x => x.Destination == ShellDestination.Settings), snapshot)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void DisconnectedPhoneDestinationExplainsTheRequiredAction()
    {
        var definition = new ShellNavigationRegistry().Definitions
            .Single(x => x.Destination == ShellDestination.FileBrowser);

        var result = ShellNavigationPolicy.Evaluate(definition, ShellCapabilitySnapshot.Disconnected);

        result.IsEnabled.Should().BeFalse();
        result.DisabledReason.Should().Be("Connect a phone to browse files.");
    }

    [Fact]
    public void MissingMediaPermissionIsExplainedWithoutHidingTheDestination()
    {
        var definition = new ShellNavigationRegistry().Definitions
            .Single(x => x.Destination == ShellDestination.Gallery);
        var snapshot = new ShellCapabilitySnapshot(
            true,
            true,
            false,
            true,
            false,
            true,
            null);

        var result = ShellNavigationPolicy.Evaluate(definition, snapshot);

        result.IsVisible.Should().BeTrue();
        result.IsEnabled.Should().BeFalse();
        result.DisabledReason.Should().Contain("permission is missing");
    }

    [Fact]
    public void CapabilityDiscoveryErrorIsExposedAsTheDisabledReason()
    {
        var definition = new ShellNavigationRegistry().Definitions
            .Single(x => x.Destination == ShellDestination.FileBrowser);
        var snapshot = new ShellCapabilitySnapshot(
            true,
            false,
            false,
            false,
            false,
            false,
            "Capability check failed. Select Retry.");

        var result = ShellNavigationPolicy.Evaluate(definition, snapshot);

        result.IsEnabled.Should().BeFalse();
        result.DisabledReason.Should().Be("Capability check failed. Select Retry.");
    }
}
