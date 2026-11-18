using EventBus.Abstractions;
using GymHub.BuildingBlocks.Auth;
using GymHub.BuildingBlocks.Identity;
using GymHub.Modules.Trainers.Contracts;

namespace GymHub.Modules.Trainers.Identity;

/// <summary>
/// Removes the Keycloak "trainer" role. Runs from the outbox; removing it twice is harmless. The user's current
/// access token keeps the role until the Gateway refreshes it.
/// </summary>
internal sealed class RevokeTrainerRoleHandler(IIdentityAdmin identityAdmin) : IIntegrationEventHandler<TrainerRevoked>
{
    public Task Handle(TrainerRevoked @event) =>
        identityAdmin.RemoveRealmRoleAsync(@event.UserId, Roles.Trainer, CancellationToken.None);
}
