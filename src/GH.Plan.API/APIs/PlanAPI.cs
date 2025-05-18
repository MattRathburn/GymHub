
using GH.Plan.Infrastructure.Models;
using GH.Plan.API.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;

namespace GH.Plan.API.Endpoints;

public static class PlanAPI
{
    public static IEndpointRouteBuilder MapPlanAPIv1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/plan").HasApiVersion(1.0);
        // GET
        api.MapGet("/", GetAllPlans);
        api.MapGet("/{id:int}", GetPlanById);
        api.MapGet("/{name:minlength(2)}", GetPlansByName);
        api.MapGet("/creator/{creator:minlength(2)}", GetPlansByCreator);

        api.MapPut("/", UpdatePlan);
        api.MapPost("/", CreatePlan);
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
            .Where(p => p.Creator.Name.StartsWith(creator))
            .LongCountAsync();

        var itemsOnPage = await services.Context.GHPlans
            .Where(p => p.Creator.Name.StartsWith(creator))
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
            .Where(p => p.Name.StartsWith(name))
            .LongCountAsync();

        var itemsOnPage = await services.Context.GHPlans
            .Where(p => p.Name.StartsWith(name))
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

}

