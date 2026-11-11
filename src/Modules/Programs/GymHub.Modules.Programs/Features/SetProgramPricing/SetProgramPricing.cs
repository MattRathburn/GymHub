using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Billing.Contracts;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.SetProgramPricing;

public sealed record SetProgramPricingRequest(PricingModel Model, long? AmountMinor, string? Currency, BillingInterval? Interval, int? TrialDays = null);

internal sealed class SetProgramPricingValidator : AbstractValidator<SetProgramPricingRequest>
{
    public SetProgramPricingValidator()
    {
        RuleFor(r => r.Model).IsInEnum();
        RuleFor(r => r.Interval).IsInEnum();
        RuleFor(r => r.Currency).Matches("^[A-Z]{3}$").When(r => r.Currency is not null)
            .WithMessage("Currency must be a three-letter ISO 4217 code, e.g. USD.");
        RuleFor(r => r.AmountMinor).LessThanOrEqualTo(10_000_000);
        RuleFor(r => r.TrialDays).InclusiveBetween(0, ProgramPrice.MaxTrialDays);
    }
}

internal sealed class SetProgramPricingHandler(ProgramsDbContext db, IPayoutReadiness payouts, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid programId, SetProgramPricingRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        // A zero-day trial is no trial, so it compares equal to "none".
        var trialDays = request.TrialDays is > 0 ? request.TrialDays : null;
        var price = new ProgramPrice(request.Model, request.AmountMinor, request.Currency, request.Interval, trialDays);
        var program = loaded.Value!;

        // Drafts can be priced freely; selling starts at publish, which checks again.
        if (program.Status == ProgramStatus.Published && !price.IsFree && !await payouts.IsReadyAsync(program.TrainerId, cancellationToken))
        {
            return Error.Conflict(PayoutsRequired.Message);
        }

        var result = program.SetPricing(price, timeProvider.UtcNow());

        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal static class SetProgramPricingEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/{programId:guid}/pricing", async (Guid programId, SetProgramPricingRequest request, ClaimsPrincipal user, SetProgramPricingHandler handler, CancellationToken ct) =>
                (await handler.Handle(programId, request, CurrentUser.Required(user), ct)).ToHttp())
            .WithValidation<SetProgramPricingRequest>()
            .RequireAuthorization(Policies.Trainer)
            .WithName("SetProgramPricing");
}
