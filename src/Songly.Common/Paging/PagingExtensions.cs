using Microsoft.EntityFrameworkCore;

namespace Songly.Common.Paging;

public static class PagingExtensions
{
    /// <summary>
    /// Reads one page and the total count of an <b>ordered</b> query.
    /// </summary>
    /// <remarks>
    /// The query must already be ordered by something unique (an id as the last key): without it,
    /// Postgres may return the rows of one page in a different order on the next request, and a
    /// row can appear on two pages or on none. Filters belong in the query, not in code after the
    /// page is read — filtering afterwards would return short pages and count hidden rows in
    /// <c>totalElements</c>.
    /// </remarks>
    public static async Task<PagedResponse<TResult>> ToPageAsync<TSource, TResult>(
        this IQueryable<TSource> ordered,
        PageQuery page,
        Func<TSource, TResult> map,
        CancellationToken cancellationToken)
    {
        var total = await ordered.LongCountAsync(cancellationToken);
        var rows = page.Offset >= total
            ? []
            : await ordered.Skip((int)page.Offset).Take(page.Size).ToListAsync(cancellationToken);
        return Page(rows.Select(map).ToList(), page, total);
    }

    /// <summary>An empty page that still says which page was asked for.</summary>
    public static PagedResponse<T> Empty<T>(PageQuery page) => Page<T>([], page, total: 0);

    /// <summary>
    /// A page of a list ranked in memory, where no query can order it (matching ranks by a score
    /// computed in code): the caller has cut <paramref name="content"/> out of <paramref name="total"/>
    /// rows itself.
    /// </summary>
    public static PagedResponse<T> Page<T>(IReadOnlyList<T> content, PageQuery page, long total) =>
        new(content, new PageMetadata(page.Size, page.Page, total, (int)Math.Ceiling(total / (double)page.Size)));
}
