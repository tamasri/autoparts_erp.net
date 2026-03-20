namespace AutoPartsERP.Domain.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && !error.IsNone)
        {
            throw new ArgumentException("Successful results cannot include an error.", nameof(error));
        }

        if (!isSuccess && error.IsNone)
        {
            throw new ArgumentException("Failed results must include an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success()
    {
        return new Result(true, Error.None);
    }

    public static Result Failure(Error error)
    {
        return new Result(false, error);
    }
}
