using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Admin;
using GymHub.Modules.Billing.Data;
using GymHub.Modules.Billing.Domain;
using GymHub.Modules.Programs.Contracts;
using GymHub.Modules.Programs.Data;
using GymHub.Modules.Trainers.Data;
using GymHub.Modules.Trainers.Domain;
using Microsoft.EntityFrameworkCore;

namespace GH.FunctionalTests;

public class AdminApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record IdResponse(Guid Id);
    private sealed record Suspension(DateTime SuspendedAt, string? Reason);
    private sealed record AdminUser(string Id, string Username, string? DisplayName, bool Enabled, bool IsTrainer, bool IsAdmin, Suspension? Suspension);
    private sealed record AdminProgram(Guid Id, string Title, string TrainerId, string? TrainerName, ProgramStatus Status);
    private sealed record Page<T>(int PageIndex, int PageSize, long Count, List<T> Data);

    private HttpClient Admin(out string adminId)
    {
        adminId = $"admin-{Guid.NewGuid():N}";
        factory.IdentityAdmin.AddUser(adminId, adminId, "admin");
        return factory.ClientFor(adminId, "admin");
    }

    private string NewUser(string prefix, params string[] roles)
    {
        var id = $"{prefix}-{Guid.NewGuid():N}";
        factory.IdentityAdmin.AddUser(id, id, roles);
        return id;
    }

    private async Task<Guid> CreateProgramAsync(HttpClient trainer, string title, bool publish)
    {
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title, kind = "Ongoing" }, Json, Ct);
        var programId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        if (publish)
        {
            await trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
            Assert.Equal(HttpStatusCode.NoContent, (await trainer.PostAsync($"/api/v1/programs/{programId}/publish", null, Ct)).StatusCode);
        }

        return programId;
    }

    private Task<ProgramStatus> StatusOfAsync(Guid programId) =>
        factory.WithDbAsync<ProgramsDbContext, ProgramStatus>(db => db.Programs.Where(p => p.Id == programId).Select(p => p.Status).SingleAsync(Ct));

    [Fact]
    public async Task Suspended_users_are_refused_until_reactivated()
    {
        var admin = Admin(out _);
        var memberId = NewUser("member");
        var member = factory.ClientFor(memberId);

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/account/", Ct)).StatusCode);

        var suspend = await admin.PostAsJsonAsync($"/api/v1/account/admin/users/{memberId}/suspension", new { reason = "Spam" }, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);
        Assert.False(factory.IdentityAdmin.IsEnabled(memberId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/account/", Ct)).StatusCode);

        // Suspending again is harmless; the list shows the suspension and its reason.
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"/api/v1/account/admin/users/{memberId}/suspension", new { reason = (string?)null }, Json, Ct)).StatusCode);
        var listed = await admin.GetFromJsonAsync<Page<AdminUser>>($"/api/v1/account/admin/users?search={memberId}", Json, Ct);
        var user = Assert.Single(listed!.Data);
        Assert.Equal(("Spam", false), (user.Suspension?.Reason, user.Enabled));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/account/admin/users/{memberId}/suspension", Ct)).StatusCode);
        Assert.True(factory.IdentityAdmin.IsEnabled(memberId));
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/account/", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/account/admin/users/{memberId}/suspension", Ct)).StatusCode);
    }

    [Fact]
    public async Task Admins_and_unknown_users_cannot_be_suspended_and_members_cannot_suspend()
    {
        var admin = Admin(out var adminId);
        var otherAdminId = NewUser("admin", "admin");
        var memberId = NewUser("member");

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/v1/account/admin/users/{adminId}/suspension", new { }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/v1/account/admin/users/{otherAdminId}/suspension", new { }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync("/api/v1/account/admin/users/nobody/suspension", new { }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/v1/account/admin/users/{memberId}/suspension", new { reason = new string('x', 501) }, Json, Ct)).StatusCode);

        var member = factory.ClientFor(memberId);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/account/admin/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync($"/api/v1/account/admin/users/{otherAdminId}/suspension", new { }, Json, Ct)).StatusCode);
        Assert.True(factory.IdentityAdmin.IsEnabled(memberId));
    }

    [Fact]
    public async Task User_list_flags_roles_and_shows_profile_names()
    {
        var admin = Admin(out _);
        var trainerId = NewUser("listed-trainer", "trainer");
        await factory.ClientFor(trainerId, "trainer").PutAsJsonAsync("/api/v1/trainers/me/profile",
            new { displayName = "Listed Coach", bio = (string?)null, specialties = Array.Empty<string>(), links = Array.Empty<string>() }, Json, Ct);

        var page = await admin.GetFromJsonAsync<Page<AdminUser>>("/api/v1/account/admin/users?search=listed-trainer", Json, Ct);

        var user = Assert.Single(page!.Data);
        Assert.Equal((1L, trainerId, "Listed Coach", true, false), (page.Count, user.Id, user.DisplayName, user.IsTrainer, user.IsAdmin));
    }

    [Fact]
    public async Task Revoking_a_trainer_removes_the_role_archives_published_programs_and_allows_reapplying()
    {
        var admin = Admin(out _);
        var trainerId = NewUser("revoked", "trainer");
        var trainer = factory.ClientFor(trainerId, "trainer");
        var published = await CreateProgramAsync(trainer, "Revoked trainer's program", publish: true);
        var draft = await CreateProgramAsync(trainer, "Revoked trainer's draft", publish: false);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/trainers/{trainerId}/revoke", null, Ct)).StatusCode);
        await factory.DispatchOutboxAsync<TrainersDbContext>();

        Assert.False(factory.IdentityAdmin.HasRole(trainerId, "trainer"));
        Assert.Equal(ProgramStatus.Archived, await StatusOfAsync(published));
        Assert.Equal(ProgramStatus.Draft, await StatusOfAsync(draft));

        // Their old token still says "trainer", but publishing is refused.
        await trainer.PostAsJsonAsync($"/api/v1/programs/{draft}/weeks", new { title = "Week 1", releasesOn = Today }, Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await trainer.PostAsync($"/api/v1/programs/{draft}/publish", null, Ct)).StatusCode);
        Assert.Equal(ProgramStatus.Draft, await StatusOfAsync(draft));

        // Without the role they can apply again (their token no longer carries it).
        var reapply = await factory.ClientFor(trainerId).PostAsJsonAsync("/api/v1/trainers/applications",
            new { displayName = "Back again", bio = (string?)null, specialties = Array.Empty<string>(), links = Array.Empty<string>() }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, reapply.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/v1/trainers/{NewUser("plain")}/revoke", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await trainer.PostAsync($"/api/v1/trainers/{trainerId}/revoke", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Roles_from_Keycloak_groups_count_and_are_not_revoked_from_GymHub()
    {
        var admin = Admin(out _);
        var groupAdminId = NewUser("group-admin");
        factory.IdentityAdmin.AddInheritedRole(groupAdminId, "admin");
        var groupTrainerId = NewUser("group-trainer");
        factory.IdentityAdmin.AddInheritedRole(groupTrainerId, "trainer");

        // An admin through a group is still an admin: flagged, and protected from suspension.
        var listed = Assert.Single((await admin.GetFromJsonAsync<Page<AdminUser>>($"/api/v1/account/admin/users?search={groupAdminId}", Json, Ct))!.Data);
        Assert.True(listed.IsAdmin);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/v1/account/admin/users/{groupAdminId}/suspension", new { }, Json, Ct)).StatusCode);
        Assert.True(factory.IdentityAdmin.IsEnabled(groupAdminId));

        // GymHub can't remove a group's role, so it says so instead of half-revoking.
        Assert.True(Assert.Single((await admin.GetFromJsonAsync<Page<AdminUser>>($"/api/v1/account/admin/users?search={groupTrainerId}", Json, Ct))!.Data).IsTrainer);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/trainers/{groupTrainerId}/revoke", null, Ct)).StatusCode);
        Assert.False(await factory.WithDbAsync<TrainersDbContext, bool>(db => db.Revocations.AnyAsync(r => r.UserId == groupTrainerId, Ct)));
    }

    [Fact]
    public async Task Suspending_twice_at_once_succeeds_and_one_reactivation_undoes_it()
    {
        var admin = Admin(out _);
        var memberId = NewUser("double");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            admin.PostAsJsonAsync($"/api/v1/account/admin/users/{memberId}/suspension", new { }, Json, Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.False(factory.IdentityAdmin.IsEnabled(memberId));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/account/admin/users/{memberId}/suspension", Ct)).StatusCode);
        Assert.True(factory.IdentityAdmin.IsEnabled(memberId));
    }

    [Fact]
    public async Task Revoking_marks_the_approved_application_revoked()
    {
        var admin = Admin(out _);
        var applicantId = NewUser("applicant");
        var applied = await factory.ClientFor(applicantId).PostAsJsonAsync("/api/v1/trainers/applications",
            new { displayName = "Applicant", bio = (string?)null, specialties = Array.Empty<string>(), links = Array.Empty<string>() }, Json, Ct);
        var applicationId = (await applied.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/trainers/applications/{applicationId}/approve", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/trainers/{applicantId}/revoke", null, Ct)).StatusCode);

        var status = await factory.WithDbAsync<TrainersDbContext, ApplicationStatus>(db =>
            db.Applications.Where(a => a.Id == applicationId).Select(a => a.Status).SingleAsync(Ct));
        Assert.Equal(ApplicationStatus.Revoked, status);

        // Approved again, they may publish again.
        var again = await factory.ClientFor(applicantId).PostAsJsonAsync("/api/v1/trainers/applications",
            new { displayName = "Applicant", bio = (string?)null, specialties = Array.Empty<string>(), links = Array.Empty<string>() }, Json, Ct);
        var againId = (await again.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/trainers/applications/{againId}/approve", null, Ct)).StatusCode);
        Assert.False(await factory.WithDbAsync<TrainersDbContext, bool>(db => db.Revocations.AnyAsync(r => r.UserId == applicantId, Ct)));
    }

    [Fact]
    public async Task Admin_program_list_covers_every_status_and_admins_can_archive_any_program()
    {
        var admin = Admin(out _);
        var trainerId = NewUser("listing", "trainer");
        var trainer = factory.ClientFor(trainerId, "trainer");
        var title = $"Moderated {Guid.NewGuid():N}";
        var published = await CreateProgramAsync(trainer, title, publish: true);
        var draft = await CreateProgramAsync(trainer, $"{title} draft", publish: false);

        var all = await admin.GetFromJsonAsync<Page<AdminProgram>>($"/api/v1/programs/admin?search={title}", Json, Ct);
        Assert.Equal([draft, published], all!.Data.Select(p => p.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/programs/{published}/archive", null, Ct)).StatusCode);

        var archived = await admin.GetFromJsonAsync<Page<AdminProgram>>($"/api/v1/programs/admin?search={title}&status=Archived", Json, Ct);
        Assert.Equal(published, Assert.Single(archived!.Data).Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await trainer.GetAsync("/api/v1/programs/admin", Ct)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_collects_every_modules_numbers()
    {
        var admin = Admin(out _);
        var trainer = factory.ClientFor(NewUser("metrics", "trainer"), "trainer");
        await CreateProgramAsync(trainer, "Metrics program", publish: true);

        var metrics = (await admin.GetFromJsonAsync<List<AdminMetric>>("/api/v1/account/admin/metrics", Json, Ct))!;

        Assert.Equal(
            new HashSet<string> { AdminSections.Users, AdminSections.Programs, AdminSections.Money, AdminSections.Moderation },
            metrics.Select(m => m.Section).ToHashSet());
        Assert.True(metrics.Single(m => m.Label == "Published programs").Value >= 1);
        Assert.True(metrics.Single(m => m.Label == "Users").Value >= 2);
        Assert.Contains(metrics, m => m.Label == "Open post reports");
        Assert.Contains(metrics, m => m.Label == "Open review reports");
        Assert.Contains(metrics, m => m.Label == "Trainer applications waiting");

        Assert.Equal(HttpStatusCode.Forbidden, (await trainer.GetAsync("/api/v1/account/admin/metrics", Ct)).StatusCode);
    }

    [Fact]
    public async Task Sales_count_net_of_refunds_without_disputes_and_fees_round_per_payment()
    {
        var admin = Admin(out _);
        var now = DateTime.UtcNow;

        // XTS (the ISO test currency) keeps these apart from other tests' payments.
        await factory.WithDbAsync<BillingDbContext, int>(async db =>
        {
            PurchasePayment Pay(long amountMinor)
            {
                var program = new ProgramSummary(Guid.NewGuid(), "metrics-trainer", "Metrics", ProgramStatus.Published,
                    new ProgramPrice(PricingModel.OneTime, amountMinor, "XTS", null), 1);
                var purchase = Purchase.Start($"buyer-{Guid.NewGuid():N}", program, 7.5m, trialEligible: false, now).Value!;
                db.Purchases.Add(purchase);

                var payment = new PurchasePayment($"pi_{Guid.NewGuid():N}", purchase.Id, now.AddDays(-1));
                db.Payments.Add(payment);
                return payment;
            }

            Pay(1000);                                // counts 1000, fee 75
            Pay(2000).RecordRefund(500, now);         // counts 1500, fee 112.5 -> 113
            Pay(3000).RecordDispute(now);             // the bank took it back
            Pay(4000).RecordRefund(4000, now);        // fully refunded
            return await db.SaveChangesAsync(Ct);
        });

        var metrics = (await admin.GetFromJsonAsync<List<AdminMetric>>("/api/v1/account/admin/metrics", Json, Ct))!;

        Assert.Equal(2500, metrics.Single(m => m.Label == "Sales (30 days)" && m.Currency == "XTS").Value);
        Assert.Equal(188, metrics.Single(m => m.Label == "Platform fees (30 days)" && m.Currency == "XTS").Value);
    }
}
