using System.Linq;
using FluentAssertions;
using VcfEditor.Navigation;
using Xunit;

namespace VcfEditor.UI.Tests.Navigation;

public sealed class ShellNavigationRegistryTests
{
    [Fact]
    public void DefinitionsAreUniqueOrderedAndGrouped()
    {
        var registry = new ShellNavigationRegistry();
        var definitions = registry.Definitions;

        definitions.Should().HaveCount(6);
        definitions.Select(item => item.Destination).Should().OnlyHaveUniqueItems();
        definitions.Select(item => item.Key).Should().OnlyHaveUniqueItems();
        definitions.Select(item => item.AccessKey).Should().OnlyHaveUniqueItems();
        definitions.Select(item => item.GroupLabel).Distinct().Should().ContainInOrder(
            "Overview", "Phone Data", "Transfers", "System");
    }

    [Fact]
    public void PhoneFeaturesDeclareTheirRequiredCapability()
    {
        var definitions = new ShellNavigationRegistry().Definitions;

        definitions.Single(item => item.Destination == ShellDestination.FileBrowser)
            .RequiredCapability.Should().Be(ShellCapability.Files);
        definitions.Single(item => item.Destination == ShellDestination.Gallery)
            .RequiredCapability.Should().Be(ShellCapability.Gallery);
        definitions.Single(item => item.Destination == ShellDestination.Backup)
            .RequiredCapability.Should().Be(ShellCapability.Backup);
    }
}
