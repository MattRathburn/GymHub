using GymHub.BuildingBlocks.Auth;
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

namespace GymHub.Modules.Programs.Features.AdminPrograms;

public sealed record AdminProgramsQuery(string? Search, ProgramStatus? Status);

public sealed record AdminProgramDto(
    Guid Id,
    string Title,
    string TrainerId,
    string? TrainerName,
    ProgramStatus Status,
    ProgramKind Kind,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    PricingModel PriceModel,
    long? PriceAmountMinor,
    string? PriceCurrency,
    BillingInterval? PriceInterval,
    RatingDto Rating);

/// <summary>Admin: every program in any status, newest first, searchable by title or trainer name.</summary>
internal sealed class AdminProgramsHandler(ProgramsDbContext db, ITrainerDirectory trainers, IProfileDirectory profiles)
{
    public async Task<PagedResult<AdminProgramDto>> Handle(AdminProgramsQuery query, PaginationRequest page, CancellationToken cancellationToken)
    {
        var programs = db.Programs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = LikePattern.Contains(query.Search);
            var trainerIds = await trainers.FindTrainerIdsAsync(query.Search, cancellationToken);

            programs = programs.Where(p => EF.Functions.ILike(p.Title, pattern, LikePattern.EscapeCharacter) || trainerIds.Contains(p.TrainerId));
        }

        if (query.Status is { } status)
        {
            programs = programs.Where(p => p.Status == status);
        }

        var count = await programs.LongCountAsync(cancellationToken);
        var items = await programs.OrderByDescending(p => p.CreatedAt).Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);
        var names = await profiles.GetAuthorsAsync([.. items.Select(p => p.TrainerId).Distinct()], cancellationToken);

        return new PagedResult<AdminProgramDto>(page.PageIndex, page.Take, count, [.. items.Select(p => new AdminProgramDto(
            p.Id, p.Title, p.TrainerId, names.GetValueOrDefault(p.TrainerId)?.DisplayName, p.Status, p.Kind, p.CreatedAt, p.PublishedAt,
            p.PriceModel, p.PriceAmountMinor, p.PriceCurrency, p.PriceInterval, RatingDto.From(p)))]);
    }
}

internal static class AdminProgramsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/admin", async ([AsParameters] AdminProgramsQuery query, [AsParameters] PaginationRequest page, AdminProgramsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.Handle(query, page, ct)))
            .RequireAuthorization(Policies.Admin)
            .WithName("AdminListPrograms");
}
