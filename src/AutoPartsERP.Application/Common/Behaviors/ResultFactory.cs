namespace AutoPartsERP.Application.Common.Behaviors;

internal static class ResultFactory
{
    public static TResponse Failure<TResponse>(Error error)
    {
        var responseType = typeof(TResponse);

        // Commands that return a plain Result (no payload) must be short-circuited by the pipeline
        // behaviors too — otherwise a validation/permission/approval failure surfaces as a 500.
        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException($"{responseType.Name} must be a Result or a Result<T>.");
        }

        var innerType = responseType.GetGenericArguments()[0];
        var method = typeof(Result<>)
            .MakeGenericType(innerType)
            .GetMethod(nameof(Result<object>.Failure), new[] { typeof(Error) })
            ?? throw new InvalidOperationException("Failure factory method was not found.");

        return (TResponse)method.Invoke(null, new object[] { error })!;
    }
}
