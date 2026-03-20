namespace AutoPartsERP.Api.Common;

public sealed record ApiError(string Code, string Message);

public sealed record ApiResponse(bool IsSuccess, bool IsPending, string Message, object? Data, ApiError? Error)
{
    public static ApiResponse SuccessResponse(object? data, string message = "Success")
    {
        return new ApiResponse(true, false, message, data, null);
    }

    public static ApiResponse PendingResponse(string message)
    {
        return new ApiResponse(false, true, message, null, null);
    }

    public static ApiResponse FailureResponse(string code, string message)
    {
        return new ApiResponse(false, false, message, null, new ApiError(code, message));
    }

    public static ApiResponse Success(object? data, string message = "Success")
    {
        return SuccessResponse(data, message);
    }

    public static ApiResponse Pending(string message)
    {
        return PendingResponse(message);
    }
}
