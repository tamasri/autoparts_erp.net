namespace AutoPartsERP.Domain.Constants;

public static class PermissionCodes
{
    // Phase 1 compatibility constants
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string UsersManageRoles = "users.manage-roles";
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string ApprovalsRead = "approvals.read";
    public const string ApprovalsWrite = "approvals.write";
    public const string ApprovalsReview = "approvals.review";
    public const string PeriodLocksRead = "period-locks.read";
    public const string PeriodLocksWrite = "period-locks.write";
    public const string ReasonCodesRead = "reason-codes.read";
    public const string ReasonCodesWrite = "reason-codes.write";
    public const string AuditRead = "audit.read";
    public const string AuthManage = "auth.manage";

    public static class Customers
    {
        public const string Read = "customers:read";
        public const string Create = "customers:create";
        public const string Update = "customers:update";
        public const string Deactivate = "customers:deactivate";
    }

    public static class Catalog
    {
        public const string Read = "catalog:read";
        public const string Write = "catalog:write";
        public const string Delete = "catalog:delete";
    }

    public static class Inventory
    {
        public const string Read = "inventory:read";
        public const string Write = "inventory:write";
        public const string Adjust = "inventory:adjust";
        public const string Transfer = "inventory:transfer";
        public const string ViewBatches = "inventory:view_batches";
        public const string ManageBatches = "inventory:manage_batches";
    }

    public static class Invoices
    {
        public const string Read = "invoices:read";
        public const string Create = "invoices:create";
        public const string Update = "invoices:update";
        public const string Post = "invoices:post";
        public const string Void = "invoices:void";
        public const string PriceOverride = "invoices:price_override";
        public const string DeliveryFee = "invoices:delivery_fee";
    }

    public static class Payments
    {
        public const string Read = "payments:read";
        public const string Create = "payments:create";
        public const string Allocate = "payments:allocate";
        public const string WriteOff = "payments:write_off";
    }

    public static class FxRates
    {
        public const string Read = "fx_rates:read";
        public const string Manage = "fx_rates:manage";
    }

    public static class Warranty
    {
        public const string Read = "warranty:read";
        public const string Create = "warranty:create";
        public const string Process = "warranty:process";
        public const string Reject = "warranty:reject";
    }

    public static class Reports
    {
        public const string ProfitLoss = "reports:profit_loss";
        public const string AccountStatement = "reports:account_statement";
        public const string InventoryValue = "reports:inventory_value";
        public const string BatchTrace = "reports:batch_trace";
    }

    public static class Party
    {
        public const string Read = "party:read";
        public const string Create = "party:create";
        public const string Update = "party:update";
        public const string Deactivate = "party:deactivate";
        public const string AssignType = "party:assign_type";
    }

    public static class Items
    {
        public const string Read = "items:read";
        public const string Create = "items:create";
        public const string Update = "items:update";
        public const string StopShip = "items:stop_ship";
        public const string ManageAliases = "items:aliases";
        public const string ManageInterchanges = "items:interchanges";
    }

    public static class Receiving
    {
        public const string Read = "receiving:read";
        public const string Create = "receiving:create";
        public const string Post = "receiving:post";
        public const string Putaway = "receiving:putaway";
    }

    public static class IssueOrders
    {
        public const string Read = "issue_orders:read";
        public const string Create = "issue_orders:create";
        public const string Pick = "issue_orders:pick";
        public const string Verify = "issue_orders:verify";
        public const string Issue = "issue_orders:issue";
    }

    public static class Transfers
    {
        public const string Read = "transfers:read";
        public const string CreateRequest = "transfers:create_request";
        public const string ApproveRequest = "transfers:approve_request";
        public const string CreateOrder = "transfers:create_order";
        public const string Ship = "transfers:ship";
        public const string Receive = "transfers:receive";
    }

    public static class CycleCounts
    {
        public const string Read = "cycle_counts:read";
        public const string Create = "cycle_counts:create";
        public const string Record = "cycle_counts:record";
        public const string ApproveVariance = "cycle_counts:approve_variance";
    }

    public static class StockAdjustments
    {
        public const string Read = "stock_adjustments:read";
        public const string Create = "stock_adjustments:create";
        public const string Post = "stock_adjustments:post";
    }

    public static class InventoryAlerts
    {
        public const string Read = "inventory_alerts:read";
        public const string Acknowledge = "inventory_alerts:acknowledge";
        public const string Resolve = "inventory_alerts:resolve";
    }

    public static class Barcodes
    {
        public const string Scan = "barcodes:scan";
        public const string Generate = "barcodes:generate";
    }

    public static class Ai
    {
        public const string Chat = "ai:chat";
        public const string SuggestionsRead = "ai:suggestions:read";
        public const string SuggestionsReview = "ai:suggestions:review";
        public const string FeedbackCreate = "ai:feedback:create";
        public const string PromptLogsRead = "ai:prompt_logs:read";
        public const string TaskRunsRead = "ai:task_runs:read";
        public const string KnowledgeIndex = "ai:knowledge:index";
        public const string FeatureFlagsRead = "ai:feature_flags:read";
        public const string FeatureFlagsWrite = "ai:feature_flags:write";
        public const string ScheduledTasksRead = "ai:scheduled_tasks:read";
        public const string ScheduledTasksWrite = "ai:scheduled_tasks:write";
    }

    public static class System
    {
        public const string ConfigRead = "system:config_read";
        public const string ConfigWrite = "system:config_write";
    }

    public static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UsersRead,
        UsersWrite,
        UsersManageRoles,
        RolesRead,
        RolesWrite,
        ApprovalsRead,
        ApprovalsWrite,
        ApprovalsReview,
        PeriodLocksRead,
        PeriodLocksWrite,
        ReasonCodesRead,
        ReasonCodesWrite,
        AuditRead,
        AuthManage,
        Customers.Read,
        Customers.Create,
        Customers.Update,
        Customers.Deactivate,
        Catalog.Read,
        Catalog.Write,
        Catalog.Delete,
        Inventory.Read,
        Inventory.Write,
        Inventory.Adjust,
        Inventory.Transfer,
        Inventory.ViewBatches,
        Inventory.ManageBatches,
        Invoices.Read,
        Invoices.Create,
        Invoices.Update,
        Invoices.Post,
        Invoices.Void,
        Invoices.PriceOverride,
        Invoices.DeliveryFee,
        Payments.Read,
        Payments.Create,
        Payments.Allocate,
        Payments.WriteOff,
        FxRates.Read,
        FxRates.Manage,
        Warranty.Read,
        Warranty.Create,
        Warranty.Process,
        Warranty.Reject,
        Reports.ProfitLoss,
        Reports.AccountStatement,
        Reports.InventoryValue,
        Reports.BatchTrace,
        Party.Read,
        Party.Create,
        Party.Update,
        Party.Deactivate,
        Party.AssignType,
        Items.Read,
        Items.Create,
        Items.Update,
        Items.StopShip,
        Items.ManageAliases,
        Items.ManageInterchanges,
        Receiving.Read,
        Receiving.Create,
        Receiving.Post,
        Receiving.Putaway,
        IssueOrders.Read,
        IssueOrders.Create,
        IssueOrders.Pick,
        IssueOrders.Verify,
        IssueOrders.Issue,
        Transfers.Read,
        Transfers.CreateRequest,
        Transfers.ApproveRequest,
        Transfers.CreateOrder,
        Transfers.Ship,
        Transfers.Receive,
        CycleCounts.Read,
        CycleCounts.Create,
        CycleCounts.Record,
        CycleCounts.ApproveVariance,
        StockAdjustments.Read,
        StockAdjustments.Create,
        StockAdjustments.Post,
        InventoryAlerts.Read,
        InventoryAlerts.Acknowledge,
        InventoryAlerts.Resolve,
        Barcodes.Scan,
        Barcodes.Generate,
        Ai.Chat,
        Ai.SuggestionsRead,
        Ai.SuggestionsReview,
        Ai.FeedbackCreate,
        Ai.PromptLogsRead,
        Ai.TaskRunsRead,
        Ai.KnowledgeIndex,
        Ai.FeatureFlagsRead,
        Ai.FeatureFlagsWrite,
        Ai.ScheduledTasksRead,
        Ai.ScheduledTasksWrite,
        System.ConfigRead,
        System.ConfigWrite
    };
}
