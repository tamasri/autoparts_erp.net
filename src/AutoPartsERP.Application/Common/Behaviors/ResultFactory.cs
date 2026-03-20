namespace AutoPartsERP.Application.Common.Behaviors;

internal static class ResultFactory
{
    public static TResponse Failure<TResponse>(Error error)
    {
        var responseType = typeof(TResponse);

        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            throw new InvalidOperationException($"{responseType.Name} must be a Result<T>.");
        }

        var innerType = responseType.GetGenericArguments()[0];
        var method = typeof(Result<>)
            .MakeGenericType(innerType)
            .GetMethod(nameof(Result<object>.Failure), new[] { typeof(Error) })
            ?? throw new InvalidOperationException("Failure factory method was not found.");

        return (TResponse)method.Invoke(null, new object[] { error })!;
    }
}
