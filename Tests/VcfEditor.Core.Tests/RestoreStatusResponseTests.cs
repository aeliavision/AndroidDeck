using System.Text.Json;
using FluentAssertions;
using VcfEditor.Core;

namespace VcfEditor.Core.Tests;

public sealed class RestoreStatusResponseTests
{
    [Fact]
    public void ItemResultsDeserializeAndExposeTheMostUsefulDetail()
    {
        const string json = """
            {
              "restoreId": "restore-1",
              "progress": 1.0,
              "phase": "complete",
              "restoredItems": 1,
              "failedItems": 1,
              "skippedItems": 1,
              "error": null,
              "itemResults": [
                { "path": "DCIM/a.jpg", "status": "restored", "conflict": null, "error": null },
                { "path": "DCIM/b.jpg", "status": "skipped", "conflict": "Existing file kept", "error": null },
                { "path": "DCIM/c.jpg", "status": "failed", "conflict": null, "error": "Permission denied" }
              ]
            }
            """;

        var result = JsonSerializer.Deserialize<RestoreStatusResponse>(json);

        result.Should().NotBeNull();
        result!.ItemResults.Should().HaveCount(3);
        result.ItemResults![0].Detail.Should().Be("restored");
        result.ItemResults[1].Detail.Should().Be("Existing file kept");
        result.ItemResults[2].Detail.Should().Be("Permission denied");
    }
}
