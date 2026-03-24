
using GH.Plan.Infrastructure.Models;
using GH.Plan.API.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Xml.Linq;

namespace GH.Plan.API.Endpoints;

public static class PlanAPI
{
    public static IEndpointRouteBuilder MapPlanAPIv1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/plan").HasApiVersion(1.0);

        var apiVersionSet = app.NewApiVersionSet()
            .HasApiVersion(1.0)
            .ReportApiVersions()
            .Build();

        api.MapGet("/", GetAllPlans);
        api.MapGet("/{id:int}", GetPlanById);
        api.MapGet("/popular", GetPopularPlans);
        api.MapGet("/subscribed", GetSubscribedPlans).RequireAuthorization();
        api.MapGet("/{name:minlength(2)}", GetPlansByName);
        api.MapGet("/creator/{creator:minlength(2)}", GetPlansByCreator);

        api.MapPut("/", UpdatePlan);
        api.MapPost("/", CreatePlan);
        api.MapPost("/{id:int}/subscribe", SubscribeToPlan).RequireAuthorization();
        api.MapDelete("/{id:int}/unsubscribe", UnsubscribeFromPlan).RequireAuthorization();
        api.MapDelete("/{id:int}", DeletePlan);

        return app;
    }

    public static async Task<Results<NoContent, NotFound>> DeletePlan(
        [AsParameters] GHPlanServices services,
        int id)
    {
        var plan = services.Context.GHPlans.SingleOrDefault(x => x.Id == id);

        if (plan is null)
        {
            return TypedResults.NotFound();
        }

        services.Context.GHPlans.Remove(plan);
        await services.Context.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    public static async Task<Created> CreatePlan(
        [AsParameters] GHPlanServices services,
        GHPlan plan)
    {
        var newPlan = new GHPlan()
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            Creator = plan.Creator,
            Components = plan.Components
        };

        services.Context.GHPlans.Add(newPlan);
        await services.Context.SaveChangesAsync();

        return TypedResults.Created($"/api/program/{newPlan.Id}");
    }

    public static async Task<Results<Created, NotFound<string>>> UpdatePlan(
        [AsParameters] GHPlanServices services,
        GHPlan planToUpdate)
    {
        var plan = await services.Context.GHPlans.SingleOrDefaultAsync(p => p.Id == planToUpdate.Id);

        if (plan == null)
        {
            return TypedResults.NotFound($"Program with id {planToUpdate.Id} not found");
        }

        var entry = services.Context.Entry(plan);
        entry.CurrentValues.SetValues(planToUpdate);

        await services.Context.SaveChangesAsync();

        return TypedResults.Created($"/api/plan/{planToUpdate.Id}");
    }

    public static async Task<Ok<PaginatedPlan<GHPlan>>> GetPlansByCreator(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] GHPlanServices services,
        string creator)
    {
        var pageSize = paginationRequest.PageSize;
        var pageIndex = paginationRequest.PageIndex;

        var totalItems = await services.Context.GHPlans
            .Where(p => p.Creator != null && p.Creator.Name != null && p.Creator.Name.StartsWith(creator))
            .LongCountAsync();

        var itemsOnPage = await services.Context.GHPlans
            .Where(p => p.Creator != null && p.Creator.Name != null && p.Creator.Name.StartsWith(creator))
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return TypedResults.Ok(new PaginatedPlan<GHPlan>(pageIndex, pageSize, totalItems, itemsOnPage));
    }

    public static async Task<Ok<PaginatedPlan<GHPlan>>> GetPlansByName(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] GHPlanServices services,
        string name)
    {
        var pageSize = paginationRequest.PageSize;
        var pageIndex = paginationRequest.PageIndex;

        var totalItems = await services.Context.GHPlans
            .Where(p => p.Name != null && p.Name.StartsWith(name))
            .LongCountAsync();

        var itemsOnPage = await services.Context.GHPlans
            .Where(p => p.Name != null && p.Name.StartsWith(name))
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return TypedResults.Ok(new PaginatedPlan<GHPlan>(pageIndex, pageSize, totalItems, itemsOnPage));
    }

    public static async Task<Results<Ok<GHPlan>, NotFound, BadRequest<string>>> GetPlanById(
        [AsParameters] GHPlanServices services,
        int id)
    {
        if (id <= 0)
            return TypedResults.BadRequest("Invalid ID");

        var item = await services.Context.GHPlans.SingleOrDefaultAsync(p => p.Id == id);

        if (item == null)
            return TypedResults.NotFound();

        return TypedResults.Ok(item);
    }

    public static async Task<Results<Ok<PaginatedPlan<GHPlan>>, BadRequest<string>>> GetAllPlans(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] GHPlanServices services)
    {
        var pageSize = paginationRequest.PageSize;
        var pageIndex = paginationRequest.PageIndex;

        var totalItems = await services.Context.GHPlans.LongCountAsync();

        var currentItems = await services.Context.GHPlans
            .OrderBy(c => c.Name)
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return TypedResults.Ok(new PaginatedPlan<GHPlan>(pageSize, pageIndex, totalItems, currentItems));
    }

    public static Ok<IEnumerable<GHPlan>> GetPopularPlans(
        [AsParameters] GHPlanServices services,
        int count = 8)
    {
        if (count <= 0)
        {
            count = 8;
        }

        var items = services.Context.GHPlans?
            .Include(p => p.Creator)
            .Include(p => p.Components)
            .OrderByDescending(p => p.Components != null ? p.Components.Count : 0)
            .ThenBy(p => p.Name)
            .Take(count)
            .AsEnumerable();

        return TypedResults.Ok(items);
    }

    private static string GetUserIdFromClaims(ClaimsPrincipal user)
    {
        var userId = user.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            userId = user.FindFirst("preferred_username")?.Value;
        }

        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("Unable to determine user identifier from claims.");
        }

        return userId;
    }

    public static async Task<Ok<IEnumerable<GHPlan>>> GetSubscribedPlans(
        [AsParameters] GHPlanServices services,
        ClaimsPrincipal user)
    {
        var userId = GetUserIdFromClaims(user);

        var planIds = await services.Context.UserPlanSubscriptions
            .Where(s => s.UserId == userId)
            .Select(s => s.PlanId)
            .ToListAsync();

        var plans = await services.Context.GHPlans
            .Where(p => planIds.Contains(p.Id))
            .Include(p => p.Creator)
            .Include(p => p.Components)
            .ToListAsync();

        return TypedResults.Ok<IEnumerable<GHPlan>>(plans);
    }

    public static async Task<Results<Ok, NotFound, BadRequest<string>>> SubscribeToPlan(
        [AsParameters] GHPlanServices services,
        ClaimsPrincipal user,
        int id)
    {
        var userId = GetUserIdFromClaims(user);

        var plan = await services.Context.GHPlans.FindAsync(id);
        if (plan == null)
        {
            return TypedResults.NotFound();
        }

        var exists = await services.Context.UserPlanSubscriptions
            .AnyAsync(x => x.UserId == userId && x.PlanId == id);

        if (exists)
        {
            return TypedResults.BadRequest("Already subscribed");
        }

        services.Context.UserPlanSubscriptions.Add(new UserPlanSubscription
        {
            UserId = userId,
            PlanId = id
        });

        await services.Context.SaveChangesAsync();

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok, NotFound, BadRequest<string>>> UnsubscribeFromPlan(
        [AsParameters] GHPlanServices services,
        ClaimsPrincipal user,
        int id)
    {
        var userId = GetUserIdFromClaims(user);

        var subscription = await services.Context.UserPlanSubscriptions
            .SingleOrDefaultAsync(x => x.UserId == userId && x.PlanId == id);

        if (subscription == null)
        {
            return TypedResults.NotFound();
        }

        services.Context.UserPlanSubscriptions.Remove(subscription);
        await services.Context.SaveChangesAsync();

        return TypedResults.Ok();
    }

}

