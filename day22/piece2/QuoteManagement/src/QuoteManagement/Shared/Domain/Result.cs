namespace QuoteManagement.Shared.Domain;

// A small Result type so domain/application code can report "this business rule failed"
// without throwing exceptions for expected, everyday validation outcomes (author missing,
// text too long, etc). Exceptions stay for genuinely exceptional failures.
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

public sealed class Result<TValue> : Result
{
    public TValue? Value { get; }

    private Result(bool isSuccess, TValue? value, string? error) : base(isSuccess, error)
    {
        Value = value;
    }

    public static Result<TValue> Success(TValue value) => new(true, value, null);
    public static new Result<TValue> Failure(string error) => new(false, default, error);
}
