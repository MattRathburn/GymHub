using EventBus.Events;

namespace GymHub.Modules.Trainers.Contracts;

/// <summary>An admin approved a trainer application. UserId is the Keycloak "sub" of the new trainer.</summary>
public sealed record TrainerApproved(string UserId, Guid ApplicationId, string DisplayName) : IntegrationEvent;

/// <summary>
/// An admin took the trainer role away. The role is removed in Keycloak and the trainer's published programs are
/// archived (buyers keep access).
/// </summary>
public sealed record TrainerRevoked(string UserId) : IntegrationEvent;
