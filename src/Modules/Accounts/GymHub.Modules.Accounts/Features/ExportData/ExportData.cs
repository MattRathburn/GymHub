using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Accounts.Features.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GymHub.Modules.Accounts.Features.ExportData;

/// <summary>Everything GymHub stores about the caller, one section per module, as a JSON file download.</summary>
internal sealed class ExportDataHandler(PersonalData personalData, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<byte[]> Handle(CurrentUser user, CancellationToken cancellationToken)
    {
        var sections = new SortedDictionary<string, object>(StringComparer.Ordinal);

        foreach (var source in personalData.Sources)
        {
            // Add, not the indexer: two sources sharing a name must fail loudly, not drop one module's data.
            sections.Add(source.Section, await source.ExportAsync(user.Id, cancellationToken));
        }

        var export = new { userId = user.Id, exportedAt = timeProvider.GetUtcNow().UtcDateTime, data = sections };
        return JsonSerializer.SerializeToUtf8Bytes(export, Json);
    }
}

internal static class ExportDataEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/export", async (ClaimsPrincipal user, ExportDataHandler handler, CancellationToken ct) =>
                TypedResults.File(await handler.Handle(CurrentUser.Required(user), ct), "application/json", "gymhub-data.json"))
            .RequireAuthorization()
            .WithName("ExportMyData");
}
