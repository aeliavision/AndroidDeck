using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VcfEditor.Core;
using VcfEditor.Core.IO;
using VcfEditor.Helpers;
using VcfEditor.Models;
using VcfEditor.Models.DTOs;
using VcfEditor.ViewModels;

namespace VcfEditor.Performance.Tests;

public sealed class LargeDataPerformanceTests
{
    [Fact]
    public void VcfParserParsesAndExportsTenThousandContactsWithinRegressionBudget()
    {
        const int contactCount = 10_000;
        var source = BuildVcf(contactCount);
        var parser = new VcfParser();

        var parseTimer = Stopwatch.StartNew();
        var contacts = parser.ParseVcf(new StringReader(source)).ToList();
        parseTimer.Stop();

        var exportTimer = Stopwatch.StartNew();
        var exported = parser.ExportToVcf(contacts);
        exportTimer.Stop();

        contacts.Should().HaveCount(contactCount);
        exported.Should().Contain("FN:Contact 9999");
        parseTimer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        exportTimer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void BulkContactReplacementUsesOneResetForTenThousandItems()
    {
        var collection = new BulkObservableCollection<Contact>();
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => events.Add(args);
        var contacts = Enumerable.Range(0, 10_000)
            .Select(index => new Contact { FullName = $"Contact {index}" })
            .ToArray();

        var timer = Stopwatch.StartNew();
        collection.ReplaceAll(contacts);
        timer.Stop();

        collection.Should().HaveCount(10_000);
        events.Should().ContainSingle(eventArgs =>
            eventArgs.Action == NotifyCollectionChangedAction.Reset);
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BulkGalleryReplacementHandlesFiveThousandItemsWithinRegressionBudget()
    {
        var collection = new BulkObservableCollection<GalleryMediaItem>();
        var media = Enumerable.Range(0, 5_000)
            .Select(index => new GalleryMediaItem(new GalleryMediaDto
            {
                Id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = $"image-{index}.jpg",
                MediaType = "image",
                Size = 1024
            }))
            .ToArray();

        var timer = Stopwatch.StartNew();
        collection.ReplaceAll(media);
        timer.Stop();

        collection.Should().HaveCount(5_000);
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BoundedStreamCopySimulatesOneGigabyteWithoutPayloadAllocation()
    {
        const long oneGigabyte = 1024L * 1024 * 1024;
        await using var source = new SyntheticLengthStream(oneGigabyte);
        await using var destination = new CountingWriteStream();

        var timer = Stopwatch.StartNew();
        var copied = await BoundedStreamCopy.CopyAsync(
            source,
            destination,
            oneGigabyte,
            cancellationToken: CancellationToken.None,
            bufferSize: 8 * 1024 * 1024);
        timer.Stop();

        copied.Should().Be(oneGigabyte);
        destination.BytesWritten.Should().Be(oneGigabyte);
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void BackupManifestProcessesLargePathCatalogWithinRegressionBudget()
    {
        var manifest = new BackupManifestResponse(
            Version: 3,
            SupportedTypes: Enumerable.Range(0, 10_000)
                .Select(index => $"type-{index}")
                .ToList(),
            DefaultPaths: Enumerable.Range(0, 10_000)
                .Select(index => $"/storage/emulated/0/folder-{index}")
                .ToList(),
            SupportsIncremental: true);

        var timer = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(manifest);
        var roundTrip = JsonSerializer.Deserialize<BackupManifestResponse>(json);
        timer.Stop();

        roundTrip.Should().NotBeNull();
        roundTrip!.SupportedTypes.Should().HaveCount(10_000);
        roundTrip.DefaultPaths.Should().HaveCount(10_000);
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private static string BuildVcf(int count)
    {
        var builder = new StringBuilder(count * 128);
        for (var index = 0; index < count; index++)
        {
            builder.AppendLine("BEGIN:VCARD");
            builder.AppendLine("VERSION:3.0");
            builder.Append("N:User;").Append(index).AppendLine(";;;");
            builder.Append("FN:Contact ").Append(index).AppendLine();
            builder.Append("TEL;CELL:+1555010").Append(index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
            builder.AppendLine("END:VCARD");
        }
        return builder.ToString();
    }

    private sealed class SyntheticLengthStream : Stream
    {
        private long _remaining;

        public SyntheticLengthStream(long length) => _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_remaining == 0) return ValueTask.FromResult(0);
            var read = (int)Math.Min(buffer.Length, _remaining);
            _remaining -= read;
            return ValueTask.FromResult(read);
        }
    }

    private sealed class CountingWriteStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }
}
