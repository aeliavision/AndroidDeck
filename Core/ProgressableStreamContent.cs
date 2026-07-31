using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core.IO;

namespace VcfEditor.Core
{
    /// <summary>
    /// Streams request content with progress, cancellation, and a hard byte limit.
    /// The caller owns the source stream lifetime.
    /// </summary>
    internal sealed class ProgressableStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly int _bufferSize;
        private readonly long _totalBytes;
        private readonly long _maxBytes;
        private readonly IProgress<double>? _progress;

        public ProgressableStreamContent(
            Stream source,
            long totalBytes,
            IProgress<double>? progress,
            int bufferSize = TransferLimits.DefaultStreamBufferBytes,
            long maxBytes = TransferLimits.MaxFileTransferBytes)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
            if (totalBytes > maxBytes)
                throw new TransferLimitExceededException(maxBytes, totalBytes);

            _source = source;
            _totalBytes = totalBytes;
            _progress = progress;
            _bufferSize = bufferSize;
            _maxBytes = maxBytes;
        }

        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context)
            => SerializeCoreAsync(target, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream target,
            TransportContext? context,
            CancellationToken cancellationToken)
            => SerializeCoreAsync(target, cancellationToken);

        private async Task SerializeCoreAsync(Stream target, CancellationToken cancellationToken)
        {
            var byteProgress = _progress is null
                ? null
                : new Progress<long>(sent =>
                {
                    if (_totalBytes > 0)
                        _progress.Report(Math.Min(1.0, (double)sent / _totalBytes));
                });

            await BoundedStreamCopy.CopyAsync(
                    _source,
                    target,
                    _maxBytes,
                    progress: byteProgress,
                    bufferSize: _bufferSize,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _progress?.Report(1.0);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            // The caller owns _source and controls when it is disposed.
            base.Dispose(disposing);
        }
    }
}
