using FluentValidation;
using GymHub.BuildingBlocks.Modules;
using GymHub.BuildingBlocks.Outbox;
using GymHub.BuildingBlocks.Persistence;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Discussions.Contracts;
using GymHub.Modules.Notifications.Data;
using GymHub.Modules.Notifications.Delivery;
using GymHub.Modules.Notifications.Features.Inbox;
using GymHub.Modules.Notifications.Features.Preferences;
using GymHub.Modules.Notifications.Handlers;
using GymHub.Modules.Notifications.Realtime;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Trainers.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GymHub.Modules.Notifications;

/// <summary>
/// Turns other modules' events into notifications: an in-app inbox with live unread counts, and queued emails,
/// each filtered by the user's per-type preferences.
/// </summary>
public sealed class NotificationsModule : IModule
{
    /// <summary>Hub path below the module's route group (/api/v1/notifications).</summary>
    public const string HubPath = "/hub";

    public string Name => "notifications";

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName);

        var services = builder.Services;
        services.Configure<NotificationsOptions>(builder.Configuration.GetSection(NotificationsOptions.SectionName));
        services.AddValidatorsFromAssemblyContaining<NotificationsModule>(includeInternalTypes: true);
        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, SubUserIdProvider>();

        services.AddSingleton<IUnreadNotifier, HubUnreadNotifier>();
        services.AddScoped<NotificationPublisher>();
        services.AddScoped<NotificationContext>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<EmailDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailDispatcher>());
        services.AddHostedService<NotificationCleanupJob>();

        services.AddIntegrationEventHandler<ProgramWeekReleased, WeekReleasedHandler>();
        services.AddIntegrationEventHandler<ProgramPublished, TrainerPublishedHandler>();
        services.AddIntegrationEventHandler<DiscussionThreadStarted, ThreadStartedHandler>();
        services.AddIntegrationEventHandler<DiscussionReplyPosted, ReplyPostedHandler>();
        services.AddIntegrationEventHandler<DiscussionAnswerMarked, AnswerMarkedHandler>();
        services.AddIntegrationEventHandler<TrainerApproved, TrainerApprovedHandler>();
        services.AddIntegrationEventHandler<ProgramSold, ProgramSoldHandler>();
        services.AddIntegrationEventHandler<EnrollmentStarted, FreeEnrollmentHandler>();
        services.AddIntegrationEventHandler<SubscriptionPriceChangeScheduled, PriceChangeHandler>();
        services.AddIntegrationEventHandler<SubscriptionTrialEnding, TrialEndingHandler>();
        services.AddIntegrationEventHandler<SubscriptionPaymentFailed, PaymentFailedHandler>();
        services.AddIntegrationEventHandler<PaidAccessEnded, AccessEndedHandler>();

        services.AddScoped<InboxHandler>();
        services.AddScoped<PreferencesHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PreferencesEndpoint.Map(endpoints);
        InboxEndpoint.Map(endpoints);

        endpoints.MapHub<NotificationsHub>(HubPath);
    }
}
