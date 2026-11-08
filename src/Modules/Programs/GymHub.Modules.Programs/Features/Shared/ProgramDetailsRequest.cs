using FluentValidation;
using GymHub.Modules.Programs.Domain;

namespace GymHub.Modules.Programs.Features.Shared;

/// <summary>Body for creating a program and for updating its details.</summary>
public sealed record ProgramDetailsRequest(string Title, string? Summary, string? Description, ProgramKind Kind, DateOnly? StartsOn, DateOnly? EndsOn);

internal sealed class ProgramDetailsValidator : AbstractValidator<ProgramDetailsRequest>
{
    public ProgramDetailsValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Summary).MaximumLength(500);
        RuleFor(r => r.Description).MaximumLength(20_000);
        RuleFor(r => r.Kind).IsInEnum();
    }
}
