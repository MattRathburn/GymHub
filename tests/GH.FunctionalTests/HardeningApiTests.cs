using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GH.FunctionalTests;

/// <summary>Upload rules (allowlist, stored size and type) and API rate limiting.</summary>
public class HardeningApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record IdResponse(Guid Id);
    private sealed record Upload(string ObjectKey, Uri UploadUrl);

    private FakeObjectStorage Storage => (FakeObjectStorage)factory.Services.GetRequiredService<IObjectStorage>();

    private static async Task<Upload> RequestAvatarUploadAsync(HttpClient member, string contentType, string fileName = "me.png")
    {
        var response = await member.PostAsJsonAsync("/api/v1/trainers/me/member-profile/avatar/uploads", new { fileName, contentType, size = 1024L }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Upload>(Json, Ct))!;
    }

    private async Task<HttpClient> MemberWithProfileAsync()
    {
        var member = factory.ClientFor($"member-{Guid.NewGuid():N}");
        var saved = await member.PutAsJsonAsync("/api/v1/trainers/me/member-profile", new { displayName = "Uploader" }, Json, Ct);
        Assert.True(saved.IsSuccessStatusCode);
        return member;
    }

    private static Task<HttpResponseMessage> SetAvatarAsync(HttpClient member, string objectKey) =>
        member.PutAsJsonAsync("/api/v1/trainers/me/member-profile/avatar", new { objectKey }, Json, Ct);

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("video/mp4")]
    public async Task Avatar_uploads_allow_only_raster_images(string contentType)
    {
        var member = await MemberWithProfileAsync();

        var response = await member.PostAsJsonAsync("/api/v1/trainers/me/member-profile/avatar/uploads", new { fileName = "x.png", contentType, size = 1024L }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Object_keys_take_their_extension_from_the_type_not_the_file_name()
    {
        var member = await MemberWithProfileAsync();

        var upload = await RequestAvatarUploadAsync(member, "image/jpeg", fileName: "evil.html");

        Assert.EndsWith(".jpg", upload.ObjectKey);
    }

    [Fact]
    public async Task Oversized_or_retyped_objects_are_refused_and_deleted()
    {
        var member = await MemberWithProfileAsync();

        var tooBig = await RequestAvatarUploadAsync(member, "image/png");
        Storage.Objects[tooBig.ObjectKey] = new StoredObject(UploadRules.MaxImageBytes + 1, "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAvatarAsync(member, tooBig.ObjectKey)).StatusCode);
        Assert.False(Storage.Objects.ContainsKey(tooBig.ObjectKey));

        // Uploaded with a different Content-Type than the one presigned (or stored by some other route).
        var retyped = await RequestAvatarUploadAsync(member, "image/png");
        Storage.Objects[retyped.ObjectKey] = new StoredObject(1024, "text/html");
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAvatarAsync(member, retyped.ObjectKey)).StatusCode);
        Assert.False(Storage.Objects.ContainsKey(retyped.ObjectKey));

        var missing = await RequestAvatarUploadAsync(member, "image/png");
        Storage.Objects.TryRemove(missing.ObjectKey, out _);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAvatarAsync(member, missing.ObjectKey)).StatusCode);

        var fine = await RequestAvatarUploadAsync(member, "image/png");
        Storage.Objects[fine.ObjectKey] = new StoredObject(UploadRules.MaxImageBytes, "image/png");
        Assert.Equal(HttpStatusCode.OK, (await SetAvatarAsync(member, fine.ObjectKey)).StatusCode);
    }

    [Fact]
    public async Task Program_media_takes_large_video_but_covers_must_be_images()
    {
        var trainer = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "Upload rules", kind = "Ongoing" }, Json, Ct);
        var programId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        async Task<Upload> RequestAsync(string contentType)
        {
            var response = await trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/media/uploads", new { fileName = "clip", contentType, size = 1024L }, Json, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<Upload>(Json, Ct))!;
        }

        Task<HttpResponseMessage> AttachAsync(string target, Upload upload) =>
            trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/media",
                new { target, targetId = (Guid?)null, objectKey = upload.ObjectKey, contentType = "image/png", caption = (string?)null }, Json, Ct);

        var video = await RequestAsync("video/mp4");
        Storage.Objects[video.ObjectKey] = new StoredObject(400L * 1024 * 1024, "video/mp4");

        // The stored type wins over the request's claimed type, so it's attached as video, and isn't a valid cover.
        Assert.Equal(HttpStatusCode.BadRequest, (await AttachAsync("Cover", video)).StatusCode);
        var video2 = await RequestAsync("video/webm");
        Storage.Objects[video2.ObjectKey] = new StoredObject(400L * 1024 * 1024, "video/webm");
        Assert.Equal(HttpStatusCode.Created, (await AttachAsync("Program", video2)).StatusCode);

        var hugeVideo = await RequestAsync("video/mp4");
        Storage.Objects[hugeVideo.ObjectKey] = new StoredObject(UploadRules.MaxVideoBytes + 1, "video/mp4");
        Assert.Equal(HttpStatusCode.BadRequest, (await AttachAsync("Program", hugeVideo)).StatusCode);

        var other = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");
        var foreign = await other.PostAsJsonAsync($"/api/v1/programs/{programId}/media/uploads", new { fileName = "x", contentType = "image/png", size = 1024L }, Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/media/uploads", new { fileName = "x.svg", contentType = "image/svg+xml", size = 1024L }, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_limited_per_minute_but_the_Stripe_webhook_is_not()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:AnonymousPermitsPerMinute", "2"));
        var anonymous = limited.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/programs/", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/programs/", Ct)).StatusCode);

        var refused = await anonymous.GetAsync("/api/v1/programs/", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter is not null);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);

        for (var i = 0; i < 3; i++)
        {
            var webhook = await anonymous.PostAsync("/api/v1/billing/webhooks/stripe", new StringContent("{}"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, webhook.StatusCode);
        }
    }

    [Fact]
    public async Task Health_answers_direct_probes_but_not_requests_that_came_through_a_proxy()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive", Ct)).StatusCode);

        // What the Gateway sends for /gh-api/alive (YARP adds X-Forwarded-For), in any case of path.
        foreach (var (header, path) in new[] { ("X-Forwarded-For", "/alive"), ("X-Original-For", "/ALIVE"), ("Forwarded", "/health/") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation(header, "203.0.113.7");
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Upload_requests_declare_a_size_within_the_types_limit()
    {
        var member = await MemberWithProfileAsync();

        async Task<HttpStatusCode> RequestAsync(long size) => (await member.PostAsJsonAsync(
            "/api/v1/trainers/me/member-profile/avatar/uploads", new { fileName = "me.png", contentType = "image/png", size }, Json, Ct)).StatusCode;

        Assert.Equal(HttpStatusCode.OK, await RequestAsync(UploadRules.MaxImageBytes));
        Assert.Equal(HttpStatusCode.BadRequest, await RequestAsync(UploadRules.MaxImageBytes + 1));
        Assert.Equal(HttpStatusCode.BadRequest, await RequestAsync(0));
    }

    [Fact]
    public async Task A_failed_attach_never_deletes_media_that_is_already_attached()
    {
        var trainer = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "In use", kind = "Ongoing" }, Json, Ct);
        var programId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;

        var response = await trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/media/uploads", new { fileName = "clip", contentType = "video/mp4", size = 1024L }, Json, Ct);
        var video = (await response.Content.ReadFromJsonAsync<Upload>(Json, Ct))!;

        Task<HttpResponseMessage> AttachAsync(string target) => trainer.PostAsJsonAsync($"/api/v1/programs/{programId}/media",
            new { target, targetId = (Guid?)null, objectKey = video.ObjectKey, contentType = "video/mp4", caption = (string?)null }, Json, Ct);

        Assert.Equal(HttpStatusCode.Created, (await AttachAsync("Program")).StatusCode);

        // Picking the attached video as the cover is refused, and the video stays.
        Assert.Equal(HttpStatusCode.BadRequest, (await AttachAsync("Cover")).StatusCode);
        Assert.True(Storage.Objects.ContainsKey(video.ObjectKey));
    }

    [Fact]
    public async Task Sweepers_delete_old_uploads_nothing_points_at()
    {
        var member = await MemberWithProfileAsync();
        var attached = await RequestAvatarUploadAsync(member, "image/png");
        Assert.Equal(HttpStatusCode.OK, (await SetAvatarAsync(member, attached.ObjectKey)).StatusCode);
        var abandoned = await RequestAvatarUploadAsync(member, "image/png");
        var fresh = await RequestAvatarUploadAsync(member, "image/png");

        var trainer = factory.ClientFor($"trainer-{Guid.NewGuid():N}", "trainer");
        var created = await trainer.PostAsJsonAsync("/api/v1/programs/", new { title = "Sweep", kind = "Ongoing" }, Json, Ct);
        var programId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json, Ct))!.Id;
        var orphanMedia = $"programs/{programId:N}/{Guid.NewGuid():N}.mp4";
        Storage.Objects[orphanMedia] = new StoredObject(1024, "video/mp4");

        var old = DateTime.UtcNow - OrphanedUploadSweeper.GracePeriod - TimeSpan.FromHours(1);
        Storage.LastModified[attached.ObjectKey] = old;
        Storage.LastModified[abandoned.ObjectKey] = old;
        Storage.LastModified[orphanMedia] = old;

        await factory.Services.GetRequiredService<GymHub.Modules.Trainers.AvatarSweeper>().SweepOnceAsync(Ct);
        await factory.Services.GetRequiredService<GymHub.Modules.Programs.ProgramMediaSweeper>().SweepOnceAsync(Ct);

        Assert.True(Storage.Objects.ContainsKey(attached.ObjectKey));
        Assert.False(Storage.Objects.ContainsKey(abandoned.ObjectKey));
        Assert.True(Storage.Objects.ContainsKey(fresh.ObjectKey));
        Assert.False(Storage.Objects.ContainsKey(orphanMedia));
    }

    [Fact]
    public void Outside_Development_the_trusted_proxies_must_be_configured()
    {
        static WebApplication Build(params (string Key, string Value)[] settings)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.Configuration.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
            builder.ConfigureForwardedHeaders();
            return builder.Build();
        }

        Assert.Throws<InvalidOperationException>(() => Build().UseDefaultForwardedHeaders());
        Build(("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8")).UseDefaultForwardedHeaders();
        Build(("ForwardedHeaders:NoProxy", "true")).UseDefaultForwardedHeaders();
    }
}
