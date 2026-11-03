namespace Songly.Common.Paging;

/// <summary>
/// One page of a list, in the shape the original's Spring Data serialized it (<c>PagedModel</c>):
/// the rows and, separately, where this page is.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Content, PageMetadata Page);

/// <param name="Size">The size that was applied, after <see cref="PageQuery"/>'s limits.</param>
/// <param name="Number">Zero-based.</param>
public sealed record PageMetadata(int Size, int Number, long TotalElements, int TotalPages);
