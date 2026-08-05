namespace AtomPix.Infrastructure;

using AtomPix.Core.Errors;
using AtomPix.Core.Results;

internal static class InfrastructureErrors
{
    public static OperationResult Canceled() =>
        OperationResult.Failure(new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));

    public static OperationResult<T> Canceled<T>() =>
        OperationResult<T>.Failure(new AtomPixError(AtomPixErrorCode.OperationCanceled, AtomPixErrorCategory.Cancellation, "Operation canceled."));

    public static OperationResult Failure(AtomPixErrorCode code, AtomPixErrorCategory category, string message, Exception? exception = null)
    {
        var details = exception is null
            ? null
            : new Dictionary<string, string> { ["exception"] = exception.GetType().Name, ["message"] = exception.Message };
        return OperationResult.Failure(new AtomPixError(code, category, message, details));
    }

    public static OperationResult<T> Failure<T>(AtomPixErrorCode code, AtomPixErrorCategory category, string message, Exception? exception = null)
    {
        var details = exception is null
            ? null
            : new Dictionary<string, string> { ["exception"] = exception.GetType().Name, ["message"] = exception.Message };
        return OperationResult<T>.Failure(new AtomPixError(code, category, message, details));
    }
}
