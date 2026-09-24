using AutoPartsERP.Application.Features.CompanyProfile;

namespace AutoPartsERP.Api.Modules;

/// <summary>The company's own details printed on every document (settings screen "بيانات المنشأة").</summary>
public sealed class CompanyProfileModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/company-profile").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetCompanyProfileQuery(), ct)).ToApiResult());

        group.MapPut("/", async Task<IResult> (CompanyProfileDto request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new UpdateCompanyProfileCommand(request), ct)).ToApiResult());
    }
}
