using System.Globalization;
using EventBus.Abstractions;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Discussions.Contracts;
using GymHub.Modules.Notifications.Delivery;
using GymHub.Modules.Notifications.Domain;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Subscriptions.Contracts;
using GymHub.Modules.Trainers.Contracts;

namespace GymHub.Modules.Notifications.Handlers;

// One handler per event the other modules publish. Each works out the recipients, words the notification, and hands
// it to NotificationPublisher, which applies preferences, skips the actor and drops duplicates.

/// <summary>Program titles and people's names, for wording.</summary>
internal sealed class NotificationContext(IProgramCatalog catalog, IProfileDirectory profiles)
{
    public async Task<ProgramSummary?> ProgramAsync(Guid programId) => await catalog.GetSummaryAsync(programId, CancellationToken.None);

    public async Task<string> ProgramTitleAsync(Guid programId) => (await ProgramAsync(programId))?.Title ?? "your program";

    public async Task<string> NameAsync(string userId, string fallback = "Someone")
    {
        var authors = await profiles.GetAuthorsAsync([userId], CancellationToken.None);
        return authors.GetValueOrDefault(userId)?.DisplayName ?? fallback;
    }

    public static string ProgramLink(Guid programId) => $"/programs/{programId}";

    public static string ThreadLink(Guid programId, Guid threadId) => $"/programs/{programId}/discussion/{threadId}";

    public static string Money(long amountMinor, string currency) =>
        $"{(amountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    public static string Day(DateTime? date) => date?.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) ?? "your next renewal";
}

internal sealed class WeekReleasedHandler(NotificationPublisher publisher, NotificationContext context, IProgramMembers members)
    : IIntegrationEventHandler<ProgramWeekReleased>
{
    public async Task Handle(ProgramWeekReleased @event)
    {
        var title = await context.ProgramTitleAsync(@event.ProgramId);
        var memberIds = await members.GetActiveMemberIdsAsync(@event.ProgramId, CancellationToken.None);

        await publisher.PublishAsync(memberIds.Select(userId => new NotificationDraft(
            userId,
            NotificationType.WeekReleased,
            $"Week {@event.WeekNumber} of {title} is out",
            $"\"{@event.WeekTitle}\" is ready for you.",
            NotificationContext.ProgramLink(@event.ProgramId),
            $"week-released:{@event.WeekId}")));
    }
}

internal sealed class TrainerPublishedHandler(NotificationPublisher publisher, NotificationContext context, IFollowers followers)
    : IIntegrationEventHandler<ProgramPublished>
{
    public async Task Handle(ProgramPublished @event)
    {
        var trainer = await context.NameAsync(@event.TrainerId, "A trainer you follow");
        var followerIds = await followers.GetFollowerIdsAsync(@event.TrainerId, CancellationToken.None);

        await publisher.PublishAsync(followerIds.Select(userId => new NotificationDraft(
            userId,
            NotificationType.TrainerPublished,
            $"{trainer} published {@event.Title}",
            "A new program from a trainer you follow.",
            NotificationContext.ProgramLink(@event.ProgramId),
            $"program-published:{@event.ProgramId}")), actorId: @event.TrainerId);
    }
}

internal sealed class ThreadStartedHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<DiscussionThreadStarted>
{
    public async Task Handle(DiscussionThreadStarted @event)
    {
        if (await context.ProgramAsync(@event.ProgramId) is not { } program)
        {
            return;
        }

        var author = await context.NameAsync(@event.AuthorId);

        await publisher.PublishAsync([new NotificationDraft(
            program.TrainerId,
            NotificationType.ThreadStarted,
            $"New thread on {program.Title}",
            $"{author}: \"{@event.Title}\"",
            NotificationContext.ThreadLink(@event.ProgramId, @event.ThreadId),
            $"thread-started:{@event.ThreadId}")], actorId: @event.AuthorId);
    }
}

internal sealed class ReplyPostedHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<DiscussionReplyPosted>
{
    public async Task Handle(DiscussionReplyPosted @event)
    {
        var author = await context.NameAsync(@event.AuthorId);
        var title = await context.ProgramTitleAsync(@event.ProgramId);

        await publisher.PublishAsync([new NotificationDraft(
            @event.ParentAuthorId,
            NotificationType.PostReplied,
            $"{author} replied to your post",
            $"In the {title} discussion.",
            NotificationContext.ThreadLink(@event.ProgramId, @event.ThreadId),
            $"reply:{@event.PostId}")], actorId: @event.AuthorId);
    }
}

internal sealed class AnswerMarkedHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<DiscussionAnswerMarked>
{
    public async Task Handle(DiscussionAnswerMarked @event)
    {
        var title = await context.ProgramTitleAsync(@event.ProgramId);

        await publisher.PublishAsync([new NotificationDraft(
            @event.PostAuthorId,
            NotificationType.AnswerMarked,
            "Your reply was marked as the answer",
            $"In the {title} discussion.",
            NotificationContext.ThreadLink(@event.ProgramId, @event.ThreadId),
            $"answer:{@event.ThreadId}:{@event.PostId}")]);
    }
}

internal sealed class TrainerApprovedHandler(NotificationPublisher publisher) : IIntegrationEventHandler<TrainerApproved>
{
    public Task Handle(TrainerApproved @event) =>
        publisher.PublishAsync([new NotificationDraft(
            @event.UserId,
            NotificationType.TrainerApproved,
            "You're a GymHub trainer",
            "Your application was approved. Sign out and back in, then open the trainer studio to create your first program.",
            "/studio",
            $"trainer-approved:{@event.ApplicationId}")]);
}

internal sealed class ProgramSoldHandler(NotificationPublisher publisher, NotificationContext context) : IIntegrationEventHandler<ProgramSold>
{
    public async Task Handle(ProgramSold @event)
    {
        var buyer = await context.NameAsync(@event.UserId);
        var title = await context.ProgramTitleAsync(@event.ProgramId);

        await publisher.PublishAsync([new NotificationDraft(
            @event.TrainerId,
            NotificationType.NewMember,
            @event.IsSubscription ? $"{buyer} subscribed to {title}" : $"{buyer} bought {title}",
            "See your sales on the Payouts page.",
            "/studio/payouts",
            $"sold:{@event.PurchaseId}")], actorId: @event.UserId);
    }
}

internal sealed class FreeEnrollmentHandler(NotificationPublisher publisher, NotificationContext context) : IIntegrationEventHandler<EnrollmentStarted>
{
    public async Task Handle(EnrollmentStarted @event)
    {
        if (await context.ProgramAsync(@event.ProgramId) is not { } program)
        {
            return;
        }

        var member = await context.NameAsync(@event.UserId);

        await publisher.PublishAsync([new NotificationDraft(
            program.TrainerId,
            NotificationType.NewMember,
            $"{member} joined {program.Title}",
            "A new member of your free program.",
            NotificationContext.ProgramLink(@event.ProgramId),
            $"enrolled:{@event.Id}")], actorId: @event.UserId);
    }
}

internal sealed class PriceChangeHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<SubscriptionPriceChangeScheduled>
{
    public async Task Handle(SubscriptionPriceChangeScheduled @event)
    {
        var title = await context.ProgramTitleAsync(@event.ProgramId);
        var per = @event.Interval == "Yearly" ? "year" : "month";

        await publisher.PublishAsync([new NotificationDraft(
            @event.UserId,
            NotificationType.PriceChangeScheduled,
            $"The price of {title} is changing",
            $"From {NotificationContext.Day(@event.EffectiveAt)} your subscription renews at "
                + $"{NotificationContext.Money(@event.AmountMinor, @event.Currency)} per {per}. You can cancel any time before then in Manage billing.",
            "/me/programs",
            $"price-change:{@event.PurchaseId}:{@event.PriceVersion}")]);
    }
}

internal sealed class TrialEndingHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<SubscriptionTrialEnding>
{
    public async Task Handle(SubscriptionTrialEnding @event)
    {
        var title = await context.ProgramTitleAsync(@event.ProgramId);

        await publisher.PublishAsync([new NotificationDraft(
            @event.UserId,
            NotificationType.TrialEnding,
            $"Your free trial of {title} ends soon",
            $"Your trial ends on {NotificationContext.Day(@event.TrialEndsAt)}, and then your subscription starts. Cancel before then in Manage billing if you don't want to continue.",
            "/me/programs",
            $"trial-ending:{@event.PurchaseId}")]);
    }
}

internal sealed class PaymentFailedHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<SubscriptionPaymentFailed>
{
    public async Task Handle(SubscriptionPaymentFailed @event)
    {
        var title = await context.ProgramTitleAsync(@event.ProgramId);

        await publisher.PublishAsync([new NotificationDraft(
            @event.UserId,
            NotificationType.PaymentFailed,
            $"Your payment for {title} failed",
            "We'll try again over the next few days. Update your card in Manage billing to keep your access.",
            "/me/programs",
            $"payment-failed:{@event.Id}")]);
    }
}

internal sealed class AccessEndedHandler(NotificationPublisher publisher, NotificationContext context)
    : IIntegrationEventHandler<PaidAccessEnded>
{
    public async Task Handle(PaidAccessEnded @event)
    {
        var title = await context.ProgramTitleAsync(@event.ProgramId);

        var body = @event.Reason switch
        {
            PaidAccessEndReason.Refunded => "Your purchase was refunded, so you no longer have access.",
            PaidAccessEndReason.Disputed => "The payment was disputed with your bank, so access has been paused.",
            PaidAccessEndReason.Unpaid => "We couldn't collect the payment, so your subscription ended.",
            _ => "Your subscription ended. You can subscribe again any time."
        };

        await publisher.PublishAsync([new NotificationDraft(
            @event.UserId,
            NotificationType.AccessEnded,
            $"Your access to {title} ended",
            body,
            NotificationContext.ProgramLink(@event.ProgramId),
            $"access-ended:{@event.PurchaseId}")]);
    }
}
