using Sprawl.Contracts;
using Sprawl.Domain;

namespace Sprawl.Domain.Tests;

public sealed class ReviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Good_answers_space_out_and_again_resets()
    {
        var state = Review.Initial(Start);
        Assert.Equal(Start.AddDays(1), state.DueAt);

        var intervals = new List<double>();
        var now = state.DueAt;
        for (var i = 0; i < 4; i++)
        {
            state = Review.Next(state, ReviewGrades.Good, now);
            intervals.Add(state.IntervalDays);
            now = state.DueAt;
        }
        Assert.Equal([1, 3, 7.5, 18.75], intervals);

        state = Review.Next(state, ReviewGrades.Again, now);
        Assert.Equal((0, 1.0, 2.3), (state.Reps, state.IntervalDays, state.Ease));
        Assert.Equal(now.AddDays(1), state.DueAt);
    }

    [Fact]
    public void Ease_has_a_floor_and_intervals_a_ceiling_and_unknown_grades_are_refused()
    {
        var state = Review.Initial(Start);
        for (var i = 0; i < 20; i++) state = Review.Next(state, ReviewGrades.Hard, Start);
        Assert.Equal(Review.MinEase, state.Ease);

        for (var i = 0; i < 20; i++) state = Review.Next(state, ReviewGrades.Easy, Start);
        Assert.Equal(Review.MaxIntervalDays, state.IntervalDays);

        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<SprawlException>(() => Review.Next(state, "meh", Start)).Code);
    }
}
