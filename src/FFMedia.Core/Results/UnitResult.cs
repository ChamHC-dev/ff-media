namespace FFMedia.Core.Results;

/// <summary>Outcome of an operation that carries no value but may fail with a user-facing reason.
/// Lives in UnitResult.cs because Result.cs holds the generic <see cref="Result{T}"/>.</summary>
public sealed class Result
{
    private Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public string? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}
