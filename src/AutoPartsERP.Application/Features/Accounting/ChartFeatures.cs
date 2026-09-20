using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

// The chart of accounts lives in ERPNext. These requests read it (optionally with balances from the ledger), keep it (create, rename,
// retype, disable) and load it in bulk from a file. Every change goes through the audit log like any other action.

public sealed record GetChartOfAccountsQuery(bool IncludeBalances) : IRequest<Result<IReadOnlyList<AccountNodeDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetChartOfAccountsQueryHandler : IRequestHandler<GetChartOfAccountsQuery, Result<IReadOnlyList<AccountNodeDto>>>
{
    private readonly IErpNextClient _erpNext;

    public GetChartOfAccountsQueryHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public async Task<Result<IReadOnlyList<AccountNodeDto>>> Handle(GetChartOfAccountsQuery request, CancellationToken cancellationToken)
    {
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure)
        {
            return Result<IReadOnlyList<AccountNodeDto>>.Failure(chart.Error);
        }

        var tree = ChartTree.Build(chart.Value!);
        IReadOnlyDictionary<string, decimal>? balances = null;
        if (request.IncludeBalances)
        {
            var gl = await _erpNext.GetGlBalancesAsync(null, null, cancellationToken);
            if (gl.IsFailure)
            {
                return Result<IReadOnlyList<AccountNodeDto>>.Failure(gl.Error);
            }

            balances = FinancialReports.NaturalBalances(tree, gl.Value!);
        }

        return Result<IReadOnlyList<AccountNodeDto>>.Success(tree.Ordered
            .Select(n => new AccountNodeDto(n.Account.Name, n.Account.AccountName, n.Account.ParentAccount, n.Account.IsGroup, n.Account.RootType,
                n.Account.AccountType, n.Account.Currency, balances?.GetValueOrDefault(n.Account.Name)))
            .ToList());
    }
}

public sealed record GetAccountMappingQuery : IRequest<Result<IReadOnlyList<ErpNextAccountMapping>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetAccountMappingQueryHandler : IRequestHandler<GetAccountMappingQuery, Result<IReadOnlyList<ErpNextAccountMapping>>>
{
    private readonly IErpNextClient _erpNext;

    public GetAccountMappingQueryHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public Task<Result<IReadOnlyList<ErpNextAccountMapping>>> Handle(GetAccountMappingQuery request, CancellationToken cancellationToken) =>
        _erpNext.GetAccountMappingAsync(cancellationToken);
}

// ------------------------------------------------------------------ create / update

public sealed record CreateAccountCommand(CreateAccountRequest Request) : IRequest<Result<string>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.ManageAccounts;
    public string AuditModule => "ACCOUNTING";
}

public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.Request.AccountName).NotEmpty().MaximumLength(140);
        RuleFor(x => x.Request.ParentAccount).NotEmpty();
        RuleFor(x => x.Request.AccountType).Must(t => t is null || ErpNextAccountTypes.All.Contains(t)).WithMessage("Unknown account type.");
        RuleFor(x => x.Request.AccountNumber).MaximumLength(20);
    }
}

public sealed class CreateAccountCommandHandler : IRequestHandler<CreateAccountCommand, Result<string>>
{
    private readonly IErpNextClient _erpNext;

    public CreateAccountCommandHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public async Task<Result<string>> Handle(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure)
        {
            return Result<string>.Failure(chart.Error);
        }

        var parent = chart.Value!.FirstOrDefault(a => a.Name == request.ParentAccount);
        if (parent is null || !parent.IsGroup)
        {
            return Result<string>.Failure(new Error("Accounting.ParentNotGroup", "The parent must be an existing group account."));
        }

        if (chart.Value!.Any(a => a.ParentAccount == parent.Name && string.Equals(a.AccountName, request.AccountName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result<string>.Failure(new Error("Accounting.AccountConflict", "An account with this name already exists under the same parent."));
        }

        return await _erpNext.CreateAccountAsync(
            new ErpNextAccountCreate(request.AccountName.Trim(), parent.Name, request.IsGroup, request.AccountType, request.AccountNumber?.Trim()), cancellationToken);
    }
}

public sealed record UpdateAccountCommand(string Name, UpdateAccountRequest Request) : IRequest<Result<string>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.ManageAccounts;
    public string AuditModule => "ACCOUNTING";
}

public sealed class UpdateAccountCommandValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Request).Must(r => r.AccountName is not null || r.AccountType is not null || r.Disabled is not null).WithMessage("Nothing to change.");
        RuleFor(x => x.Request.AccountName).MaximumLength(140).When(x => x.Request.AccountName is not null);
        RuleFor(x => x.Request.AccountType).Must(t => t is null || ErpNextAccountTypes.All.Contains(t)).WithMessage("Unknown account type.");
    }
}

public sealed class UpdateAccountCommandHandler : IRequestHandler<UpdateAccountCommand, Result<string>>
{
    private readonly IErpNextClient _erpNext;

    public UpdateAccountCommandHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public Task<Result<string>> Handle(UpdateAccountCommand command, CancellationToken cancellationToken) =>
        _erpNext.UpdateAccountAsync(command.Name, new ErpNextAccountUpdate(command.Request.AccountName, command.Request.AccountType, command.Request.Disabled), cancellationToken);
}

// ------------------------------------------------------------------ import

/// <summary>Bulk load of accounts. A dry run reports what would happen row by row; a real run creates the missing ones and skips those that already exist.</summary>
public sealed record ImportAccountsCommand(IReadOnlyList<ImportAccountRow> Rows, bool DryRun) : IRequest<Result<ImportAccountsResult>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.ManageAccounts;
    public string AuditModule => "ACCOUNTING";
}

public sealed class ImportAccountsCommandValidator : AbstractValidator<ImportAccountsCommand>
{
    public const int MaxRows = 1000;

    public ImportAccountsCommandValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().WithMessage("The file has no rows.");
        RuleFor(x => x.Rows.Count).LessThanOrEqualTo(MaxRows).WithMessage($"At most {MaxRows} rows per file.");
    }
}

public sealed class ImportAccountsCommandHandler : IRequestHandler<ImportAccountsCommand, Result<ImportAccountsResult>>
{
    private readonly IErpNextClient _erpNext;

    public ImportAccountsCommandHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public async Task<Result<ImportAccountsResult>> Handle(ImportAccountsCommand command, CancellationToken cancellationToken)
    {
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure)
        {
            return Result<ImportAccountsResult>.Failure(chart.Error);
        }

        var byName = chart.Value!.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var byAccountName = chart.Value!.GroupBy(a => a.AccountName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var results = new List<ImportAccountRowResult>();
        int created = 0, existing = 0, failed = 0;

        ErpNextAccount? FindParent(string text)
        {
            if (byName.TryGetValue(text, out var exact)) return exact;
            return byAccountName.TryGetValue(text, out var matches) && matches.Count == 1 ? matches[0] : null;
        }

        void Register(ErpNextAccount account)
        {
            byName[account.Name] = account;
            if (!byAccountName.TryGetValue(account.AccountName, out var list)) byAccountName[account.AccountName] = list = [];
            list.Add(account);
        }

        foreach (var row in command.Rows)
        {
            var name = row.AccountName?.Trim() ?? string.Empty;
            string? problem = row.Error;
            ErpNextAccount? parent = null;

            if (problem is null && name.Length is < 2 or > 140) problem = "اسم الحساب مطلوب (2 إلى 140 حرفاً).";
            if (problem is null && string.IsNullOrWhiteSpace(row.ParentAccount)) problem = "الحساب الأب مطلوب.";
            if (problem is null)
            {
                parent = FindParent(row.ParentAccount!.Trim());
                if (parent is null) problem = $"الحساب الأب '{row.ParentAccount}' غير موجود أو غير محدد (ضعه قبل هذا السطر).";
                else if (!parent.IsGroup) problem = $"'{parent.AccountName}' ليس مجموعة فلا يقبل حسابات تحته.";
            }

            var isGroup = row.IsGroup ?? false;
            if (problem is null && !isGroup && row.AccountType is not null && !ErpNextAccountTypes.All.Contains(row.AccountType)) problem = $"نوع الحساب غير معروف: {row.AccountType}.";

            if (problem is not null)
            {
                failed++;
                results.Add(new ImportAccountRowResult(row.RowNumber, name, "ERROR", problem));
                continue;
            }

            if (byName.Values.Any(a => a.ParentAccount == parent!.Name && string.Equals(a.AccountName, name, StringComparison.OrdinalIgnoreCase)))
            {
                existing++;
                results.Add(new ImportAccountRowResult(row.RowNumber, name, "EXISTS", null));
                continue;
            }

            if (command.DryRun)
            {
                Register(new ErpNextAccount($"{name} (new)", name, parent!.Name, isGroup, parent.RootType, row.AccountType, null));
                created++;
                results.Add(new ImportAccountRowResult(row.RowNumber, name, "CREATE", null));
                continue;
            }

            var made = await _erpNext.CreateAccountAsync(new ErpNextAccountCreate(name, parent!.Name, isGroup, isGroup ? null : row.AccountType, row.AccountNumber), cancellationToken);
            if (made.IsFailure)
            {
                failed++;
                results.Add(new ImportAccountRowResult(row.RowNumber, name, "ERROR", made.Error.Message));
                continue;
            }

            Register(new ErpNextAccount(made.Value!, name, parent.Name, isGroup, parent.RootType, row.AccountType, null));
            created++;
            results.Add(new ImportAccountRowResult(row.RowNumber, name, "CREATED", null));
        }

        return Result<ImportAccountsResult>.Success(new ImportAccountsResult(command.DryRun, command.Rows.Count, created, existing, failed, results));
    }
}
