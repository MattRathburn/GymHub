
using GH.Program.API.Models;
using GH.Program.API.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;

namespace GH.Program.API.Endpoints;

public static class ProgramAPI
{
    public static IEndpointRouteBuilder MapProgramAPIv1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/program").HasApiVersion(1.0);
        // GET
        api.MapGet("/", GetAllPrograms);
        api.MapGet("/{id:int}", GetProgramById);
        api.MapGet("/{name:minlength(2)}", GetProgramsByName);
        api.MapGet("/{creator:minlength(2)}", GetProgramsByCreator);

        api.MapPut("/", UpdateProgram);
        api.MapPost("/", CreateProgram);
        api.MapDelete("/{id:int}", DeleteProgram);

        return app;
    }

    public static async Task<Results<NoContent, NotFound>> DeleteProgram(
        [AsParameters] GHProgramServices services,
        int id)
    {
        var program = services.Context.GHPrograms.SingleOrDefault(x => x.Id == id);

        if (program is null)
        {
            return TypedResults.NotFound();
        }

        services.Context.GHPrograms.Remove(program);
        await services.Context.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    public static async Task<Created> CreateProgram(
        [AsParameters] GHProgramServices services,
        GHProgram program)
    {
        var newProgram = new GHProgram()
        {
            Id = program.Id,
            Name = program.Name,
            Description = program.Description,
            Creator = program.Creator,
            Components = program.Components
        };

        services.Context.GHPrograms.Add(newProgram);
        await services.Context.SaveChangesAsync();

        return TypedResults.Created($"/api/program/{newProgram.Id}");
    }

    public static async Task<Results<Created, NotFound<string>>> UpdateProgram(
        [AsParameters] GHProgramServices services,
        GHProgram programToUpdate)
    {
        var program = await services.Context.GHPrograms.SingleOrDefaultAsync(p => p.Id == programToUpdate.Id);

        if (program == null)
        {
            return TypedResults.NotFound($"Program with id {programToUpdate.Id} not found");
        }

        var entry = services.Context.Entry(program);
        entry.CurrentValues.SetValues(programToUpdate);

        await services.Context.SaveChangesAsync();

        return TypedResults.Created($"/api/program/{programToUpdate.Id}");
    }

    public static async Task<Ok<PaginatedPrograms<GHProgram>>> GetProgramsByCreator(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] GHProgramServices services,
        string creator)
    {
        var pageSize = paginationRequest.PageSize;
        var pageIndex = paginationRequest.PageIndex;

        var totalItems = await services.Context.GHPrograms
            .Where(p => p.Creator.Name.StartsWith(creator))
            .LongCountAsync();

        var itemsOnPage = await services.Context.GHPrograms
            .Where(p => p.Creator.Name.StartsWith(creator))
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return TypedResults.Ok(new PaginatedPrograms<GHProgram>(pageIndex, pageSize, totalItems, itemsOnPage));
    }

    public static async Task<Ok<PaginatedPrograms<GHProgram>>> GetProgramsByName(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] GHProgramServices services,
        string name)
    {
        var pageSize = paginationRequest.PageSize;
        var pageIndex = paginationRequest.PageIndex;

        var totalItems = await services.Context.GHPrograms
            .Where(p => p.Name.StartsWith(name))
            .LongCountAsync();

        var itemsOnPage = await services.Context.GHPrograms
            .Where(p => p.Name.StartsWith(name))
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return TypedResults.Ok(new PaginatedPrograms<GHProgram>(pageIndex, pageSize, totalItems, itemsOnPage));
    }

    public static async Task<Results<Ok<GHProgram>, NotFound, BadRequest<string>>> GetProgramById(
        [AsParameters] GHProgramServices services,
        int id)
    {
        if (id <= 0)
            return TypedResults.BadRequest("Invalid ID");

        var item = await services.Context.GHPrograms.SingleOrDefaultAsync(p => p.Id == id);

        if (item == null)
            return TypedResults.NotFound();

        return TypedResults.Ok(item);
    }

    public static async Task<Results<Ok<PaginatedPrograms<GHProgram>>, BadRequest<string>>> GetAllPrograms(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] GHProgramServices services)
    {
        var pageSize = paginationRequest.PageSize;
        var pageIndex = paginationRequest.PageIndex;

        var totalItems = await services.Context.GHPrograms.LongCountAsync();

        var currentItems = await services.Context.GHPrograms
            .OrderBy(c => c.Name)
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return TypedResults.Ok(new PaginatedPrograms<GHProgram>(pageSize, pageIndex, totalItems, currentItems));
    }

}

