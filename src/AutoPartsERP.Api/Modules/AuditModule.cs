namespace AutoPartsERP.Api.Modules;

public sealed class AuditModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? module,
                string? entityType,
                Guid? entityId,
                Guid? actorId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var pageNumber = page <= 0 ? 1 : page;
                var size = pageSize <= 0 ? 50 : pageSize;
                var result = await sender.Send(
                    new GetAuditLogsQuery(pageNumber, size, from, to, module, entityType, entityId, actorId),
                    cancellationToken);

                return result.ToApiResult();
            });

        group.MapGet("/{auditLogId:guid}", async Task<IResult> (Guid auditLogId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAuditLogByIdQuery(auditLogId), cancellationToken);
                return result.ToApiResult();
            });
    }
}
