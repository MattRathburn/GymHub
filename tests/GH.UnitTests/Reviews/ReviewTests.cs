using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Reviews.Contracts;
using GymHub.Modules.Reviews.Domain;

namespace GH.UnitTests.Reviews;

public class ReviewTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ProgramId = Guid.NewGuid();

    private static Result<Review> Write(int stars = 4, string? body = "Solid program.", string author = "member") =>
        Review.Write(ProgramId, "trainer", author, "member-name", stars, body, Now);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Stars_must_be_1_to_5(int stars)
    {
        var review = Write(stars);

        Assert.False(review.IsSuccess);
        Assert.Equal(ErrorKind.Invalid, review.Error!.Kind);
    }

    [Fact]
    public void Text_is_optional_trimmed_and_limited()
    {
        Assert.Null(Write(body: "   ").Value!.Body);
        Assert.Equal("Great", Write(body: "  Great \n").Value!.Body);
        Assert.Equal(ErrorKind.Invalid, Write(body: new string('a', Review.MaxBodyLength + 1)).Error!.Kind);
    }

    [Fact]
    public void Trainers_cannot_review_their_own_program() =>
        Assert.Equal(ErrorKind.Forbidden, Write(author: "trainer").Error!.Kind);

    [Fact]
    public void Editing_changes_stars_text_and_time()
    {
        var review = Write().Value!;

        Assert.True(review.Edit(2, null, Now.AddDays(1)).IsSuccess);
        Assert.Equal(2, review.Stars);
        Assert.Null(review.Body);
        Assert.Equal(Now, review.CreatedAt);
        Assert.Equal(Now.AddDays(1), review.UpdatedAt);
        Assert.False(review.Edit(9, null, Now).IsSuccess);
        Assert.Equal(2, review.Stars);
    }

    [Fact]
    public void A_reply_keeps_its_first_time_when_edited_and_can_be_removed()
    {
        var review = Write().Value!;

        Assert.Equal(ErrorKind.Invalid, review.SetReply("  ", Now).Error!.Kind);
        Assert.True(review.SetReply("Thanks!", Now).IsSuccess);
        Assert.True(review.SetReply(" Thanks a lot! ", Now.AddHours(1)).IsSuccess);

        Assert.Equal("Thanks a lot!", review.Reply);
        Assert.Equal(Now, review.RepliedAt);
        Assert.Equal(Now.AddHours(1), review.ReplyUpdatedAt);

        review.RemoveReply();
        Assert.Null(review.Reply);
        Assert.Null(review.RepliedAt);
    }

    [Fact]
    public void Reports_follow_the_discussion_rules()
    {
        var review = Write().Value!;

        Assert.Equal(ErrorKind.Invalid, ReviewReport.File(review, "member", ReportReason.Spam, null, Now).Error!.Kind);

        var report = ReviewReport.File(review, "someone", ReportReason.Abuse, "  rude ", Now).Value!;
        Assert.Equal("rude", report.Note);
        Assert.Equal(review.ProgramId, report.ProgramId);
        Assert.True(report.Resolve(ReportStatus.Dismissed, "admin", Now).IsSuccess);
        Assert.Equal(ErrorKind.Conflict, report.Resolve(ReportStatus.Actioned, "admin", Now).Error!.Kind);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(3, 13, 4.3)]
    [InlineData(2, 9, 4.5)]
    [InlineData(1, 5, 5.0)]
    public void Averages_round_to_one_decimal(int count, int sum, double? average)
    {
        decimal? expected = average is null ? null : (decimal)average.Value;
        Assert.Equal(expected, Ratings.Average(count, sum));
    }

    [Fact]
    public void A_rating_publishes_a_new_version_only_when_it_changes()
    {
        var rating = new ProgramRating(ProgramId);

        var first = rating.Update(0, 0, Now);
        Assert.Equal(1, first!.Version);

        Assert.Null(rating.Update(0, 0, Now));

        var second = rating.Update(2, 9, Now);
        Assert.Equal((ProgramId, 2, 9, 2L), (second!.ProgramId, second.Count, second.Sum, second.Version));
        Assert.Equal(4.5m, rating.Average);
    }
}
