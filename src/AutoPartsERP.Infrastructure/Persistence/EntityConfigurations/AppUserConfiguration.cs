using AutoPartsERP.Infrastructure.Identity;

namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
    }
}