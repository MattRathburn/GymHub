using System.Security.Claims;
using FluentValidation;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Results;
using GymHub.BuildingBlocks.Validation;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Programs.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Programs.Features.SetProgramPricing;

public sealed record SetProgramPricingRequest(PricingModel Model, long? AmountMinor, string? Currency, BillingInterval? Interval);

internal sealed class SetProgramPricingValidator : AbstractValidator<SetProgramPricingRequest>
{
    public SetProgramPricingValidator()
    {
        RuleFor(r => r.Model).IsInEnum();
        RuleFor(r => r.Interval).IsInEnum();
        RuleFor(r => r.Currency).Matches("^[A-Z]{3}$").When(r => r.Currency is not null)
            .WithMessage("Currency must be a three-letter ISO 4217 code, e.g. USD.");
        RuleFor(r => r.AmountMinor).LessThanOrEqualTo(10_000_000);
    }
}

internal sealed class SetProgramPricingHandler(ProgramsDbContext db, TimeProvider timeProvider)
{
    public async Task<Result> Handle(Guid programId, SetProgramPricingRequest request, CurrentUser user, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadOwnedAsync(programId, user, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.Error!;
        }

        var price = new ProgramPrice(request.Model, request.AmountMinor, request.Currency, request.Interval);
        var result = loaded.Value!.SetPricing(price, timeProvider.UtcNow());

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
