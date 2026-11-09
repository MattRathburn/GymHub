using EventBus.Events;

namespace GymHub.Modules.Trainers.Contracts;

/// <summary>An admin approved a trainer application. UserId is the Keycloak "sub" of the new trainer.</summary>
public sealed record TrainerApproved(string UserId, Guid ApplicationId, string DisplayName) : IntegrationEvent;
