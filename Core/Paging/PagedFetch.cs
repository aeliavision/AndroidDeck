using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Core.Paging;

internal static class PagedFetch
{
    public const int DefaultMaximumPageCount = 10_000;

    public static async Task<List<TItem>> FetchAllAsync<TPage, TItem>(
        Func<int, CancellationToken, Task<TPage>> fetchPageAsync,
        Func<TPage, IReadOnlyList<TItem>> itemsSelector,
        Func<TPage, int?> nextPageSelector,
        Func<TItem, string?> identitySelector,
        int initialPage = 1,
        int maxPages = DefaultMaximumPageCount,
        Action<int>? reportItemCount = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPageAsync);
        ArgumentNullException.ThrowIfNull(itemsSelector);
        ArgumentNullException.ThrowIfNull(nextPageSelector);
        ArgumentNullException.ThrowIfNull(identitySelector);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPages, 1);

        var allItems = new List<TItem>();
        var seenPageNumbers = new HashSet<int>();
        var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var currentPage = initialPage;

        for (var pagesFetched = 1; pagesFetched <= maxPages; pagesFetched++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!seenPageNumbers.Add(currentPage))
            {
                throw new IncompletePagedResultException(
                    PaginationIncompleteReason.RepeatedPageToken,
                    currentPage,
                    currentPage,
                    pagesFetched - 1,
                    allItems.Count);
            }

            var page = await fetchPageAsync(currentPage, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<TItem> items = itemsSelector(page) ?? Array.Empty<TItem>();
            var nextPage = nextPageSelector(page);

            if (items.Count == 0)
            {
                if (nextPage.HasValue)
                {
                    throw new IncompletePagedResultException(
                        PaginationIncompleteReason.EmptyPageWithContinuation,
                        currentPage,
                        nextPage,
                        pagesFetched,
                        allItems.Count);
                }

                return allItems;
            }

            var fingerprint = CreateStableFingerprint(items, identitySelector);
            if (!seenFingerprints.Add(fingerprint))
            {
                throw new IncompletePagedResultException(
                    PaginationIncompleteReason.RepeatedPageContent,
                    currentPage,
                    nextPage,
                    pagesFetched,
                    allItems.Count);
            }

            allItems.AddRange(items);
            reportItemCount?.Invoke(allItems.Count);

            if (!nextPage.HasValue)
                return allItems;

            if (nextPage.Value <= currentPage)
            {
                throw new IncompletePagedResultException(
                    PaginationIncompleteReason.NonForwardNextPage,
                    currentPage,
                    nextPage,
                    pagesFetched,
                    allItems.Count);
            }

            if (seenPageNumbers.Contains(nextPage.Value))
            {
                throw new IncompletePagedResultException(
                    PaginationIncompleteReason.RepeatedPageToken,
                    currentPage,
                    nextPage,
                    pagesFetched,
                    allItems.Count);
            }

            if (pagesFetched == maxPages)
            {
                throw new IncompletePagedResultException(
                    PaginationIncompleteReason.MaximumPageCountExceeded,
                    currentPage,
                    nextPage,
                    pagesFetched,
                    allItems.Count);
            }

            currentPage = nextPage.Value;
        }

        throw new InvalidOperationException("The pagination guard exited unexpectedly.");
    }

    private static string CreateStableFingerprint<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, string?> identitySelector)
    {
        var identities = items
            .Select((item, index) => identitySelector(item)?.Trim() is { Length: > 0 } identity
                ? identity
                : $"<missing:{index}:{(item is null ? 0 : item.GetHashCode())}>")
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToArray();

        var payload = $"{items.Count}\n{string.Join("\n", identities)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
