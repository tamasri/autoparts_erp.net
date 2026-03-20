namespace AutoPartsERP.Infrastructure.Identity;

public static class IdentityConfiguration
{
    public static IServiceCollection AddErpIdentity(this IServiceCollection services)
    {
        services
            .AddIdentity<AppUser, AppRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<Persistence.AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}