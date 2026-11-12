using EventBus.Events;

namespace GymHub.Modules.Subscriptions.Contracts;

/// <summary>A user left a program, so they lose its members-only content.</summary>
public sealed record EnrollmentCancelled(string UserId, Guid ProgramId) : IntegrationEvent;

/// <summary>A user joined a free program (paid ones are announced by Billing's ProgramSold).</summary>
public sealed record EnrollmentStarted(string UserId, Guid ProgramId) : IntegrationEvent;
