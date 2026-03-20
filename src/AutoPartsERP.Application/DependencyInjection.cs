using System.Reflection;
using AutoPartsERP.Application.Common.Behaviors;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsERP.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationAssembly).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
            cfg.AddOpenBehavior(typeof(PeriodLockBehavior<,>));
            cfg.AddOpenBehavior(typeof(MakerCheckerBehavior<,>));
        });

        services.AddApplicationValidators(typeof(ApplicationAssembly).Assembly);

        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(typeof(ApplicationAssembly).Assembly);
        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }

    private static IServiceCollection AddApplicationValidators(this IServiceCollection services, Assembly assembly)
    {
        var validatorTypes = assembly
            .DefinedTypes
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .SelectMany(type => type.GetInterfaces(), (type, contract) => new { type, contract })
            .Where(pair => pair.contract.IsGenericType && pair.contract.GetGenericTypeDefinition() == typeof(IValidator<>));

        foreach (var validatorType in validatorTypes)
        {
            services.AddTransient(validatorType.contract, validatorType.type);
        }

        return services;
    }
}
