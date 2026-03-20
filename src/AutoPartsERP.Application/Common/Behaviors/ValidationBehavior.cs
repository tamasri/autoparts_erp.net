namespace AutoPartsERP.Application.Common.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));
        var failures = results
            .SelectMany(result => result.Errors)
            .Where(error => error is not null)
            .Select(error => $"{error.PropertyName}: {error.ErrorMessage}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (failures.Length == 0)
        {
            return await next();
        }

        return ResultFactory.Failure<TResponse>(new Error("Validation.Failed", string.Join(" | ", failures)));
    }
}
