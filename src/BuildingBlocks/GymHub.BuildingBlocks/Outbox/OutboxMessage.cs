using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymHub.BuildingBlocks.Outbox;

public sealed class OutboxMessage
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; init; }

    /// <summary>Event type name, matching the keys of EventBusSubscriptionInfo.EventTypes.</summary>
    public required string Type { get; init; }

    public required string Content { get; init; }

    public DateTime OccurredAt { get; init; }

    public DateTime? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public string? Error { get; set; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(200);
        builder.Property(m => m.Content).HasColumnType("jsonb");
        builder.HasIndex(m => new { m.ProcessedAt, m.OccurredAt });
    }
}
