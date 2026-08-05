namespace AtomPix.Core.Results;

using AtomPix.Core.Errors;

public sealed record OperationResult
{
    private OperationResult(bool succeeded, AtomPixError? error)
    {
        if (succeeded && error is not null)
        {
            throw new ArgumentException("Successful results cannot contain an error.", nameof(error));
        }

        if (!succeeded && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Failed results must contain an error.");
        }

        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public AtomPixError? Error { get; }

    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(AtomPixError error) => new(false, error);
}

public sealed record OperationResult<T>
{
    private OperationResult(bool succeeded, T? value, AtomPixError? error)
    {
        if (succeeded && error is not null)
        {
            throw new ArgumentException("Successful results cannot contain an error.", nameof(error));
        }

        if (!succeeded && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Failed results must contain an error.");
        }

        if (succeeded && value is null)
        {
            throw new ArgumentNullException(nameof(value), "Successful results must contain a value.");
        }

        Succeeded = succeeded;
        Value = value;
        Error = error;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public AtomPixError? Error { get; }

    public static OperationResult<T> Success(T value) => new(true, value, null);

    public static OperationResult<T> Failure(AtomPixError error) => new(false, default, error);
}