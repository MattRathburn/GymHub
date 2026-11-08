namespace GymHub.BuildingBlocks.Paging;

public record PaginationRequest(int PageSize = 10, int PageIndex = 0)
{
    public const int MaxPageSize = 50;

    public int Skip => Math.Max(PageIndex, 0) * Take;

    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);
}

public sealed record PagedResult<T>(int PageIndex, int PageSize, long Count, IReadOnlyList<T> Data);
