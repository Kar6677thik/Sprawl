using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>
/// Spaced review of open questions (SM-2, as in Anki and RemNote): a question graded "again" comes back the next day;
/// one graded "good" comes back at growing intervals. A question is first due a day after it is written, so a fresh
/// batch from Claude does not land in today's queue.
/// </summary>
public static class Review
{
    public const double StartEase = 2.5;
    public const double MinEase = 1.3;
    public const double MaxIntervalDays = 365;

    public static ReviewState Initial(DateTimeOffset createdAt) => new(0, StartEase, 0, createdAt.AddDays(1), null);

    public static ReviewState Next(ReviewState current, string grade, DateTimeOffset now)
    {
        var (reps, ease, interval) = (current.Reps, current.Ease, current.IntervalDays);
        switch (grade)
        {
            case ReviewGrades.Again:
                (reps, interval, ease) = (0, 1, ease - 0.2);
                break;
            case ReviewGrades.Hard:
                (reps, interval, ease) = (reps + 1, Math.Max(1, interval * 1.2), ease - 0.15);
                break;
            case ReviewGrades.Good:
                interval = reps switch { 0 => 1, 1 => 3, _ => interval * ease };
                reps++;
                break;
            case ReviewGrades.Easy:
                interval = reps == 0 ? 4 : interval * ease * 1.3;
                (reps, ease) = (reps + 1, ease + 0.15);
                break;
            default:
                throw SprawlException.Invalid($"grade must be one of {string.Join(", ", ReviewGrades.All)}.");
        }
        ease = Math.Round(Math.Max(MinEase, ease), 2);
        interval = Math.Round(Math.Min(MaxIntervalDays, interval), 2);
        return new(reps, ease, interval, now.AddDays(interval), now);
    }
}
