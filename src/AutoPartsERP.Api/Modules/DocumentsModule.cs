using AutoPartsERP.Application.Features.Documents;
using AutoPartsERP.Contracts.Documents;
using Microsoft.AspNetCore.Mvc;

namespace AutoPartsERP.Api.Modules;

/// <summary>Numbered documents of every kind: previous/next by number, the Super Admin's recorded deletion, and the numbering check.</summary>
public sealed class DocumentsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/documents").RequireAuthorization();

        group.MapGet("/deleted", async Task<IResult> (int? page, int? pageSize, string? series, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetDeletedDocumentsQuery(page ?? 1, pageSize ?? 20, series), ct)).ToApiResult());

        group.MapGet("/numbering", async Task<IResult> (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetNumberingHealthQuery(), ct)).ToApiResult());

        group.MapGet("/{kind}/{id:guid}/neighbors", async Task<IResult> (string kind, Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetDocumentNeighborsQuery(kind, id), ct)).ToApiResult());

        group.MapDelete("/{kind}/{id:guid}", async Task<IResult> (string kind, Guid id, [FromBody] DeleteDocumentRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DeleteDocumentCommand(kind, id, request.Reason), ct)).ToApiResult());
    }
}
