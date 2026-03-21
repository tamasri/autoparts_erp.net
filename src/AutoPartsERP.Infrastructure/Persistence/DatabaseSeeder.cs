namespace AutoPartsERP.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        const string adminEmail = "admin@autoparts.local";
        const string adminPassword = "Admin@123456";
        const string adminUsername = "admin";

        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing is not null)
        {
            return;
        }

        var user = new AppUser
        {
            UserName = adminUsername,
            Email = adminEmail,
            FullName = "System Administrator",
            CreatedAt = DateTimeOffset.UtcNow,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, adminPassword);
        if (!result.Succeeded)
        {
            throw new Exception($"Seed failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, "SUPER_ADMIN");
        await userManager.AddToRoleAsync(user, "ADMIN");
    }
}
