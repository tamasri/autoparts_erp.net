namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PartNumber).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PartNumberCanonical).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PartNumberNumeric).HasMaxLength(120).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NameArColloquial).HasMaxLength(300);
        builder.Property(x => x.Brand).HasMaxLength(120);
        builder.Property(x => x.CategoryPath).HasColumnType("ltree");
        builder.Property(x => x.UnitOfMeasure).HasMaxLength(32);
        builder.Property(x => x.ReorderLevel).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.PartNumberCanonical).IsUnique();
        builder.HasIndex(x => x.PartNumberNumeric);
        builder.HasIndex(x => x.SkuId);
        builder.ToTable(t => t.HasCheckConstraint("ck_items_reorder_level_non_negative", "reorder_level >= 0"));
    }
}

public sealed class ItemAliasConfiguration : IEntityTypeConfiguration<ItemAlias>
{
    public void Configure(EntityTypeBuilder<ItemAlias> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Alias).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AliasCanonical).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.ItemId, x.AliasCanonical }).IsUnique();
        builder.HasOne<Item>()
            .WithMany(i => i.Aliases)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ItemInterchangeConfiguration : IEntityTypeConfiguration<ItemInterchange>
{
    public void Configure(EntityTypeBuilder<ItemInterchange> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Priority).HasDefaultValue(1);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.ItemId, x.InterchangeItemId }).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("ck_item_interchanges_not_self", "item_id <> interchange_item_id"));
    }
}

public sealed class ItemReorderSettingConfiguration : IEntityTypeConfiguration<ItemReorderSetting>
{
    public void Configure(EntityTypeBuilder<ItemReorderSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReorderPoint).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReorderQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.MaxStock).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique();
    }
}

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Qty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.ItemId, x.LocationId, x.BatchId, x.Status }).IsUnique();
        builder.HasIndex(x => new { x.ItemId, x.Status });
        builder.HasIndex(x => x.LocationId);
        builder.ToTable(t => t.HasCheckConstraint("ck_inventory_balances_qty_non_negative", "qty >= 0"));
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MovementType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Qty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Direction).HasMaxLength(8).IsRequired();
        builder.Property(x => x.FromStatus).HasMaxLength(32);
        builder.Property(x => x.ToStatus).HasMaxLength(32);
        builder.Property(x => x.ReferenceType).HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.ItemId, x.CreatedAt });
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
    }
}

public sealed class ReceivingDocumentConfiguration : IEntityTypeConfiguration<ReceivingDocument>
{
    public void Configure(EntityTypeBuilder<ReceivingDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentNo).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PurchaseOrderRef).HasMaxLength(128);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.PostedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.DocumentNo).IsUnique();
    }
}

public sealed class ReceivingLineConfiguration : IEntityTypeConfiguration<ReceivingLine>
{
    public void Configure(EntityTypeBuilder<ReceivingLine> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExpectedQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReceivedQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.RejectedQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ConditionStatus).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class PutawayTaskConfiguration : IEntityTypeConfiguration<PutawayTask>
{
    public void Configure(EntityTypeBuilder<PutawayTask> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Qty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.ConfirmedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class TransferOrderConfiguration : IEntityTypeConfiguration<TransferOrder>
{
    public void Configure(EntityTypeBuilder<TransferOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TransferNo).HasMaxLength(32).IsRequired();
        builder.Property(x => x.InternalTrackingNo).HasMaxLength(128);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ShippedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.TransferNo).IsUnique();
    }
}

public sealed class TransferOrderLineConfiguration : IEntityTypeConfiguration<TransferOrderLine>
{
    public void Configure(EntityTypeBuilder<TransferOrderLine> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ShippedQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReceivedQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class CycleCountPlanConfiguration : IEntityTypeConfiguration<CycleCountPlan>
{
    public void Configure(EntityTypeBuilder<CycleCountPlan> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeType).HasMaxLength(24).IsRequired();
        builder.Property(x => x.ScopeFilterJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PostedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class CycleCountLineConfiguration : IEntityTypeConfiguration<CycleCountLine>
{
    public void Configure(EntityTypeBuilder<CycleCountLine> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SystemQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CountedQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.VarianceQty).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReasonCode).HasMaxLength(64);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AdjustmentNo).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AdjustmentType).HasMaxLength(24).IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PostedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.AdjustmentNo).IsUnique();
    }
}

public sealed class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.QtyDelta).HasColumnType("numeric(18,4)");
        builder.Property(x => x.SystemQtyBefore).HasColumnType("numeric(18,4)");
        builder.Property(x => x.SystemQtyAfter).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class InventoryAlertConfiguration : IEntityTypeConfiguration<InventoryAlert>
{
    public void Configure(EntityTypeBuilder<InventoryAlert> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AlertType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ThresholdValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CurrentValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.AcknowledgedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ResolvedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

public sealed class BarcodeScanLogConfiguration : IEntityTypeConfiguration<BarcodeScanLog>
{
    public void Configure(EntityTypeBuilder<BarcodeScanLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScanCode).HasMaxLength(240).IsRequired();
        builder.Property(x => x.ScanType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(120);
        builder.Property(x => x.ScannedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.ScanCode, x.ScannedAt });
    }
}
