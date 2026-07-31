using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Core.Polling;

internal sealed class OperationPollingTimeoutException : TimeoutException
{
    public OperationPollingTimeoutException(TimeSpan timeout, int attempts)
        : base($"The remote operation did not complete within {timeout} after {attempts} polling attempts.")
    {
        Timeout = timeout;
        Attempts = attempts;
    }

    public TimeSpan Timeout { get; }
    public int Attempts { get; }
}

internal sealed class RemoteOperationFailedException : Exception
{
    public RemoteOperationFailedException(int attempts, string? remoteMessage = null)
        : base(string.IsNullOrWhiteSpace(remoteMessage)
            ? $"The remote operation failed after {attempts} polling attempts."
            : remoteMessage)
    {
        Attempts = attempts;
    }

    public int Attempts { get; }
}

internal static class OperationPollingPolicy
{
    public static async Task<TStatus> PollAsync<TStatus>(
        Func<CancellationToken, Task<TStatus>> pollAsync,
        Func<TStatus, bool> isSuccess,
        Func<TStatus, bool> isFailure,
        TimeSpan interval,
        TimeSpan timeout,
        int maxAttempts,
        IProgress<TStatus>? progress = null,
        Func<TStatus, string?>? failureMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pollAsync);
        ArgumentNullException.ThrowIfNull(isSuccess);
        ArgumentNullException.ThrowIfNull(isFailure);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        var stopwatch = Stopwatch.StartNew();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= timeout)
                throw new OperationPollingTimeoutException(timeout, attempt - 1);

            var remainingBeforeRequest = timeout - stopwatch.Elapsed;
            if (remainingBeforeRequest <= TimeSpan.Zero)
                throw new OperationPollingTimeoutException(timeout, attempt - 1);

            using var attemptCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(remainingBeforeRequest);

            TStatus status;
            try
            {
                status = await pollAsync(attemptCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                attemptCancellation.IsCancellationRequested)
            {
                throw new OperationPollingTimeoutException(timeout, attempt);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(status);

            if (isSuccess(status))
                return status;
            if (isFailure(status))
                throw new RemoteOperationFailedException(attempt, failureMessage?.Invoke(status));

            if (attempt == maxAttempts)
                throw new OperationPollingTimeoutException(timeout, attempt);

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new OperationPollingTimeoutException(timeout, attempt);

            var delay = interval <= remaining ? interval : remaining;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The polling policy exited unexpectedly.");
    }
}
