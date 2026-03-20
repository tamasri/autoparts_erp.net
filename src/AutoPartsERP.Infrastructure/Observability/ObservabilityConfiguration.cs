using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AutoPartsERP.Infrastructure.Observability;

public static class ObservabilityConfiguration
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("AutoPartsERP.*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(
                        configuration["Observability:OtlpEndpoint"]
                        ?? "http://localhost:4317");
                }))
            .WithMetrics(metrics => metrics
                .AddMeter("AutoPartsERP")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddPrometheusExporter());

        services.AddSingleton<ErpMetrics>();
        return services;
    }
}
