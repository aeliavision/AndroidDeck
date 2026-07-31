using FluentAssertions;
using VcfEditor.Behaviors;
using VcfEditor.Models;

namespace VcfEditor.UI.Tests;

public sealed class ResponsiveLayoutBehaviorTests
{
    [Theory]
    [InlineData(0, ResponsiveLayoutMode.Expanded)]
    [InlineData(899, ResponsiveLayoutMode.Compact)]
    [InlineData(900, ResponsiveLayoutMode.Medium)]
    [InlineData(1199, ResponsiveLayoutMode.Medium)]
    [InlineData(1200, ResponsiveLayoutMode.Expanded)]
    public void ResolveModeUsesDocumentedBreakpoints(double width, ResponsiveLayoutMode expected)
    {
        ResponsiveLayoutBehavior.ResolveMode(width).Should().Be(expected);
    }
}
