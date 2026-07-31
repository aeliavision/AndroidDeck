using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Core.IO;

internal sealed class TransferLimitExceededException : IOException
{
    public TransferLimitExceededException(long limitBytes, long observedBytes)
        : base($"The transfer exceeded the configured limit of {limitBytes:N0} bytes.")
    {
        LimitBytes = limitBytes;
        ObservedBytes = observedBytes;
    }

    public long LimitBytes { get; }
    public long ObservedBytes { get; }
}

internal static class BoundedStreamCopy
{
    public static void ValidateDeclaredLength(long? declaredLength, long limitBytes, string contentName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limitBytes);
        if (declaredLength is > 0 && declaredLength.Value > limitBytes)
        {
            throw new TransferLimitExceededException(limitBytes, declaredLength.Value);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentName);
    }

    public static async Task<long> CopyAsync(
        Stream source,
        Stream destination,
        long limitBytes,
        IProgress<long>? progress = null,
        int bufferSize = TransferLimits.DefaultStreamBufferBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.CanRead) throw new ArgumentException("Source stream must be readable.", nameof(source));
        if (!destination.CanWrite) throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegative(limitBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        long copied = 0;
        try
        {
            for (;;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = limitBytes - copied;
                var readSize = remaining >= bufferSize
                    ? bufferSize
                    : checked((int)Math.Max(1, remaining + 1));

                var read = await source.ReadAsync(
                        buffer.AsMemory(0, readSize),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    return copied;

                if (read > remaining)
                    throw new TransferLimitExceededException(limitBytes, copied + read);

                await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
                copied += read;
                progress?.Report(copied);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<byte[]> ReadAllBytesAsync(
        Stream source,
        long limitBytes,
        CancellationToken cancellationToken = default)
    {
        await using var destination = new MemoryStream();
        await CopyAsync(source, destination, limitBytes, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return destination.ToArray();
    }
}
