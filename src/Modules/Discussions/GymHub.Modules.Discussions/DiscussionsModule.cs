using FluentValidation;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Discussions.Data;
using GymHub.Modules.Discussions.Features.GetThread;
using GymHub.Modules.Discussions.Features.ListThreads;
using GymHub.Modules.Discussions.Features.ManageMyPost;
using GymHub.Modules.Discussions.Features.MarkAnswer;
using GymHub.Modules.Discussions.Features.ModeratePost;
using GymHub.Modules.Discussions.Features.ModerateThread;
using GymHub.Modules.Discussions.Features.Reply;
using GymHub.Modules.Discussions.Features.ReportPost;
using GymHub.Modules.Discussions.Features.ReviewReports;
using GymHub.Modules.Discussions.Features.Shared;
using GymHub.Modules.Discussions.Features.StartThread;
using GymHub.Modules.Discussions.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Discussions;

/// <summary>Each program's subscriber-only discussion board: threads, nested replies, moderation, reports and live updates.</summary>
public sealed class DiscussionsModule : IModule
{
    /// <summary>Hub path below the module's route group (/api/v1/discussions).</summary>
    public const string HubPath = "/hub";

    public string Name => "discussions";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<DiscussionsDbContext>(DiscussionsDbContext.SchemaName);

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<DiscussionsModule>(includeInternalTypes: true);
        services.AddSignalR();

        services.AddScoped<BoardAccess>();
        services.AddScoped<AuthorLookup>();
        services.AddSingleton<IBoardNotifier, HubBoardNotifier>();

        services.AddScoped<ListThreadsHandler>();
        services.AddScoped<StartThreadHandler>();
        services.AddScoped<GetThreadHandler>();
        services.AddScoped<ReplyHandler>();
        services.AddScoped<ManageMyPostHandler>();
        services.AddScoped<ModerateThreadHandler>();
        services.AddScoped<ModeratePostHandler>();
        services.AddScoped<MarkAnswerHandler>();
        services.AddScoped<ReportPostHandler>();
        services.AddScoped<ReviewReportsHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ListThreadsEndpoint.Map(endpoints);
        StartThreadEndpoint.Map(endpoints);
        GetThreadEndpoint.Map(endpoints);
        ReplyEndpoint.Map(endpoints);
        ManageMyPostEndpoint.Map(endpoints);
        ModerateThreadEndpoint.Map(endpoints);
        ModeratePostEndpoint.Map(endpoints);
        MarkAnswerEndpoint.Map(endpoints);
        ReportPostEndpoint.Map(endpoints);
        ReviewReportsEndpoint.Map(endpoints);

        endpoints.MapHub<DiscussionsHub>(HubPath);
    }
}
