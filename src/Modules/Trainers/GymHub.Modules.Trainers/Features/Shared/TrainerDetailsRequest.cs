using FluentValidation;
using GymHub.Modules.Trainers.Domain;

namespace GymHub.Modules.Trainers.Features.Shared;

/// <summary>Body of an application and of a profile edit.</summary>
public sealed record TrainerDetailsRequest(string DisplayName, string? Bio, IReadOnlyList<string>? Specialties, IReadOnlyList<string>? Links)
{
    public TrainerDetails ToDetails() => TrainerDetails.Create(DisplayName, Bio, Specialties, Links);
}

internal sealed class TrainerDetailsRequestValidator : AbstractValidator<TrainerDetailsRequest>
{
    public const int MaxSpecialties = 10;
    public const int MaxLinks = 5;

    public TrainerDetailsRequestValidator()
    {
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(r => r.Bio).MaximumLength(2000);

        RuleFor(r => r.Specialties).Must(s => s is null || s.Count <= MaxSpecialties)
            .WithMessage($"List at most {MaxSpecialties} specialties.");
        RuleForEach(r => r.Specialties).NotEmpty().MaximumLength(40);

        RuleFor(r => r.Links).Must(l => l is null || l.Count <= MaxLinks)
            .WithMessage($"Add at most {MaxLinks} links.");
        RuleForEach(r => r.Links).NotEmpty().MaximumLength(300)
            .Must(BeWebUrl).WithMessage("Links must be full http(s) URLs.");
    }

    private static bool BeWebUrl(string link) =>
        Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
