namespace PatchHound.Core.Common;

public class Result<T>
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    private readonly T? _value;

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed result.");

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
    }

    private Result(string error)
    {
        IsSuccess = false;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(string error) => new(error);
}
