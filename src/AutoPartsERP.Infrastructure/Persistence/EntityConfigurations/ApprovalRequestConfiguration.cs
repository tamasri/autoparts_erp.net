namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_approval_requests_required_approvals", "required_approvals > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActionCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestedByUserId).IsRequired();
        builder.Property(x => x.RequiredApprovals).IsRequired();
        builder.Property(x => x.RequestedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasMany(x => x.Decisions)
            .WithOne()
            .HasForeignKey(x => x.ApprovalRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Decisions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
