using GymHub.BuildingBlocks.Results;
using GymHub.Modules.Trainers.Contracts;
using GymHub.Modules.Trainers.Domain;

namespace GH.UnitTests.Trainers;

public class TrainerApplicationTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TrainerDetails Details =
        TrainerDetails.Create("  Sam Strong ", "  Coach ", ["Powerlifting", " powerlifting ", "", "Mobility"], ["https://example.com"]);

    private static TrainerApplication Pending(params TrainerApplication[] previous) =>
        TrainerApplication.Submit("user-1", Details, isAlreadyTrainer: false, previous, Now).Value!;

    [Fact]
    public void Details_are_trimmed_and_deduplicated()
    {
        Assert.Equal("Sam Strong", Details.DisplayName);
        Assert.Equal("Coach", Details.Bio);
        Assert.Equal(["Powerlifting", "Mobility"], Details.Specialties);
    }

    [Fact]
    public void New_application_is_pending()
    {
        var application = Pending();

        Assert.Equal(ApplicationStatus.Pending, application.Status);
        Assert.Equal("user-1", application.UserId);
        Assert.Empty(application.Events);
    }

    [Fact]
    public void Trainers_cannot_apply()
    {
        var result = TrainerApplication.Submit("user-1", Details, isAlreadyTrainer: true, [], Now);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public void Only_one_pending_application_at_a_time()
    {
        var result = TrainerApplication.Submit("user-1", Details, false, [Pending()], Now);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public void Approved_users_cannot_apply_again()
    {
        var approved = Pending();
        approved.Approve("admin", Now);

        var result = TrainerApplication.Submit("user-1", Details, false, [approved], Now);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public void Rejected_users_can_reapply()
    {
        var rejected = Pending();
        rejected.Reject("admin", "Add certifications", Now);

        var result = TrainerApplication.Submit("user-1", Details, false, [rejected], Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Approving_raises_TrainerApproved()
    {
        var application = Pending();

        var result = application.Approve("admin", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal("admin", application.ReviewedBy);
        var approved = Assert.IsType<TrainerApproved>(Assert.Single(application.Events));
        Assert.Equal("user-1", approved.UserId);
        Assert.Equal(application.Id, approved.ApplicationId);
    }

    [Fact]
    public void Reviewed_applications_cannot_be_reviewed_again()
    {
        var application = Pending();
        application.Approve("admin", Now);

        Assert.Equal(ErrorKind.Conflict, application.Approve("admin", Now).Error?.Kind);
        Assert.Equal(ErrorKind.Conflict, application.Reject("admin", "No", Now).Error?.Kind);
    }

    [Fact]
    public void Rejecting_needs_a_reason()
    {
        var application = Pending();

        Assert.Equal(ErrorKind.Invalid, application.Reject("admin", " ", Now).Error?.Kind);
        Assert.Equal(ApplicationStatus.Pending, application.Status);
    }

    [Fact]
    public void Users_cannot_follow_themselves()
    {
        Assert.Equal(ErrorKind.Invalid, Follow.Start("user-1", "user-1", Now).Error?.Kind);
        Assert.True(Follow.Start("user-1", "trainer-1", Now).IsSuccess);
    }
}
