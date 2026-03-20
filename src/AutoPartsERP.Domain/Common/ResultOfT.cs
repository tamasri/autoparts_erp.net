namespace AutoPartsERP.Domain.Common;

public sealed class Result<T> : Result
{
    private Result(T? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value)
    {
        return new Result<T>(value, true, Error.None);
    }

    public static new Result<T> Failure(Error error)
    {
        return new Result<T>(default, false, error);
    }
}
