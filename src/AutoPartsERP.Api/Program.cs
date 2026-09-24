DapperTypeHandlers.Register();

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

// Local defaults exist only for Development and Testing. Anywhere else a missing setting is a deployment mistake and must stop the
// start-up, not silently connect to a guessed database.
var allowLocalDefaults = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

var databaseConnectionString = builder.Configuration["Database:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? (allowLocalDefaults
        ? "Host=localhost;Port=5432;Database=autoparts_erp;Username=postgres;Password=postgres"
        : throw new InvalidOperationException("Database:ConnectionString is not configured."));

var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("Redis")
    ?? (allowLocalDefaults ? "localhost:6379" : throw new InvalidOperationException("Redis:ConnectionString is not configured."));

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
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(WarehouseScopeBehavior<,>));
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
        // Keep claim names exactly as issued (sub, email, ...). The default inbound mapping renamed `sub` to
        // ClaimTypes.NameIdentifier, so ICurrentUser.UserId was always Guid.Empty: every created_by / audit /
        // maker-checker record was attributed to nobody and /auth/me could not find the user.
        options.MapInboundClaims = false;

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

        // Browsers cannot put an Authorization header on a WebSocket, so SignalR sends the access token in the query string.
        // Read it there for the hub path only; everywhere else the header stays the only source.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/hubs")
                    && context.Request.Query["access_token"].FirstOrDefault() is { Length: > 0 } token)
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
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
    // Every job class is tagged [Queue("governance")]; a server that only listens to "default"
    // (the AddHangfireServer() default) silently never runs any of them.
    builder.Services.AddHangfireServer(options => options.Queues = new[] { "default", "governance" });
}

// HealthChecks
builder.Services
    .AddHealthChecks()
    .AddNpgSql(databaseConnectionString, name: "postgresql")
    .AddRedis(redisConnectionString, name: "redis");

// OpenTelemetry + business metrics
builder.Services.AddObservability(builder.Configuration);

// CORS
var allowedOrigins = (builder.Configuration["AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            // Fail closed outside Development when AllowedOrigins is missing/misconfigured,
            // instead of silently falling back to AllowAnyOrigin().
            policy.WithOrigins(Array.Empty<string>());
        }
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
builder.Services.AddSignalR();

// IdempotentMinimalAPI
builder.Services.AddApiIdempotency();
builder.Services.AddErpRateLimits(builder.Configuration);

// DI registrations
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
builder.Services.AddScoped<IManualAuditService, ManualAuditService>();
builder.Services.AddScoped<IIdempotencyService, DistributedIdempotencyService>();
builder.Services.AddScoped<IPeriodLockService, PeriodLockService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<IApprovalReplayContext, ApprovalReplayContext>();
builder.Services.AddScoped<IWarehouseAccess, AutoPartsERP.Application.Features.Wms.WarehouseAccess>();
builder.Services.AddScoped<IWarehouseScope, AutoPartsERP.Application.Features.Wms.WarehouseScope>();
// ERPNext accounting hand-off. Every call site (InvoicePostedOutboxHandler, SyncCatalogToErpNextJob)
// depends only on IErpNextClient, so flipping Erpnext:Enabled is the only thing that changes
// behaviour - no call site needs to change.
builder.Services.AddScoped<SalesInvoiceErpNextSyncer>();
builder.Services.AddScoped<PaymentErpNextSyncer>();
builder.Services.AddScoped<PurchaseErpNextSyncer>();
builder.Services.AddScoped<StockAdjustmentErpNextSyncer>();
builder.Services.AddScoped<JournalEntryErpNextSyncer>();
builder.Services.AddScoped<LandedCostErpNextSyncer>();
builder.Services.Configure<ErpNextOptions>(builder.Configuration.GetSection(ErpNextOptions.SectionName));
var erpNextEnabled = builder.Configuration.GetValue<bool>($"{ErpNextOptions.SectionName}:Enabled");
if (erpNextEnabled)
{
    builder.Services.AddHttpClient<IErpNextClient, ErpNextClient>();
}
else
{
    builder.Services.AddScoped<IErpNextClient, NullErpNextClient>();
}
// WhatsApp assistant: intent from the language model (text only), everything else local; see AssistantModule.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient<IIntentExtractor, GroqIntentExtractor>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Assistant").Get<AutoPartsERP.Application.Features.Assistant.AssistantOptions>()
    ?? new AutoPartsERP.Application.Features.Assistant.AssistantOptions());
builder.Services.AddSingleton<AutoPartsERP.Application.Features.Assistant.IAssistantState, RedisAssistantState>();
builder.Services.AddScoped<IAssistantIdentity, AutoPartsERP.Infrastructure.Http.AssistantIdentity>();
builder.Services.AddScoped<AutoPartsERP.Application.Features.Assistant.AssistantAnswers>();
builder.Services.AddScoped<AutoPartsERP.Application.Features.Assistant.WhatsAppAssistant>();
// Real-time notices to browsers (SignalR groups chosen on the server; see ErpHub).
builder.Services.AddSingleton<IRealtimeNotifier, AutoPartsERP.Api.Hubs.HubRealtimeNotifier>();
builder.Services.AddScoped<ApprovalNotifications>();
builder.Services.AddScoped<StockAlertService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IGovernanceService, GovernanceService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddSingleton<IHumanizerService, HumanizerService>();
builder.Services.AddSingleton<IDocumentRenderer, AutoPartsERP.Infrastructure.Exports.DocumentRenderer>();
builder.Services.AddSingleton<IPartNumberService, PartNumberService>();
builder.Services.AddScoped<IItemSearchService, ItemSearchService>();
builder.Services.AddScoped<IAiService, AiService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IFxRateRepository, FxRateRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ISkuRepository, SkuRepository>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IWarrantyRepository, WarrantyRepository>();
builder.Services.AddScoped<IPartyRepository, PartyRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IItemRepository, ItemRepository>();
builder.Services.AddScoped<IInventoryBalanceRepository, InventoryBalanceRepository>();
builder.Services.AddScoped<IReceivingRepository, ReceivingRepository>();
builder.Services.AddScoped<ITransferOrderRepository, TransferOrderRepository>();
builder.Services.AddScoped<IStockAdjustmentRepository, StockAdjustmentRepository>();
builder.Services.AddScoped<IInventoryAlertRepository, InventoryAlertRepository>();
builder.Services.AddScoped<IAiRepository, AiRepository>();

builder.Services.AddSingleton<InventoryExcelExporter>();
builder.Services.AddSingleton<AccountStatementExcelExporter>();
builder.Services.AddSingleton<ProfitLossExcelExporter>();

builder.Services.AddScoped<ExpiredApprovalJob>();
builder.Services.AddScoped<IdempotencyCleanupJob>();
builder.Services.AddScoped<RefreshAccountSummaryJob>();
builder.Services.AddScoped<RefreshStockSummaryJob>();
builder.Services.AddScoped<RefreshMonthlyPlJob>();
builder.Services.AddScoped<SyncInventoryBalancesJob>();
builder.Services.AddScoped<SyncCatalogToErpNextJob>();
builder.Services.AddScoped<ExpireWarrantyRecordsJob>();
builder.Services.AddScoped<LowStockAlertJob>();
builder.Services.AddScoped<AccountingCheckJob>();
builder.Services.AddSingleton<HangfireAuthorizationFilter>();
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddScoped<IOutboxEventHandler, InvoicePostedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, PaymentAllocatedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, PaymentReversedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, InvoiceVoidedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, PurchaseInvoicePostedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, PurchaseInvoiceVoidedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, SupplierPaymentCreatedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, SupplierPaymentReversedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, StockAdjustmentPostedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, JournalEntryPostedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, JournalEntryVoidedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, LandedCostPostedOutboxHandler>();
builder.Services.AddScoped<IOutboxEventHandler, LandedCostVoidedOutboxHandler>();

// Audit configuration
AuditConfiguration.Configure(builder.Configuration);

var app = builder.Build();

// middleware pipeline
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
// API answers carry customer, price and ledger data: never keep them in a browser or proxy cache.
app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
    }

    return next(context);
});
app.UseExceptionHandler();
app.UseCors("DefaultCors");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseApiIdempotency();

// health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHub<ErpHub>("/hubs/erp");

if (!app.Environment.IsEnvironment("Testing"))
{
    // Hangfire dashboard
    var hangfireAuthorizationFilter = app.Services.GetRequiredService<HangfireAuthorizationFilter>();
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new IDashboardAuthorizationFilter[] { hangfireAuthorizationFilter }
    });
}

// Scalar + OpenAPI: a map of every endpoint is a development aid, not something to serve in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

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

    RecurringJob.AddOrUpdate<RefreshAccountSummaryJob>(
        "operational-refresh-account-summary",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        "*/15 * * * *");

    RecurringJob.AddOrUpdate<RefreshStockSummaryJob>(
        "operational-refresh-stock-summary",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        "*/10 * * * *");

    RecurringJob.AddOrUpdate<RefreshMonthlyPlJob>(
        "operational-refresh-monthly-pl",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        Cron.Daily(0, 5));

    RecurringJob.AddOrUpdate<SyncInventoryBalancesJob>(
        "operational-sync-inventory-balances",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        "*/5 * * * *");

    RecurringJob.AddOrUpdate<SyncCatalogToErpNextJob>(
        "operational-sync-catalog-erpnext",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        "*/30 * * * *");

    RecurringJob.AddOrUpdate<ExpireWarrantyRecordsJob>(
        "operational-expire-warranty-records",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        Cron.Daily(0, 10));

    RecurringJob.AddOrUpdate<LowStockAlertJob>(
        "operational-low-stock-alert",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        "*/10 * * * *");

    RecurringJob.AddOrUpdate<AccountingCheckJob>(
        "ai-accounting-check",
        "governance",
        job => job.RunAsync(CancellationToken.None),
        Cron.Daily(4));
}

// auto-migrate
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
    await DatabaseSeeder.SeedAsync(app.Services);
    await ReferenceDataSeeder.SeedAsync(app.Services);

    // One-off maintenance run (scripts/reset-business-data.sh): wipe trial data here and in ERPNext, then exit without serving.
    if (args.Contains("--reset-business-data"))
    {
        Environment.ExitCode = await AutoPartsERP.Api.Maintenance.ResetBusinessData.RunAsync(app.Services, args);
        return;
    }

    // Sample customers, items, stock and invoices are for trying the system out. Never in a real company's books (they would be
    // posted to ERPNext too): Development only, or when explicitly asked for with Seed:DemoData=true.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Seed:DemoData"))
    {
        await DemoDataSeeder.SeedAsync(app.Services);
    }
}

app.Run();

public partial class Program;
