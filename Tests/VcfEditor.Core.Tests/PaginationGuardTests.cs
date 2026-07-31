using FluentAssertions;
using VcfEditor.Core.Paging;

namespace VcfEditor.Core.Tests;

public sealed class PaginationGuardTests
{
    private sealed record TestItem(string Id);
    private sealed record TestPage(IReadOnlyList<TestItem> Items, int? NextPage);

    [Fact]
    public async Task FetchAllAsyncFollowsServerNextPageValues()
    {
        var requested = new List<int>();
        var pages = new Dictionary<int, TestPage>
        {
            [1] = new([new("a")], 4),
            [4] = new([new("b")], 9),
            [9] = new([new("c")], null)
        };

        var result = await PagedFetch.FetchAllAsync(
            async (page, _) =>
            {
                requested.Add(page);
                await Task.Yield();
                return pages[page];
            },
            page => page.Items,
            page => page.NextPage,
            item => item.Id,
            cancellationToken: CancellationToken.None);

        requested.Should().Equal(1, 4, 9);
        result.Select(item => item.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task FetchAllAsyncRejectsNonForwardNextPage()
    {
        var act = () => PagedFetch.FetchAllAsync(
            (_, _) => Task.FromResult(new TestPage([new("a")], 1)),
            page => page.Items,
            page => page.NextPage,
            item => item.Id,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<IncompletePagedResultException>();
        exception.Which.Reason.Should().Be(PaginationIncompleteReason.NonForwardNextPage);
        exception.Which.CurrentPage.Should().Be(1);
        exception.Which.NextPage.Should().Be(1);
    }

    [Fact]
    public async Task FetchAllAsyncDetectsAbACycleByStableFingerprint()
    {
        var pages = new Dictionary<int, TestPage>
        {
            [1] = new([new("a"), new("b")], 2),
            [2] = new([new("c")], 3),
            [3] = new([new("b"), new("a")], 4)
        };

        var act = () => PagedFetch.FetchAllAsync(
            (page, _) => Task.FromResult(pages[page]),
            page => page.Items,
            page => page.NextPage,
            item => item.Id,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<IncompletePagedResultException>();
        exception.Which.Reason.Should().Be(PaginationIncompleteReason.RepeatedPageContent);
        exception.Which.CurrentPage.Should().Be(3);
        exception.Which.ItemsCollected.Should().Be(3);
    }

    [Fact]
    public async Task FetchAllAsyncRejectsEmptyPageThatClaimsAnotherPage()
    {
        var act = () => PagedFetch.FetchAllAsync(
            (_, _) => Task.FromResult(new TestPage([], 2)),
            page => page.Items,
            page => page.NextPage,
            item => item.Id,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<IncompletePagedResultException>();
        exception.Which.Reason.Should().Be(PaginationIncompleteReason.EmptyPageWithContinuation);
    }

    [Fact]
    public async Task FetchAllAsyncStopsAtConfiguredMaximumPageCount()
    {
        var act = () => PagedFetch.FetchAllAsync(
            (page, _) => Task.FromResult(new TestPage([new($"item-{page}")], page + 1)),
            page => page.Items,
            page => page.NextPage,
            item => item.Id,
            maxPages: 2,
            cancellationToken: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<IncompletePagedResultException>();
        exception.Which.Reason.Should().Be(PaginationIncompleteReason.MaximumPageCountExceeded);
        exception.Which.PagesFetched.Should().Be(2);
    }

    [Fact]
    public async Task FetchAllAsyncObservesCancellationBeforeNextRequest()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        var act = () => PagedFetch.FetchAllAsync(
            (page, _) =>
            {
                calls++;
                cts.Cancel();
                return Task.FromResult(new TestPage([new($"item-{page}")], page + 1));
            },
            page => page.Items,
            page => page.NextPage,
            item => item.Id,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }
}
