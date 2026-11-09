namespace GymHub.Modules.Trainers.Domain;

/// <summary>What a trainer says about themselves, shared by applications and profiles. Normalized on creation.</summary>
public sealed record TrainerDetails(string DisplayName, string? Bio, IReadOnlyList<string> Specialties, IReadOnlyList<string> Links)
{
    public static TrainerDetails Create(string displayName, string? bio, IEnumerable<string>? specialties, IEnumerable<string>? links) => new(
        displayName.Trim(),
        string.IsNullOrWhiteSpace(bio) ? null : bio.Trim(),
        Clean(specialties, StringComparer.OrdinalIgnoreCase),
        Clean(links, StringComparer.Ordinal));

    private static List<string> Clean(IEnumerable<string>? values, StringComparer comparer) =>
        [.. (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(comparer)];
}
