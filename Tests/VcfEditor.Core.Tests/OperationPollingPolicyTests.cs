using FluentAssertions;
using VcfEditor.Core.Polling;

namespace VcfEditor.Core.Tests;

public sealed class OperationPollingPolicyTests
{
    private sealed record Status(string Phase);

    [Fact]
    public async Task PollAsyncReturnsTerminalSuccess()
    {
        var phases = new Queue<string>(["working", "working", "ready"]);

        var result = await OperationPollingPolicy.PollAsync(
            _ => Task.FromResult(new Status(phases.Dequeue())),
            status => status.Phase == "ready",
            status => status.Phase == "failed",
            interval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(1),
            maxAttempts: 10,
            cancellationToken: CancellationToken.None);

        result.Phase.Should().Be("ready");
    }

    [Fact]
    public async Task PollAsyncThrowsTypedFailureForTerminalFailure()
    {
        var act = () => OperationPollingPolicy.PollAsync(
            _ => Task.FromResult(new Status("failed")),
            status => status.Phase == "ready",
            status => status.Phase == "failed",
            interval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(1),
            maxAttempts: 10,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RemoteOperationFailedException>();
        exception.Which.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task PollAsyncStopsAtAttemptLimit()
    {
        var act = () => OperationPollingPolicy.PollAsync(
            _ => Task.FromResult(new Status("working")),
            status => status.Phase == "ready",
            status => status.Phase == "failed",
            interval: TimeSpan.Zero,
            timeout: TimeSpan.FromMinutes(1),
            maxAttempts: 3,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<OperationPollingTimeoutException>();
        exception.Which.Attempts.Should().Be(3);
    }


    [Fact]
    public async Task PollAsyncConvertsPerRequestTimeoutToTypedTimeout()
    {
        var act = () => OperationPollingPolicy.PollAsync(
            async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new Status("working");
            },
            status => status.Phase == "ready",
            status => status.Phase == "failed",
            interval: TimeSpan.Zero,
            timeout: TimeSpan.FromMilliseconds(100),
            maxAttempts: 3,
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<OperationPollingTimeoutException>();
    }

    [Fact]
    public async Task PollAsyncObservesCancellationAfterRequestReturns()
    {
        using var cts = new CancellationTokenSource();
        var act = () => OperationPollingPolicy.PollAsync(
            _ =>
            {
                cts.Cancel();
                return Task.FromResult(new Status("working"));
            },
            status => status.Phase == "ready",
            status => status.Phase == "failed",
            interval: TimeSpan.Zero,
            timeout: TimeSpan.FromMinutes(1),
            maxAttempts: 3,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PollAsyncObservesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => OperationPollingPolicy.PollAsync(
            _ => Task.FromResult(new Status("working")),
            status => status.Phase == "ready",
            status => status.Phase == "failed",
            interval: TimeSpan.Zero,
            timeout: TimeSpan.FromMinutes(1),
            maxAttempts: 3,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
