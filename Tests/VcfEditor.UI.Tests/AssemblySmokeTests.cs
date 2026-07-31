using FluentAssertions;
using VcfEditor.Views;

namespace VcfEditor.UI.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void ShellWindowIsAvailableFromApplicationAssembly()
    {
        typeof(ShellWindow).Assembly.GetName().Name.Should().Be("AndroidDeck");
    }
}
