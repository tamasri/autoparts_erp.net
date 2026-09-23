namespace AutoPartsERP.Api.Modules;

/// <summary>Sales representatives: figures per period, a rep's customers and invoices, terms, and handing customers over.</summary>
public sealed class SalesRepsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var reps = app.MapGroup("/api/v1/sales-reps").RequireAuthorization();

        reps.MapGet("/", async Task<IResult> (DateOnly? from, DateOnly? to, bool? includeInactive, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetSalesRepsQuery(from, to, includeInactive == true), ct)).ToApiResult());

        reps.MapGet("/candidates", async Task<IResult> (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetSalesRepCandidatesQuery(), ct)).ToApiResult());

        reps.MapGet("/{userId:guid}", async Task<IResult> (Guid userId, DateOnly? from, DateOnly? to, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetSalesRepDetailQuery(userId, from, to), ct)).ToApiResult());

        reps.MapPut("/{userId:guid}", async Task<IResult> (Guid userId, SaveSalesRepRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveSalesRepCommand(userId, request), ct)).ToApiResult());

        reps.MapPost("/{userId:guid}/customers", async Task<IResult> (Guid userId, AssignSalesRepCustomersRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new AssignSalesRepCustomersCommand(userId, request), ct)).ToApiResult());
    }
}
