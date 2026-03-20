using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class PartyRepository : IPartyRepository
{
    private readonly AppDbContext _dbContext;
    private readonly IDbConnectionFactory _connectionFactory;

    public PartyRepository(AppDbContext dbContext, IDbConnectionFactory connectionFactory)
    {
        _dbContext = dbContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Parties
            .Include(x => x.TypeAssignments)
            .Include(x => x.Contacts)
            .Include(x => x.Addresses)
            .Include(x => x.NotesEntries)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Party?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.Parties
            .Include(x => x.TypeAssignments)
            .FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.Parties.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public async Task<PagedResult<PartyListItemDto>> GetPagedAsync(PartyQueryRequest query, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var sql = """
            SELECT
                p.id AS Id,
                p.code AS Code,
                p.display_name AS DisplayName,
                p.display_name_ar AS DisplayNameAr,
                p.is_active AS IsActive,
                (
                    EXISTS (
                        SELECT 1 FROM party_type_assignments pa
                        WHERE pa.party_id = p.id
                          AND pa.type_code = 'CUSTOMER'
                          AND pa.is_active = TRUE
                    )
                    AND EXISTS (
                        SELECT 1 FROM party_type_assignments pa
                        WHERE pa.party_id = p.id
                          AND pa.type_code = 'VENDOR'
                          AND pa.is_active = TRUE
                    )
                ) AS HasCombinedStatement,
                COALESCE(array_agg(a.type_code) FILTER (WHERE a.type_code IS NOT NULL), ARRAY[]::text[]) AS ActiveTypeCodes,
                p.created_at AS CreatedAt
            FROM parties p
            LEFT JOIN party_type_assignments a
                ON a.party_id = p.id AND a.is_active = TRUE
            WHERE (@IsActive IS NULL OR p.is_active = @IsActive)
              AND (@SearchTerm IS NULL OR p.display_name ILIKE '%' || @SearchTerm || '%' OR p.display_name_ar ILIKE '%' || @SearchTerm || '%')
              AND (
                    @TypeCode IS NULL OR EXISTS (
                        SELECT 1
                        FROM party_type_assignments f
                        WHERE f.party_id = p.id
                          AND f.is_active = TRUE
                          AND f.type_code = @TypeCode
                    )
                  )
            GROUP BY p.id, p.code, p.display_name, p.display_name_ar, p.is_active, p.created_at
            HAVING (
                @HasCombinedStatement IS NULL
                OR @HasCombinedStatement = (
                    EXISTS (
                        SELECT 1 FROM party_type_assignments pa
                        WHERE pa.party_id = p.id
                          AND pa.type_code = 'CUSTOMER'
                          AND pa.is_active = TRUE
                    )
                    AND EXISTS (
                        SELECT 1 FROM party_type_assignments pa
                        WHERE pa.party_id = p.id
                          AND pa.type_code = 'VENDOR'
                          AND pa.is_active = TRUE
                    )
                )
            )
            ORDER BY p.created_at DESC
            OFFSET @Offset
            LIMIT @Limit;
            """;

        var countSql = """
            SELECT COUNT(1)
            FROM parties p
            WHERE (@IsActive IS NULL OR p.is_active = @IsActive)
              AND (@SearchTerm IS NULL OR p.display_name ILIKE '%' || @SearchTerm || '%' OR p.display_name_ar ILIKE '%' || @SearchTerm || '%')
              AND (
                    @TypeCode IS NULL OR EXISTS (
                        SELECT 1
                        FROM party_type_assignments f
                        WHERE f.party_id = p.id
                          AND f.is_active = TRUE
                          AND f.type_code = @TypeCode
                    )
                  )
              AND (
                    @HasCombinedStatement IS NULL
                    OR @HasCombinedStatement = (
                        EXISTS (
                            SELECT 1 FROM party_type_assignments pa
                            WHERE pa.party_id = p.id
                              AND pa.type_code = 'CUSTOMER'
                              AND pa.is_active = TRUE
                        )
                        AND EXISTS (
                            SELECT 1 FROM party_type_assignments pa
                            WHERE pa.party_id = p.id
                              AND pa.type_code = 'VENDOR'
                              AND pa.is_active = TRUE
                        )
                    )
                  );
            """;

        var page = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        var parameters = new
        {
            IsActive = query.IsActive,
            SearchTerm = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim(),
            TypeCode = string.IsNullOrWhiteSpace(query.TypeCode) ? null : query.TypeCode.Trim().ToUpperInvariant(),
            HasCombinedStatement = query.HasCombinedStatement,
            Offset = (page - 1) * pageSize,
            Limit = pageSize
        };

        var rows = await connection.QueryAsync<PartyListItemRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        var items = rows.Select(r => new PartyListItemDto(
            r.Id,
            r.Code,
            r.DisplayName,
            r.DisplayNameAr,
            r.IsActive,
            r.HasCombinedStatement,
            r.ActiveTypeCodes,
            r.CreatedAt)).ToArray();

        return new PagedResult<PartyListItemDto>(items, page, pageSize, total);
    }

    public async Task AddAsync(Party party, CancellationToken cancellationToken = default)
    {
        await _dbContext.Parties.AddAsync(party, cancellationToken);
    }

    public Task UpdateAsync(Party party, CancellationToken cancellationToken = default)
    {
        _dbContext.Parties.Update(party);
        return Task.CompletedTask;
    }

    private sealed class PartyListItemRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string DisplayNameAr { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public bool HasCombinedStatement { get; init; }
        public string[] ActiveTypeCodes { get; init; } = Array.Empty<string>();
        public DateTimeOffset CreatedAt { get; init; }
    }
}
