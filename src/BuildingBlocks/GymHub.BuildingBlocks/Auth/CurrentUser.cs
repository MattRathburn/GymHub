using System.Security.Claims;
using GymHub.ServiceDefaults;

namespace GymHub.BuildingBlocks.Auth;

/// <summary>
/// The caller, reduced to what handlers need. Id is the Keycloak "sub".
/// </summary>
public sealed record CurrentUser(string Id, bool IsTrainer, bool IsAdmin)
{
    public static CurrentUser? From(ClaimsPrincipal principal)
    {
        var id = principal.GetUserId();

        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return new CurrentUser(id, principal.IsInRole(Roles.Trainer), principal.IsInRole(Roles.Admin));
    }

    /// <summary>
    /// For endpoints that already require authorization, where a missing "sub" is a configuration error.
    /// </summary>
    public static CurrentUser Required(ClaimsPrincipal principal) =>
        From(principal) ?? throw new InvalidOperationException("Authenticated principal has no 'sub' claim.");
}
