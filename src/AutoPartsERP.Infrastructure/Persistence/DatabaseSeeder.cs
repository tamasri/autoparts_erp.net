namespace AutoPartsERP.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        await SeedRolesAsync(roleManager);
        await SeedAdminUserAsync(userManager, configuration, environment);
    }

    private static async Task SeedRolesAsync(RoleManager<AppRole> roleManager)
    {
        foreach (var roleCode in RoleCodes.All)
        {
            var role = await roleManager.FindByNameAsync(roleCode);
            if (role is null)
            {
                role = new AppRole
                {
                    Id = Guid.NewGuid(),
                    Name = roleCode,
                    NormalizedName = roleCode.ToUpperInvariant(),
                    Description = roleCode,
                    IsSystem = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = Guid.Empty
                };

                var created = await roleManager.CreateAsync(role);
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Seed failed creating role '{roleCode}': {string.Join(", ", created.Errors.Select(e => e.Description))}");
                }
            }

            await SyncRolePermissionsAsync(roleManager, role, roleCode);
        }
    }

    private static async Task SyncRolePermissionsAsync(RoleManager<AppRole> roleManager, AppRole role, string roleCode)
    {
        if (!RolePermissionMap.Bundles.TryGetValue(roleCode, out var desiredPermissions))
        {
            return;
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = existingClaims
            .Where(c => c.Type == "permission")
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in desiredPermissions)
        {
            if (existingPermissions.Contains(permission))
            {
                continue;
            }

            var added = await roleManager.AddClaimAsync(role, new Claim("permission", permission));
            if (!added.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Seed failed granting '{permission}' to role '{roleCode}': {string.Join(", ", added.Errors.Select(e => e.Description))}");
            }
        }
    }

    private static async Task SeedAdminUserAsync(UserManager<AppUser> userManager, IConfiguration configuration, IHostEnvironment environment)
    {
        var adminEmail = configuration["Seed:AdminEmail"] ?? "admin@autoparts.local";
        var adminUsername = configuration["Seed:AdminUsername"] ?? "admin";
        // Only create the admin once; do NOT delete/recreate on every restart, or an operator-changed
        // production password would be silently reset back to the seed value on the next deploy.
        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing is not null)
        {
            return;
        }

        // No built-in default in any environment: a known password on a reachable dev box is as bad as in production.
        // scripts/init-dev-settings.ps1 writes a random one into the git-ignored appsettings.Development.json.
        var adminPassword = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                $"Seed:AdminPassword must be configured to create the first admin ({environment.EnvironmentName}): set Seed__AdminPassword, or run scripts/init-dev-settings.ps1 on a dev machine.");
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
            throw new InvalidOperationException(
                $"Seed failed creating admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, RoleCodes.SystemAdministrator);
    }
}
