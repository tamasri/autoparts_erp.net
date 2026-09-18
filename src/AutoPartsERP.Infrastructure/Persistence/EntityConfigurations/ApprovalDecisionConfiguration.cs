namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable("approval_decisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Comment).HasColumnType("text");
        builder.Property(x => x.ReviewedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}
