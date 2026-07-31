using FluentAssertions;
using VcfEditor.Services.Performance;

namespace VcfEditor.UI.Tests;

public sealed class UiThreadStallMonitorTests
{
    [Fact]
    public void CalculateStallReturnsOnlyDelayBeyondSamplingInterval()
    {
        UiThreadStallMonitor.CalculateStall(TimeSpan.FromMilliseconds(450))
            .Should().Be(TimeSpan.Zero);

        UiThreadStallMonitor.CalculateStall(TimeSpan.FromMilliseconds(875))
            .Should().Be(TimeSpan.FromMilliseconds(375));
    }
}
