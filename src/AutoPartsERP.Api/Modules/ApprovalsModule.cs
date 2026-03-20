namespace AutoPartsERP.Api.Modules;

public sealed class ApprovalsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/approvals").RequireAuthorization();

        group.MapGet("/pending", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var pageNumber = page <= 0 ? 1 : page;
                var size = pageSize <= 0 ? 20 : pageSize;
                var result = await sender.Send(new GetPendingApprovalsQuery(pageNumber, size), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{requestId:guid}/approve", async Task<IResult> (Guid requestId, ApproveApprovalRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ApproveRequestCommand(requestId, request.Comment), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{requestId:guid}/reject", async Task<IResult> (Guid requestId, RejectApprovalRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RejectRequestCommand(requestId, request.Comment), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
