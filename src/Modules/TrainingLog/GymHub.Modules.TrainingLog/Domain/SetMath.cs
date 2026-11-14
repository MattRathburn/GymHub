using System.Text;

namespace GymHub.Modules.TrainingLog.Domain;

/// <summary>Turns exercise names into keys, so "Back Squat" and "back  squat" share one progress line.</summary>
public static class ExerciseKeys
{
    public const int MaxLength = 200;

    /// <summary>Lower-case letters and digits, with every other run of characters as one hyphen ("Bench Press (DB)" → "bench-press-db").</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var key = new StringBuilder(name.Length);
        var pendingHyphen = false;

        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingHyphen && key.Length > 0)
                {
                    key.Append('-');
                }

                key.Append(char.ToLowerInvariant(c));
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return key.Length > MaxLength ? key.ToString(0, MaxLength).TrimEnd('-') : key.ToString();
    }
}

public sealed record SetSummary(decimal? TopWeightKg, decimal? BestE1RmKg, decimal VolumeKg, int TotalReps);

public static class SetMath
{
    /// <summary>Estimated one-rep maxes are only trusted up to this many reps.</summary>
    public const int MaxRepsForE1Rm = 12;

    /// <summary>Epley: weight × (1 + reps / 30), and the weight itself for a single. Null when it can't be estimated.</summary>
    public static decimal? EstimatedOneRepMax(int? reps, decimal? weightKg)
    {
        if (reps is not { } r || weightKg is not { } w || w <= 0 || r < 1 || r > MaxRepsForE1Rm)
        {
            return null;
        }

        return r == 1 ? w : Math.Round(w * (1 + r / 30m), 1, MidpointRounding.AwayFromZero);
    }

    public static SetSummary Summarize(IReadOnlyCollection<LoggedSet> sets)
    {
        decimal? topWeight = null;
        decimal? bestE1Rm = null;
        decimal volume = 0;
        var reps = 0;

        foreach (var set in sets)
        {
            if (set.WeightKg is { } weight && weight > 0 && (topWeight is null || weight > topWeight))
            {
                topWeight = weight;
            }

            if (EstimatedOneRepMax(set.Reps, set.WeightKg) is { } e1Rm && (bestE1Rm is null || e1Rm > bestE1Rm))
            {
                bestE1Rm = e1Rm;
            }

            if (set.Reps is { } r)
            {
                reps += r;
                volume += r * (set.WeightKg ?? 0);
            }
        }

        return new SetSummary(topWeight, bestE1Rm, volume, reps);
    }
}

public enum RecordKind
{
    Weight,
    E1Rm
}

/// <summary>An exercise's numbers in one session.</summary>
public sealed record RecordEntry(Guid SessionId, DateOnly PerformedOn, DateTime LoggedAt, decimal? TopWeightKg, decimal? BestE1RmKg);

/// <summary>A session that beat every earlier one.</summary>
public sealed record RecordEvent(Guid SessionId, DateOnly PerformedOn, RecordKind Kind, decimal Value, decimal Previous);

/// <summary>
/// Personal records are derived from the log, never stored, so edits and deletes can't leave a stale record. A session
/// sets a record when it beats every earlier session (by date, then by when it was logged); the first one is the baseline.
/// </summary>
public static class PersonalRecords
{
    public static IReadOnlyList<RecordEvent> History(IEnumerable<RecordEntry> entries)
    {
        var sessions = entries
            .GroupBy(e => e.SessionId)
            .Select(g => new RecordEntry(
                g.Key,
                g.First().PerformedOn,
                g.First().LoggedAt,
                g.Max(e => e.TopWeightKg),
                g.Max(e => e.BestE1RmKg)))
            .OrderBy(e => e.PerformedOn)
            .ThenBy(e => e.LoggedAt)
            .ThenBy(e => e.SessionId);

        var events = new List<RecordEvent>();
        decimal? bestWeight = null;
        decimal? bestE1Rm = null;

        foreach (var session in sessions)
        {
            Check(session, RecordKind.Weight, session.TopWeightKg, ref bestWeight, events);
            Check(session, RecordKind.E1Rm, session.BestE1RmKg, ref bestE1Rm, events);
        }

        return events;
    }

    private static void Check(RecordEntry session, RecordKind kind, decimal? value, ref decimal? best, List<RecordEvent> events)
    {
        if (value is not { } v)
        {
            return;
        }

        if (best is { } b && v > b)
        {
            events.Add(new RecordEvent(session.SessionId, session.PerformedOn, kind, v, b));
        }

        if (best is null || v > best)
        {
            best = v;
        }
    }
}
