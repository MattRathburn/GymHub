using FluentValidation;
using GymHub.BuildingBlocks.Admin;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Persistence;
using GymHub.BuildingBlocks.Privacy;
using GymHub.Modules.Reviews.Data;
using GymHub.Modules.Reviews.Features.MyReview;
using GymHub.Modules.Reviews.Features.ProgramReviews;
using GymHub.Modules.Reviews.Features.ReplyToReview;
using GymHub.Modules.Reviews.Features.ReportReview;
using GymHub.Modules.Reviews.Features.ReviewReports;
using GymHub.Modules.Reviews.Features.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Reviews;

/// <summary>
/// Members with access rate and review programs; the trainer replies; admins hide reported reviews. Each change
/// publishes the program's rating for the catalog.
/// </summary>
public sealed class ReviewsModule : IModule
{
    public string Name => "reviews";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<ReviewsDbContext>(ReviewsDbContext.SchemaName);
        builder.Services.AddPersonalDataSource<ReviewsPersonalData>();
        builder.Services.AddAdminMetrics<ReviewsMetrics>();

        var services = builder.Services;
        services.AddValidatorsFromAssemblyContaining<ReviewsModule>(includeInternalTypes: true);
        services.AddScoped<ProgramGate>();
        services.AddScoped<RatingWriter>();
        services.AddScoped<ReviewMapper>();

        services.AddScoped<ProgramReviewsHandler>();
        services.AddScoped<MyReviewHandler>();
        services.AddScoped<ReplyToReviewHandler>();
        services.AddScoped<ReportReviewHandler>();
        services.AddScoped<ReviewReportsHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProgramReviewsEndpoint.Map(endpoints);
        MyReviewEndpoint.Map(endpoints);
        ReplyToReviewEndpoint.Map(endpoints);
        ReportReviewEndpoint.Map(endpoints);
        ReviewReportsEndpoint.Map(endpoints);
    }
}
