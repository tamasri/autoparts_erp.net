using AutoPartsERP.Infrastructure.Identity;

namespace AutoPartsERP.Infrastructure.Persistence;

[AuditDbContext(Mode = AuditOptionMode.OptOut)]
public sealed class AppDbContext : IdentityDbContext<AppUser, AppRole, Guid,
    IdentityUserClaim<Guid>, IdentityUserRole<Guid>, IdentityUserLogin<Guid>,
    IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<PeriodLock> PeriodLocks => Set<PeriodLock>();

    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<FxRate> FxRates => Set<FxRate>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<AttributeSchema> AttributeSchemas => Set<AttributeSchema>();

    public DbSet<Sku> Skus => Set<Sku>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<InventoryStock> InventoryStocks => Set<InventoryStock>();

    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<BatchMovement> BatchMovements => Set<BatchMovement>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<WarrantyRecord> WarrantyRecords => Set<WarrantyRecord>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<PartyTypeAssignment> PartyTypeAssignments => Set<PartyTypeAssignment>();

    public DbSet<PartyContact> PartyContacts => Set<PartyContact>();

    public DbSet<PartyAddress> PartyAddresses => Set<PartyAddress>();

    public DbSet<PartyNote> PartyNotes => Set<PartyNote>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<KpiDefinition> KpiDefinitions => Set<KpiDefinition>();

    public DbSet<KpiThreshold> KpiThresholds => Set<KpiThreshold>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<ItemAlias> ItemAliases => Set<ItemAlias>();

    public DbSet<ItemInterchange> ItemInterchanges => Set<ItemInterchange>();

    public DbSet<ItemReorderSetting> ItemReorderSettings => Set<ItemReorderSetting>();

    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<ReceivingDocument> ReceivingDocuments => Set<ReceivingDocument>();

    public DbSet<ReceivingLine> ReceivingLines => Set<ReceivingLine>();

    public DbSet<PutawayTask> PutawayTasks => Set<PutawayTask>();

    public DbSet<TransferOrder> TransferOrders => Set<TransferOrder>();

    public DbSet<TransferOrderLine> TransferOrderLines => Set<TransferOrderLine>();

    public DbSet<CycleCountPlan> CycleCountPlans => Set<CycleCountPlan>();

    public DbSet<CycleCountLine> CycleCountLines => Set<CycleCountLine>();

    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();

    public DbSet<InventoryAlert> InventoryAlerts => Set<InventoryAlert>();

    public DbSet<BarcodeScanLog> BarcodeScanLogs => Set<BarcodeScanLog>();

    public DbSet<AiFeatureFlag> AiFeatureFlags => Set<AiFeatureFlag>();

    public DbSet<AiSession> AiSessions => Set<AiSession>();

    public DbSet<AiPromptLog> AiPromptLogs => Set<AiPromptLog>();

    public DbSet<AiSuggestion> AiSuggestions => Set<AiSuggestion>();

    public DbSet<AiTaskRun> AiTaskRuns => Set<AiTaskRun>();

    public DbSet<AiFeedback> AiFeedback => Set<AiFeedback>();

    public DbSet<AiDocument> AiDocuments => Set<AiDocument>();

    public DbSet<AiScheduledTask> AiScheduledTasks => Set<AiScheduledTask>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                entity.SetTableName(tableName.ToSnakeCase());
            }

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()));
                if (!string.IsNullOrWhiteSpace(column))
                {
                    property.SetColumnName(column.ToSnakeCase());
                }
            }

            foreach (var key in entity.GetKeys())
            {
                var keyName = key.GetName();
                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    key.SetName(keyName.ToSnakeCase());
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var fkName = foreignKey.GetConstraintName();
                if (!string.IsNullOrWhiteSpace(fkName))
                {
                    foreignKey.SetConstraintName(fkName.ToSnakeCase());
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (!string.IsNullOrWhiteSpace(indexName))
                {
                    index.SetDatabaseName(indexName.ToSnakeCase());
                }
            }
        }

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
