using System;
using FluentAssertions;
using VcfEditor.Navigation;
using Xunit;

namespace VcfEditor.UI.Tests.Navigation;

public sealed class ShellLayoutPolicyTests
{
    [Theory]
    [InlineData(1600, ShellLayoutMode.Expanded)]
    [InlineData(1200, ShellLayoutMode.Expanded)]
    [InlineData(1199, ShellLayoutMode.Compact)]
    [InlineData(900, ShellLayoutMode.Compact)]
    [InlineData(899, ShellLayoutMode.Overlay)]
    [InlineData(640, ShellLayoutMode.Overlay)]
    public void GetDefaultModeMapsWidthToExpectedMode(double width, ShellLayoutMode expected)
    {
        ShellLayoutPolicy.GetDefaultMode(width).Should().Be(expected);
    }

    [Theory]
    [InlineData(1600, ShellLayoutMode.Expanded, ShellLayoutMode.Expanded)]
    [InlineData(1600, ShellLayoutMode.Compact, ShellLayoutMode.Compact)]
    [InlineData(1199, ShellLayoutMode.Expanded, ShellLayoutMode.Compact)]
    [InlineData(899, ShellLayoutMode.Expanded, ShellLayoutMode.Overlay)]
    public void GetEffectiveModeHonorsPreferenceOnlyWhenTheWindowIsWideEnough(
        double width,
        ShellLayoutMode preferred,
        ShellLayoutMode expected)
    {
        ShellLayoutPolicy.GetEffectiveMode(width, preferred).Should().Be(expected);
    }

    [Fact]
    public void OverlayCannotBeSavedAsADesktopPreference()
    {
        FluentActions.Invoking(() =>
                ShellLayoutPolicy.GetEffectiveMode(1600, ShellLayoutMode.Overlay))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidWidthIsRejected(double width)
    {
        FluentActions.Invoking(() => ShellLayoutPolicy.GetDefaultMode(width))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
