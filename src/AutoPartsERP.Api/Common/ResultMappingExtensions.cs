namespace AutoPartsERP.Api.Common;

public static class ResultMappingExtensions
{
    public static IResult ToApiResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(ApiResponse.Success(result.Value));
        }

        if (string.Equals(result.Error.Code, "Approval.Pending", StringComparison.Ordinal))
        {
            return Results.Accepted(value: ApiResponse.Pending(result.Error.Message));
        }

        return Results.Problem(
            title: result.Error.Code,
            detail: result.Error.Message,
            statusCode: ResolveStatusCode(result.Error.Code));
    }

    public static IResult ToApiResult(this Result result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(ApiResponse.Success(true));
        }

        if (string.Equals(result.Error.Code, "Approval.Pending", StringComparison.Ordinal))
        {
            return Results.Accepted(value: ApiResponse.Pending(result.Error.Message));
        }

        return Results.Problem(
            title: result.Error.Code,
            detail: result.Error.Message,
            statusCode: ResolveStatusCode(result.Error.Code));
    }

    private static int ResolveStatusCode(string errorCode)
    {
        if (errorCode.StartsWith("Validation.", StringComparison.Ordinal))
        {
            return StatusCodes.Status400BadRequest;
        }

        if (errorCode.StartsWith("Authorization.", StringComparison.Ordinal))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (errorCode.StartsWith("Auth.Unauthorized", StringComparison.Ordinal))
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (errorCode.EndsWith(".NotFound", StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        if (errorCode.Contains("Conflict", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.Status409Conflict;
        }

        if (errorCode.StartsWith("Approval.Pending", StringComparison.Ordinal))
        {
            return StatusCodes.Status202Accepted;
        }

        return StatusCodes.Status400BadRequest;
    }
}
