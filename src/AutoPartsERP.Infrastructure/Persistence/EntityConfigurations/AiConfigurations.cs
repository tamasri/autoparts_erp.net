namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class AiFeatureFlagConfiguration : IEntityTypeConfiguration<AiFeatureFlag>
{
    public void Configure(EntityTypeBuilder<AiFeatureFlag> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ModelName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RequiredPermission).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AllowedRoles).HasColumnType("text[]");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.FeatureCode).IsUnique();
    }
}

public sealed class AiSessionConfiguration : IEntityTypeConfiguration<AiSession>
{
    public void Configure(EntityTypeBuilder<AiSession> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250);
        builder.Property(x => x.ContextJson).HasColumnType("jsonb");
        builder.Property(x => x.LastInteractionAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.UserId, x.FeatureCode });
    }
}

public sealed class AiPromptLogConfiguration : IEntityTypeConfiguration<AiPromptLog>
{
    public void Configure(EntityTypeBuilder<AiPromptLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ModelName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PromptText).IsRequired();
        builder.Property(x => x.ResponseText).IsRequired();
        builder.Property(x => x.ToolCallsJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.FeatureCode, x.CreatedAt });
    }
}

public sealed class AiSuggestionConfiguration : IEntityTypeConfiguration<AiSuggestion>
{
    public void Configure(EntityTypeBuilder<AiSuggestion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.SuggestedActionCode).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ReviewedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.UserId, x.Status });
    }
}

public sealed class AiTaskRunConfiguration : IEntityTypeConfiguration<AiTaskRun>
{
    public void Configure(EntityTypeBuilder<AiTaskRun> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TaskCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.TaskCode, x.StartedAt });
    }
}

public sealed class AiFeedbackConfiguration : IEntityTypeConfiguration<AiFeedback>
{
    public void Configure(EntityTypeBuilder<AiFeedback> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Correction).HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.PromptLogId);
    }
}

public sealed class AiDocumentConfiguration : IEntityTypeConfiguration<AiDocument>
{
    public void Configure(EntityTypeBuilder<AiDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourcePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentText).IsRequired();
        builder.Property(x => x.LanguageCode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.SourcePath).IsUnique();
    }
}

public sealed class AiScheduledTaskConfiguration : IEntityTypeConfiguration<AiScheduledTask>
{
    public void Configure(EntityTypeBuilder<AiScheduledTask> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TaskCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CronExpression).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LastRunAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.NextRunAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.TaskCode).IsUnique();
    }
}

