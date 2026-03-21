namespace AutoPartsERP.Api.Modules;

public sealed class BarcodesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/barcodes").RequireAuthorization();

        group.MapPost("/scan", async Task<IResult> (BarcodeScanRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ScanBarcodeCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/generate/item/{id:guid}", async Task<IResult> (
                Guid id,
                IBarcodeService barcodeService,
                IDbConnectionFactory connectionFactory,
                CancellationToken cancellationToken) =>
            {
                await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
                var item = await connection.QuerySingleOrDefaultAsync<(Guid Id, string PartNumberCanonical)>(
                    new CommandDefinition(
                        "SELECT id AS Id, part_number_canonical AS PartNumberCanonical FROM items WHERE id = @Id;",
                        new { Id = id },
                        cancellationToken: cancellationToken));

                if (item.Id == Guid.Empty)
                {
                    return Results.Problem(title: "Item.NotFound", detail: "Item was not found.", statusCode: StatusCodes.Status404NotFound);
                }

                var png = barcodeService.GenerateItemCodePng(item.Id, item.PartNumberCanonical);
                return Results.File(png, "image/png", $"item-{item.PartNumberCanonical}.png");
            });

        group.MapGet("/generate/batch/{id:guid}", async Task<IResult> (
                Guid id,
                IBarcodeService barcodeService,
                IDbConnectionFactory connectionFactory,
                CancellationToken cancellationToken) =>
            {
                await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
                var batch = await connection.QuerySingleOrDefaultAsync<(Guid Id, string BatchNumber)>(
                    new CommandDefinition(
                        "SELECT id AS Id, batch_number AS BatchNumber FROM batches WHERE id = @Id;",
                        new { Id = id },
                        cancellationToken: cancellationToken));

                if (batch.Id == Guid.Empty)
                {
                    return Results.Problem(title: "Batch.NotFound", detail: "Batch was not found.", statusCode: StatusCodes.Status404NotFound);
                }

                var png = barcodeService.GenerateBatchCodePng(batch.Id, batch.BatchNumber);
                return Results.File(png, "image/png", $"batch-{batch.BatchNumber}.png");
            });
    }
}

