namespace AtomPix.Workflows.Diagnostics;

using System.Diagnostics;
using System.Security.Cryptography;
using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Results;
using AtomPix.Workflows.Images;
using Microsoft.Extensions.Logging;

internal static class WorkflowDiagnostics
{
    private static readonly EventId StartedEvent = new(1000, "WorkflowStarted");
    private static readonly EventId CompletedEvent = new(1001, "WorkflowCompleted");
    private static readonly EventId UnexpectedEvent = new(1002, "WorkflowUnexpectedFailure");

    public static async Task<OperationResult<T>> RunAsync<T>(
        ILogger? logger,
        string workflowName,
        Func<Task<OperationResult<T>>> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentNullException.ThrowIfNull(action);
        if (logger is null) return await action().ConfigureAwait(false);

        var operationId = Guid.NewGuid().ToString("N");
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationId"] = operationId,
            ["WorkflowName"] = workflowName
        });
        var stopwatch = Stopwatch.StartNew();
        logger.Log(LogLevel.Information, StartedEvent, new Dictionary<string, object?>
        {
            ["WorkflowName"] = workflowName,
            ["Outcome"] = "Started"
        }, null, static (_, _) => "Workflow started.");

        OperationResult<T> result;
        try
        {
            result = await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var diagnosticId = CreateDiagnosticId();
            logger.Log(LogLevel.Error, UnexpectedEvent, new Dictionary<string, object?>
            {
                ["WorkflowName"] = workflowName,
                ["Outcome"] = "Failure",
                ["DurationMs"] = stopwatch.ElapsedMilliseconds,
                ["ErrorCode"] = AtomPixErrorCode.Unknown.ToString(),
                ["ErrorCategory"] = AtomPixErrorCategory.Unexpected.ToString(),
                ["DiagnosticId"] = diagnosticId
            }, exception, static (_, error) => error?.Message ?? "Unexpected workflow failure.");
            return OperationResult<T>.Failure(new AtomPixError(
                AtomPixErrorCode.Unknown,
                AtomPixErrorCategory.Unexpected,
                "An unexpected error occurred.",
                new Dictionary<string, string> { ["DiagnosticId"] = diagnosticId }));
        }

        stopwatch.Stop();
        var fields = new Dictionary<string, object?>
        {
            ["WorkflowName"] = workflowName,
            ["Outcome"] = GetOutcome(result),
            ["DurationMs"] = stopwatch.ElapsedMilliseconds
        };
        AddResultContext(fields, result.Value);
        if (!result.Succeeded)
        {
            fields["ErrorCode"] = result.Error!.Code.ToString();
            fields["ErrorCategory"] = result.Error.Category.ToString();
        }

        logger.Log(
            GetCompletionLevel(result),
            CompletedEvent,
            fields,
            null,
            static (state, _) => $"Workflow completed with outcome {state["Outcome"]}.");
        return result;
    }

    public static void LogBatchItemTerminal(ILogger? logger, int itemIndex, ImageJob job)
    {
        if (logger is null || job.Status is not (ImageJobStatus.Failed or ImageJobStatus.Canceled)) return;
        logger.Log(
            job.Status == ImageJobStatus.Canceled ? LogLevel.Information : LogLevel.Warning,
            new EventId(1003, "BatchItemCompleted"),
            new Dictionary<string, object?>
            {
                ["ItemIndex"] = itemIndex,
                ["JobId"] = job.Id.Value,
                ["Outcome"] = job.Status.ToString(),
                ["ErrorCode"] = job.Error?.Code.ToString(),
                ["ErrorCategory"] = job.Error?.Category.ToString()
            },
            null,
            static (state, _) => $"Batch item completed with outcome {state["Outcome"]}.");
    }

    private static LogLevel GetCompletionLevel<T>(OperationResult<T> result)
    {
        if (result.Succeeded || result.Error!.Category is AtomPixErrorCategory.Validation or AtomPixErrorCategory.Cancellation)
        {
            return LogLevel.Information;
        }

        return LogLevel.Warning;
    }

    private static string GetOutcome<T>(OperationResult<T> result)
    {
        if (!result.Succeeded)
        {
            return result.Error!.Category == AtomPixErrorCategory.Cancellation ? "Canceled" : "StartRejected";
        }

        return result.Value switch
        {
            CompressImageResult value => value.JobResult.Status.ToString(),
            ConvertImageResult value => value.JobResult.Status.ToString(),
            ResizeImageResult value => value.JobResult.Status.ToString(),
            CropImageResult value => value.JobResult.Status.ToString(),
            BatchCompressResult value => value.BatchResult.Status.ToString(),
            BatchConvertResult value => value.BatchResult.Status.ToString(),
            BatchResizeResult value => value.BatchResult.Status.ToString(),
            _ => "Succeeded"
        };
    }

    private static void AddResultContext<T>(IDictionary<string, object?> fields, T? value)
    {
        switch (value)
        {
            case CompressImageResult result:
                AddJob(fields, result.JobResult);
                break;
            case ConvertImageResult result:
                AddJob(fields, result.JobResult);
                break;
            case ResizeImageResult result:
                AddJob(fields, result.JobResult);
                break;
            case CropImageResult result:
                AddJob(fields, result.JobResult);
                break;
            case BatchCompressResult result:
                AddBatch(fields, result.BatchResult);
                break;
            case BatchConvertResult result:
                AddBatch(fields, result.BatchResult);
                break;
            case BatchResizeResult result:
                AddBatch(fields, result.BatchResult);
                break;
        }
    }

    private static void AddJob(IDictionary<string, object?> fields, ImageJobResult result)
    {
        fields["JobId"] = result.JobId.Value;
        fields["ErrorCode"] = result.Error?.Code.ToString();
        fields["ErrorCategory"] = result.Error?.Category.ToString();
    }

    private static void AddBatch(IDictionary<string, object?> fields, BatchResult result)
    {
        fields["BatchId"] = result.BatchId.Value;
        fields["TotalCount"] = result.TotalCount;
        fields["SucceededCount"] = result.SucceededCount;
        fields["FailedCount"] = result.FailedCount;
        fields["SkippedCount"] = result.SkippedCount;
        fields["CanceledCount"] = result.CanceledCount;
        fields["ErrorCode"] = result.Error?.Code.ToString();
        fields["ErrorCategory"] = result.Error?.Category.ToString();
    }

    private static string CreateDiagnosticId() =>
        "APX-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
}
