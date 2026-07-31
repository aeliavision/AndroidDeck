using System;
using System.Globalization;

namespace VcfEditor.Core.Paging;

internal enum PaginationIncompleteReason
{
    RepeatedPageContent,
    RepeatedPageToken,
    NonForwardNextPage,
    EmptyPageWithContinuation,
    MaximumPageCountExceeded
}

internal sealed class IncompletePagedResultException : Exception
{
    public IncompletePagedResultException(
        PaginationIncompleteReason reason,
        int currentPage,
        int? nextPage,
        int pagesFetched,
        int itemsCollected)
        : base(CreateMessage(reason, currentPage, nextPage, pagesFetched, itemsCollected))
    {
        Reason = reason;
        CurrentPage = currentPage;
        NextPage = nextPage;
        PagesFetched = pagesFetched;
        ItemsCollected = itemsCollected;
    }

    public PaginationIncompleteReason Reason { get; }
    public int CurrentPage { get; }
    public int? NextPage { get; }
    public int PagesFetched { get; }
    public int ItemsCollected { get; }

    private static string CreateMessage(
        PaginationIncompleteReason reason,
        int currentPage,
        int? nextPage,
        int pagesFetched,
        int itemsCollected)
        => $"Paged result is incomplete ({reason}) at page {currentPage}; " +
           $"next page: {(nextPage?.ToString(CultureInfo.InvariantCulture) ?? "none")}; pages fetched: {pagesFetched}; " +
           $"items collected: {itemsCollected}.";
}
