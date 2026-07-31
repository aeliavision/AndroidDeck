using FluentAssertions;
using VcfEditor.Core.IO;

namespace VcfEditor.Core.Tests;

public sealed class BoundedStreamCopyTests
{
    [Fact]
    public async Task CopyAsyncStreamsWithoutReadingBeyondLimit()
    {
        var payload = Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i % 251)).ToArray();
        await using var source = new MemoryStream(payload, writable: false);
        await using var destination = new MemoryStream();
        var progress = new List<long>();

        var copied = await BoundedStreamCopy.CopyAsync(
            source,
            destination,
            payload.Length,
            progress: new Progress<long>(value => progress.Add(value)),
            bufferSize: 4096,
            cancellationToken: CancellationToken.None);

        copied.Should().Be(payload.Length);
        destination.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task CopyAsyncThrowsTypedErrorWhenStreamExceedsLimit()
    {
        await using var source = new MemoryStream(new byte[1025], writable: false);
        await using var destination = new MemoryStream();

        var act = () => BoundedStreamCopy.CopyAsync(
            source,
            destination,
            1024,
            progress: null,
            bufferSize: 128,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TransferLimitExceededException>();
        exception.Which.LimitBytes.Should().Be(1024);
        destination.Length.Should().BeLessThanOrEqualTo(1024);
    }

    [Fact]
    public async Task CopyAsyncStopsWhenCancelled()
    {
        await using var source = new SlowInfiniteStream();
        await using var destination = new MemoryStream();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var act = () => BoundedStreamCopy.CopyAsync(
            source,
            destination,
            1024 * 1024,
            progress: null,
            bufferSize: 1024,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class SlowInfiniteStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken);
            buffer.Span.Fill(0x2A);
            return buffer.Length;
        }
    }
}
