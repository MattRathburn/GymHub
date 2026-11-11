using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Domain;

namespace GH.UnitTests.Programs;

public class TrainingProgramTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static TrainingProgram Ongoing() =>
        TrainingProgram.Create("trainer-1", "Strength", null, null, ProgramKind.Ongoing, null, null, Now).Value!;

    private static TrainingProgram Finite() =>
        TrainingProgram.Create("trainer-1", "8 weeks", null, null, ProgramKind.Finite, Today, Today.AddDays(55), Now).Value!;

    [Fact]
    public void Finite_program_needs_start_and_end_dates()
    {
        var result = TrainingProgram.Create("t", "x", null, null, ProgramKind.Finite, Today, null, Now);

        Assert.Equal(ErrorKind.Invalid, result.Error?.Kind);
    }

    [Fact]
    public void New_program_is_a_free_draft()
    {
        var program = Ongoing();

        Assert.Equal(ProgramStatus.Draft, program.Status);
        Assert.True(program.CurrentPrice.Price.IsFree);
        Assert.True(program.IsFree);
    }

    [Fact]
    public void Publishing_needs_at_least_one_week()
    {
        var result = Ongoing().Publish(Now);

        Assert.Equal(ErrorKind.Invalid, result.Error?.Kind);
    }

    [Fact]
    public void Finite_program_rejects_weeks_outside_its_dates()
    {
        var result = Finite().AddWeek("Too late", Today.AddDays(60));

        Assert.Equal(ErrorKind.Invalid, result.Error?.Kind);
    }

    [Fact]
    public void Publishing_raises_ProgramPublished_with_the_current_price()
    {
        var program = Ongoing();
        program.AddWeek("Week 1", Today);
        program.SetPricing(new ProgramPrice(PricingModel.Recurring, 1999, "USD", BillingInterval.Monthly), Now);

        var result = program.Publish(Now);

        Assert.True(result.IsSuccess);
        var published = Assert.IsType<ProgramPublished>(Assert.Single(program.Events));
        Assert.Equal(1999, published.Price.AmountMinor);
        Assert.Equal(1, published.PriceVersion);
    }

    [Fact]
    public void Draft_price_changes_replace_the_price_and_published_ones_add_a_version()
    {
        var program = Ongoing();
        program.SetPricing(new ProgramPrice(PricingModel.OneTime, 4900, "USD", null), Now);
        program.SetPricing(new ProgramPrice(PricingModel.OneTime, 5900, "USD", null), Now);
        Assert.Single(program.Prices);

        program.AddWeek("Week 1", Today);
        program.Publish(Now);
        program.ClearEvents();

        program.SetPricing(new ProgramPrice(PricingModel.OneTime, 6900, "USD", null), Now);

        Assert.Equal(2, program.Prices.Count);
        Assert.Equal(6900, program.CurrentPrice.Price.AmountMinor);
        var changed = Assert.IsType<ProgramPricingChanged>(Assert.Single(program.Events));
        Assert.Equal(2, changed.PriceVersion);
    }

    [Theory]
    [InlineData(PricingModel.OneTime, null, "USD", null)]
    [InlineData(PricingModel.Recurring, 1000L, "USD", null)]
    [InlineData(PricingModel.Free, 1000L, null, null)]
    public void Invalid_prices_are_rejected(PricingModel model, long? amount, string? currency, BillingInterval? interval)
    {
        var result = Ongoing().SetPricing(new ProgramPrice(model, amount, currency, interval), Now);

        Assert.Equal(ErrorKind.Invalid, result.Error?.Kind);
    }

    [Theory]
    [InlineData(PricingModel.Recurring, 31)]
    [InlineData(PricingModel.OneTime, 7)]
    public void Trials_are_for_subscriptions_and_up_to_30_days(PricingModel model, int trialDays)
    {
        var interval = model == PricingModel.Recurring ? BillingInterval.Monthly : (BillingInterval?)null;

        var result = Ongoing().SetPricing(new ProgramPrice(model, 1000, "USD", interval, trialDays), Now);

        Assert.Equal(ErrorKind.Invalid, result.Error?.Kind);
    }

    [Fact]
    public void A_published_subscription_keeps_its_currency()
    {
        var program = Ongoing();
        program.AddWeek("Week 1", Today);
        program.SetPricing(new ProgramPrice(PricingModel.Recurring, 1999, "USD", BillingInterval.Monthly, 7), Now);
        program.Publish(Now);

        Assert.Equal(ErrorKind.Conflict, program.SetPricing(new ProgramPrice(PricingModel.Recurring, 1999, "EUR", BillingInterval.Monthly), Now).Error?.Kind);
        Assert.True(program.SetPricing(new ProgramPrice(PricingModel.Recurring, 2499, "USD", BillingInterval.Yearly), Now).IsSuccess);
        Assert.True(program.SetPricing(new ProgramPrice(PricingModel.OneTime, 9900, "EUR", null), Now).IsSuccess);
    }

    [Fact]
    public void Only_weeks_on_or_before_today_are_released()
    {
        var program = Ongoing();
        program.AddWeek("Past", Today.AddDays(-7));
        program.AddWeek("Today", Today);
        program.AddWeek("Future", Today.AddDays(7));

        var released = program.ReleasedWeeks(Today).Select(w => w.Title);

        Assert.Equal(["Past", "Today"], released);
    }

    [Fact]
    public void Released_weeks_of_a_published_program_cannot_be_removed()
    {
        var program = Ongoing();
        var week = program.AddWeek("Week 1", Today).Value!;
        program.Publish(Now);

        var result = program.RemoveWeek(week.Id, Today);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public void Week_release_is_announced_once_and_not_for_weeks_out_at_publish()
    {
        var program = Ongoing();
        program.AddWeek("Out at publish", Today);
        program.AddWeek("Next week", Today.AddDays(7));
        program.Publish(Now);
        program.ClearEvents();

        Assert.Equal(0, program.AnnounceReleasedWeeks(Now));
        Assert.Equal(1, program.AnnounceReleasedWeeks(Now.AddDays(7)));
        Assert.Equal(0, program.AnnounceReleasedWeeks(Now.AddDays(8)));

        var released = Assert.IsType<ProgramWeekReleased>(Assert.Single(program.Events));
        Assert.Equal(2, released.WeekNumber);
    }

    [Fact]
    public void Saving_a_workout_keeps_the_ids_of_exercises_sent_back()
    {
        var program = Ongoing();
        var week = program.AddWeek("Week 1", Today).Value!;
        program.UpsertDay(week.Id, 1, "Push", isRestDay: false, null);
        var sets = new List<ExerciseSet> { new(5, 100, null, null, 180, 8) };

        var workout = program.SaveWorkout(week.Id, 1, null, "Push A", null,
            [new ExerciseDraft(null, "Bench", null, sets), new ExerciseDraft(null, "Dips", null, sets)]).Value!;
        var benchId = workout.Exercises[0].Id;

        program.SaveWorkout(week.Id, 1, workout.Id, "Push A", null,
            [new ExerciseDraft(null, "Overhead press", null, sets), new ExerciseDraft(benchId, "Bench", "Paused", sets)]);

        Assert.Equal(["Overhead press", "Bench"], workout.Exercises.OrderBy(e => e.Order).Select(e => e.Name));
        Assert.Equal(benchId, workout.Exercises.Single(e => e.Name == "Bench").Id);
        Assert.DoesNotContain(workout.Exercises, e => e.Name == "Dips");
    }

    [Fact]
    public void Rest_days_cannot_have_workouts()
    {
        var program = Ongoing();
        var week = program.AddWeek("Week 1", Today).Value!;
        program.UpsertDay(week.Id, 3, "Rest", isRestDay: true, null);

        var result = program.SaveWorkout(week.Id, 3, null, "Sneaky", null, []);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public void Media_must_come_from_the_programs_own_upload_prefix()
    {
        var program = Ongoing();

        var foreign = program.AttachMedia(MediaTarget.Program, null, "programs/someone-else/x.jpg", "image/jpeg", null);
        var own = program.AttachMedia(MediaTarget.Cover, null, $"{program.MediaKeyPrefix}cover.jpg", "image/jpeg", null);

        Assert.Equal(ErrorKind.Invalid, foreign.Error?.Kind);
        Assert.True(own.IsSuccess);
        Assert.Equal($"{program.MediaKeyPrefix}cover.jpg", program.CoverObjectKey);
    }

    [Fact]
    public void Archived_programs_cannot_be_edited()
    {
        var program = Ongoing();
        program.Archive();

        var result = program.UpdateDetails("New", null, null, ProgramKind.Ongoing, null, null);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }
}
