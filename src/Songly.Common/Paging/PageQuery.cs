using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Songly.Common.Paging;

/// <summary>
/// <c>?page=&amp;size=</c> of a list endpoint, read the way the original's Spring Data read it, so
/// the mobile app gets the same page for the same request (original Claude.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Nothing is rejected: a missing, malformed or negative <c>page</c> is the first page; a missing,
/// malformed or non-positive <c>size</c> is the default; a <c>size</c> above
/// <see cref="MaxSize"/> is cut down to it. That is Spring's behaviour, and it is the safer one
/// for a list — a client asking for 2000 rows gets a page, not an error and not 2000 rows.
/// (Spring's own default ceiling was 2000; the original lowered it to 100, because with 2000 one
/// request could download 2000 profiles from the user search.)
/// </para>
/// <para>
/// <c>sort</c> is not supported. Spring accepted it on every list and ordered by any property
/// the client named; no client uses it, and every list here has its own fixed order.
/// </para>
/// <para>
/// An endpoint declares a <see cref="PageQuery"/> parameter; Minimal APIs call
/// <see cref="BindAsync"/> to build it.
/// </para>
/// </remarks>
public sealed record PageQuery
{
    public const int DefaultSize = 20;

    public const int MaxSize = 100;

    public PageQuery(int page, int size)
    {
        Page = Math.Max(page, 0);
        Size = size < 1 ? DefaultSize : Math.Min(size, MaxSize);
    }

    /// <summary>Zero-based.</summary>
    public int Page { get; }

    public int Size { get; }

    /// <summary>
    /// How many rows come before this page. A <c>long</c>: a huge page number times the size does
    /// not fit in an <c>int</c>, and a wrapped-around offset would return some other page.
    /// </summary>
    public long Offset => (long)Page * Size;

    public static ValueTask<PageQuery> BindAsync(HttpContext context)
    {
        var query = context.Request.Query;
        return ValueTask.FromResult(new PageQuery(ParseOrZero(query["page"]), ParseOrZero(query["size"])));
    }

    private static int ParseOrZero(string? value) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
