var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

var databaseConnectionString = builder.Configuration["Database:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=autoparts_erp;Username=postgres;Password=postgres";

var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";

// EF + Identity
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));

builder.Services.AddErpIdentity();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);

// MediatR + pipeline behaviors (ordered)
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly);
});
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PeriodLockBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MakerCheckerBehavior<,>));

// FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(LoginCommand).Assembly, includeInternalTypes: true);

// Mapster
var mapsterConfig = TypeAdapterConfig.GlobalSettings;
mapsterConfig.Scan(typeof(GovernanceMappings).Assembly);
builder.Services.AddSingleton(mapsterConfig);
builder.Services.AddScoped<MapsterMapper.IMapper, ServiceMapper>();

// JWT RS256
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "username",
            RoleClaimType = ClaimTypes.Role
        };

        if (!string.IsNullOrWhiteSpace(jwtSettings.PublicKeyPemBase64))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(jwtSettings.PublicKeyPemBase64)));
            options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(rsa);
        }
    });

// Authorization
builder.Services.AddAuthorization();

// ProblemDetails
Microsoft.Extensions.DependencyInjection.ProblemDetailsServiceCollectionExtensions.AddProblemDetails(builder.Services);

// Carter
builder.Services.AddCarter();

// Hangfire
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHangfire(configuration => configuration
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(databaseConnectionString)));
    builder.Services.AddHangfireServer();
}

// HealthChecks
builder.Services
    .AddHealthChecks()
    .AddNpgSql(databaseConnectionString, name: "postgresql")
    .AddRedis(redisConnectionString, name: "redis");

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// ApiVersioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

// OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// IdempotentMinimalAPI
builder.Services.AddApiIdempotency();

// DI registrations
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
builder.Services.AddScoped<IManualAuditService, ManualAuditService>();
builder.Services.AddScoped<IIdempotencyService, DistributedIdempotencyService>();
builder.Services.AddScoped<IPeriodLockService, PeriodLockService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IGovernanceService, GovernanceService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddScoped<ExpiredApprovalJob>();
builder.Services.AddScoped<IdempotencyCleanupJob>();
builder.Services.AddSingleton<HangfireAuthorizationFilter>();

// Audit configuration
AuditConfiguration.Configure(builder.Configuration);

var app = builder.Build();

// middleware pipeline
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseCors("DefaultCors");
app.UseAuthentication();
app.UseAuthorization();
app.UseApiIdempotency();

// health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

if (!app.Environment.IsEnvironment("Testing"))
{
    // Hangfire dashboard
    var hangfireAuthorizationFilter = app.Services.GetRequiredService<HangfireAuthorizationFilter>();
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new IDashboardAuthorizationFilter[] { hangfireAuthorizationFilter }
    });
}

// Scalar + OpenAPI
app.MapOpenApi();
app.MapScalarApiReference();

// Carter modules
app.MapCarter();

if (!app.Environment.IsEnvironment("Testing"))
{
    // recurring jobs
    RecurringJob.AddOrUpdate<ExpiredApprovalJob>(
        "governance-expired-approval-job",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        Cron.Hourly);

    RecurringJob.AddOrUpdate<IdempotencyCleanupJob>(
        "governance-idempotency-cleanup-job",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        Cron.Daily);
}

// auto-migrate
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

app.Run();

public partial class Program;
