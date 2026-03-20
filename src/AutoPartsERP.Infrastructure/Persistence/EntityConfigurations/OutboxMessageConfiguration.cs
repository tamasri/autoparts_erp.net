namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ProcessedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ProcessingError);
        builder.Property(x => x.RetryCount).HasDefaultValue(0);

        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => new { x.AggregateType, x.AggregateId });
        builder.HasIndex(x => x.ProcessedAt)
            .HasFilter("processed_at IS NULL");
    }
}
