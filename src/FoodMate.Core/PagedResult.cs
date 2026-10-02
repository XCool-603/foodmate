namespace FoodMate.Core;

/// <summary>分页结果。</summary>
/// <typeparam name="T">列表项类型。</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>当前页数据。</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>总条数。</summary>
    public int Total { get; init; }

    /// <summary>当前页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;

    /// <summary>每页条数。</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>是否还有下一页。</summary>
    public bool HasMore => (long)Page * PageSize < Total;

    /// <summary>构造分页结果。</summary>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int total, int page, int pageSize)
        => new() { Items = items, Total = total, Page = page, PageSize = pageSize };
}
