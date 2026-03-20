namespace AutoPartsERP.Contracts.Common;

public sealed record ApiEnvelope<T>(
    bool Success,
    T? Data,
    IReadOnlyCollection<ApiError> Errors,
    string? TraceId,
    DateTimeOffset TimestampUtc)
{
    public static ApiEnvelope<T> Ok(T data, string? traceId = null)
    {
        return new ApiEnvelope<T>(true, data, Array.Empty<ApiError>(), traceId, DateTimeOffset.UtcNow);
    }

    public static ApiEnvelope<T> Fail(IEnumerable<ApiError> errors, string? traceId = null)
    {
        return new ApiEnvelope<T>(false, default, errors.ToArray(), traceId, DateTimeOffset.UtcNow);
    }
}
