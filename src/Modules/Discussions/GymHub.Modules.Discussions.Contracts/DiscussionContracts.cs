using EventBus.Events;

namespace GymHub.Modules.Discussions.Contracts;

/// <summary>Someone started a thread on a program's board.</summary>
public sealed record DiscussionThreadStarted(Guid ThreadId, Guid ProgramId, string AuthorId, string Title) : IntegrationEvent;

/// <summary>Someone replied in a thread. ParentAuthorId is the author of the post being answered.</summary>
public sealed record DiscussionReplyPosted(Guid PostId, Guid ThreadId, Guid ProgramId, string AuthorId, string ParentAuthorId) : IntegrationEvent;

/// <summary>A reply was marked as the answer to a question thread.</summary>
public sealed record DiscussionAnswerMarked(Guid ThreadId, Guid PostId, Guid ProgramId, string PostAuthorId) : IntegrationEvent;
