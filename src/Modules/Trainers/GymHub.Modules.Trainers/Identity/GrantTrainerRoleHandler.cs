using EventBus.Abstractions;
using GymHub.BuildingBlocks.Auth;
using GymHub.Modules.Trainers.Contracts;

namespace GymHub.Modules.Trainers.Identity;

/// <summary>
/// Gives an approved applicant the Keycloak "trainer" role. Runs from the outbox, so a Keycloak outage only delays
/// the grant; granting twice is harmless. The user sees the role after signing in again.
/// </summary>
internal sealed class GrantTrainerRoleHandler(IIdentityAdmin identityAdmin) : IIntegrationEventHandler<TrainerApproved>
{
    public Task Handle(TrainerApproved @event) =>
        identityAdmin.GrantRealmRoleAsync(@event.UserId, Roles.Trainer, CancellationToken.None);
}
