using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AutoPartsERP.Api.Common;

/// <summary>
/// Per-user limits for the expensive endpoints (file exports, PDFs, imports, AI). The general per-IP limits and the login / refresh limits
/// live in nginx (nginx/nginx.conf): behind the proxy every request reaches the API from the same address, so the API partitions by the
/// signed-in user instead.
/// </summary>
public static class RateLimiting
{
    public const string Heavy = "heavy";

    public static IServiceCollection AddErpRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var perMinute = configuration.GetValue("RateLimits:HeavyPerMinute", 30);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Heavy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }
}
