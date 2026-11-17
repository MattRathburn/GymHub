using GymHub.BuildingBlocks.Paging;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Domain;
using GymHub.Modules.Programs.Features.Shared;
using GymHub.Modules.Trainers.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GymHub.Modules.Programs.Features.BrowsePrograms;

public enum ProgramSort
{
    Newest,
    TopRated,
    PriceLow,
    PriceHigh
}

/// <summary>
/// Search matches the title, summary or the trainer's name. Pricing filters by pricing type; Free (true/false) is the
/// older free-or-paid filter and still works.
/// </summary>
public sealed record BrowseProgramsQuery(
    string? Search, string? TrainerId, ProgramKind? Kind, bool? Free, PricingModel? Pricing, ProgramSort? Sort);

public sealed record ProgramCard(Guid Id, string Title, string? Summary, string TrainerId, ProgramKind Kind, DateOnly? StartsOn, DateOnly? EndsOn, ProgramPrice Price, Uri? CoverUrl, int WeekCount, RatingDto Rating);

internal sealed class BrowseProgramsHandler(ProgramsDbContext db, ProgramContentMapper mapper, ITrainerDirectory trainers)
{
    /// <summary>How many reviews' worth of <see cref="PriorStars"/> a program's rating starts from (the Bayesian prior).</summary>
    public const double PriorWeight = 5;

    /// <summary>
    /// The neutral rating new programs are pulled toward. The site-wide average would be a poor prior while most
    /// programs score high: a single 5-star review would then beat fifty 4.8s.
    /// </summary>
    public const double PriorStars = 3;

    public async Task<PagedResult<ProgramCard>> Handle(BrowseProgramsQuery query, PaginationRequest page, CancellationToken cancellationToken)
    {
        var programs = db.Programs.AsNoTracking().Where(p => p.Status == ProgramStatus.Published);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = LikePattern.Contains(query.Search);
            var trainerIds = await trainers.FindTrainerIdsAsync(query.Search, cancellationToken);

            programs = programs.Where(p =>
                EF.Functions.ILike(p.Title, pattern, LikePattern.EscapeCharacter)
                || (p.Summary != null && EF.Functions.ILike(p.Summary, pattern, LikePattern.EscapeCharacter))
                || trainerIds.Contains(p.TrainerId));
        }

        if (!string.IsNullOrWhiteSpace(query.TrainerId))
        {
            programs = programs.Where(p => p.TrainerId == query.TrainerId);
        }

        if (query.Kind is { } kind)
        {
            programs = programs.Where(p => p.Kind == kind);
        }

        if (query.Free is { } free)
        {
            programs = programs.Where(p => p.IsFree == free);
        }

        if (query.Pricing is { } pricing)
        {
            programs = programs.Where(p => p.PriceModel == pricing);
        }

        var count = await programs.LongCountAsync(cancellationToken);
        var ordered = Order(programs, query.Sort ?? ProgramSort.Newest);

        var items = await ordered
            .Include(p => p.Prices)
            .Include(p => p.Weeks)
            .Skip(page.Skip)
            .Take(page.Take)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var cards = items.Select(p => new ProgramCard(
            p.Id, p.Title, p.Summary, p.TrainerId, p.Kind, p.StartsOn, p.EndsOn,
            p.CurrentPrice.Price, mapper.CoverUrl(p), p.Weeks.Count, RatingDto.From(p))).ToList();

        return new PagedResult<ProgramCard>(page.PageIndex, page.Take, count, cards);
    }

    private static IQueryable<TrainingProgram> Order(IQueryable<TrainingProgram> programs, ProgramSort sort)
    {
        switch (sort)
        {
            case ProgramSort.TopRated:
                // Weighted (Bayesian) average: (C·m + sum) / (C + count). Every program starts as if it had C reviews
                // of m stars, so a handful of 5-star reviews can't outrank many 4.8s. Unrated programs come last.
                const double prior = PriorWeight * PriorStars;

                return programs
                    .OrderBy(p => p.RatingCount == 0)
                    .ThenByDescending(p => (prior + p.RatingSum) / (PriorWeight + p.RatingCount))
                    .ThenByDescending(p => p.RatingCount)
                    .ThenByDescending(p => p.PublishedAt);

            // Free programs first (low) or last (high). Paid ones are grouped by currency, since amounts in different
            // currencies can't be compared, and yearly subscriptions are compared by their monthly cost.
            case ProgramSort.PriceLow:
                return programs
                    .OrderBy(p => p.PriceAmountMinor != null)
                    .ThenBy(p => p.PriceCurrency)
                    .ThenBy(p => p.PriceInterval == BillingInterval.Yearly ? p.PriceAmountMinor / 12 : p.PriceAmountMinor)
                    .ThenByDescending(p => p.PublishedAt);

            case ProgramSort.PriceHigh:
                return programs
                    .OrderBy(p => p.PriceAmountMinor == null)
                    .ThenBy(p => p.PriceCurrency)
                    .ThenByDescending(p => p.PriceInterval == BillingInterval.Yearly ? p.PriceAmountMinor / 12 : p.PriceAmountMinor)
                    .ThenByDescending(p => p.PublishedAt);

            default:
                return programs.OrderByDescending(p => p.PublishedAt);
        }
    }
}

internal static class BrowseProgramsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/", async ([AsParameters] BrowseProgramsQuery query, [AsParameters] PaginationRequest page, BrowseProgramsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(query, page, ct)))
            .AllowAnonymous()
            .WithName("BrowsePrograms");
}
