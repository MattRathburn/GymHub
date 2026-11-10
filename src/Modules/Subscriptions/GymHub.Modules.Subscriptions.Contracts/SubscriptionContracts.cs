using EventBus.Events;

namespace GymHub.Modules.Subscriptions.Contracts;

/// <summary>A user left a program, so they lose its members-only content.</summary>
public sealed record EnrollmentCancelled(string UserId, Guid ProgramId) : IntegrationEvent;
