namespace AtomPix.Core.Jobs;

using AtomPix.Core.Errors;
using AtomPix.Core.ValueObjects;

public readonly record struct ImageJobId
{
    public ImageJobId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Image job id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ImageJobId New() => new(Guid.NewGuid());
}

public readonly record struct BatchJobId
{
    public BatchJobId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Batch job id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static BatchJobId New() => new(Guid.NewGuid());
}

public enum ImageJobType
{
    Compress,
    Convert,
    Resize,
    Crop
}

public enum ImageJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled,
    Skipped
}

public enum BatchJobStatus
{
    Pending,
    Running,
    Succeeded,
    PartiallySucceeded,
    Failed,
    Canceled
}

public sealed class ImageJob
{
    public ImageJob(ImageJobId id, ImageJobType type, LocalPath inputPath, DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        InputPath = inputPath;
        CreatedAt = createdAt;
        Status = ImageJobStatus.Pending;
    }

    public ImageJobId Id { get; }
    public ImageJobType Type { get; }
    public LocalPath InputPath { get; }
    public LocalPath? OutputPath { get; private set; }
    public ImageJobStatus Status { get; private set; }
    public AtomPixError? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkRunning(DateTimeOffset startedAt)
    {
        EnsureStatus(ImageJobStatus.Pending, nameof(MarkRunning));
        if (startedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAt), "Start time cannot be earlier than creation time.");
        }

        Status = ImageJobStatus.Running;
        StartedAt = startedAt;
    }

    public void MarkSucceeded(LocalPath outputPath, DateTimeOffset completedAt)
    {
        EnsureCanComplete(nameof(MarkSucceeded));
        EnsureCompletionTime(completedAt);
        OutputPath = outputPath;
        Status = ImageJobStatus.Succeeded;
        CompletedAt = completedAt;
    }

    public void MarkFailed(AtomPixError error, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(error);
        EnsureCanComplete(nameof(MarkFailed));
        EnsureCompletionTime(completedAt);
        Error = error;
        Status = ImageJobStatus.Failed;
        CompletedAt = completedAt;
    }

    public void MarkCanceled(AtomPixError error, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(error);
        EnsureCanComplete(nameof(MarkCanceled));
        EnsureCompletionTime(completedAt);
        Error = error;
        Status = ImageJobStatus.Canceled;
        CompletedAt = completedAt;
    }

    public void MarkSkipped(LocalPath outputPath, AtomPixError error, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code != AtomPixErrorCode.OutputFileAlreadyExists)
        {
            throw new ArgumentException("Skipped jobs require an OutputFileAlreadyExists error.", nameof(error));
        }

        EnsureCanComplete(nameof(MarkSkipped));
        EnsureCompletionTime(completedAt);
        OutputPath = outputPath;
        Error = error;
        Status = ImageJobStatus.Skipped;
        CompletedAt = completedAt;
    }

    private void EnsureStatus(ImageJobStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Cannot {operation} when job status is {Status}.");
        }
    }

    private void EnsureCanComplete(string operation)
    {
        if (Status is not (ImageJobStatus.Pending or ImageJobStatus.Running))
        {
            throw new InvalidOperationException($"Cannot {operation} when job status is {Status}.");
        }
    }

    private void EnsureCompletionTime(DateTimeOffset completedAt)
    {
        var lowerBound = StartedAt ?? CreatedAt;
        if (completedAt < lowerBound)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAt), "Completion time cannot be earlier than job start or creation time.");
        }
    }
}

public sealed class BatchJob
{
    public BatchJob(BatchJobId id, ImageJobType type, IReadOnlyList<ImageJob> items, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("Batch job must contain at least one item.", nameof(items));
        }

        if (items.Any(item => item.Type != type))
        {
            throw new ArgumentException("All image jobs must have the same type as the batch job.", nameof(items));
        }
        if (items.Select(item => item.Id).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("Batch image job ids must be unique.", nameof(items));
        }

        Id = id;
        Type = type;
        Items = items.ToArray();
        CreatedAt = createdAt;
        Status = BatchJobStatus.Pending;
    }

    public BatchJobId Id { get; }
    public ImageJobType Type { get; }
    public IReadOnlyList<ImageJob> Items { get; }
    public BatchJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public AtomPixError? Error { get; private set; }

    public void MarkRunning(DateTimeOffset startedAt)
    {
        if (Status != BatchJobStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot start batch job when status is {Status}.");
        }

        if (startedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAt), "Start time cannot be earlier than creation time.");
        }

        Status = BatchJobStatus.Running;
        StartedAt = startedAt;
    }

    public void CompleteNaturally(DateTimeOffset completedAt)
    {
        EnsureCanComplete();
        if (Items.Any(item => item.Status is ImageJobStatus.Pending or ImageJobStatus.Running))
        {
            throw new InvalidOperationException("Natural completion requires every image job to be terminal.");
        }
        if (Items.Any(item => item.Status == ImageJobStatus.Canceled))
        {
            throw new InvalidOperationException("Canceled image jobs require the batch cancellation transition.");
        }

        EnsureCompletionTime(completedAt);
        var completedSuccessfully = Items.Count(item => item.Status is ImageJobStatus.Succeeded or ImageJobStatus.Skipped);
        var failed = Items.Count(item => item.Status == ImageJobStatus.Failed);
        Status = completedSuccessfully switch
        {
            > 0 when failed > 0 => BatchJobStatus.PartiallySucceeded,
            > 0 => BatchJobStatus.Succeeded,
            _ => BatchJobStatus.Failed
        };
        CompletedAt = completedAt;
    }

    public void Cancel(AtomPixError error, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code != AtomPixErrorCode.OperationCanceled || error.Category != AtomPixErrorCategory.Cancellation)
        {
            throw new ArgumentException("Batch cancellation requires OperationCanceled / Cancellation.", nameof(error));
        }

        EnsureCanComplete();
        EnsureNoRunningItems();
        EnsureCompletionTime(completedAt);
        Error = error;
        Status = BatchJobStatus.Canceled;
        CompletedAt = completedAt;
    }

    public void Abort(AtomPixError error, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(error);
        EnsureCanComplete();
        EnsureNoRunningItems();
        EnsureCompletionTime(completedAt);
        Error = error;
        Status = Items.Any(item => item.Status is ImageJobStatus.Succeeded or ImageJobStatus.Skipped)
            ? BatchJobStatus.PartiallySucceeded
            : BatchJobStatus.Failed;
        CompletedAt = completedAt;
    }

    private void EnsureCanComplete()
    {
        if (Status is not (BatchJobStatus.Pending or BatchJobStatus.Running))
        {
            throw new InvalidOperationException($"Cannot complete batch job when status is {Status}.");
        }
    }

    private void EnsureNoRunningItems()
    {
        if (Items.Any(item => item.Status == ImageJobStatus.Running))
        {
            throw new InvalidOperationException("Batch terminal transitions cannot leave a running image job.");
        }
    }

    private void EnsureCompletionTime(DateTimeOffset completedAt)
    {
        var lowerBound = StartedAt ?? CreatedAt;
        if (completedAt < lowerBound)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAt), "Completion time cannot be earlier than start or creation time.");
        }

    }
}

public sealed record ImageJobResult
{
    public ImageJobResult(
        ImageJobId jobId,
        ImageJobType type,
        LocalPath inputPath,
        LocalPath? outputPath,
        ImageJobStatus status,
        long? inputSizeBytes,
        long? outputSizeBytes,
        AtomPixError? error)
    {
        if (status is ImageJobStatus.Pending or ImageJobStatus.Running)
        {
            throw new ArgumentException("Job result status must be terminal.", nameof(status));
        }

        if (inputSizeBytes is < 0 || outputSizeBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSizeBytes), "File sizes cannot be negative.");
        }

        if (status == ImageJobStatus.Succeeded && outputPath is null)
        {
            throw new ArgumentNullException(nameof(outputPath), "Succeeded job results require an output path.");
        }

        if (status == ImageJobStatus.Succeeded && outputSizeBytes is null)
        {
            throw new ArgumentNullException(nameof(outputSizeBytes), "Succeeded job results require an output size.");
        }

        if (status == ImageJobStatus.Failed && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Failed job results require an error.");
        }

        if (status == ImageJobStatus.Canceled && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Canceled job results require an error.");
        }

        if (status == ImageJobStatus.Skipped)
        {
            if (outputPath is null)
            {
                throw new ArgumentNullException(nameof(outputPath), "Skipped job results require the planned output path.");
            }

            if (error?.Code != AtomPixErrorCode.OutputFileAlreadyExists)
            {
                throw new ArgumentException("Skipped job results require an OutputFileAlreadyExists error.", nameof(error));
            }
        }

        JobId = jobId;
        Type = type;
        InputPath = inputPath;
        OutputPath = outputPath;
        Status = status;
        InputSizeBytes = inputSizeBytes;
        OutputSizeBytes = outputSizeBytes;
        Error = error;
    }

    public ImageJobId JobId { get; }
    public ImageJobType Type { get; }
    public LocalPath InputPath { get; }
    public LocalPath? OutputPath { get; }
    public ImageJobStatus Status { get; }
    public long? InputSizeBytes { get; }
    public long? OutputSizeBytes { get; }
    public AtomPixError? Error { get; }

    public long? SizeDeltaBytes => InputSizeBytes is { } input && OutputSizeBytes is { } output ? output - input : null;

    public double? SizeDeltaRatio => InputSizeBytes is > 0 && SizeDeltaBytes is { } delta ? delta / (double)InputSizeBytes.Value : null;

    public FileSizeChangeKind? SizeChangeKind => SizeDeltaBytes switch
    {
        < 0 => FileSizeChangeKind.Reduced,
        0 => FileSizeChangeKind.Unchanged,
        > 0 => FileSizeChangeKind.Increased,
        _ => null
    };
}

public enum FileSizeChangeKind
{
    Reduced,
    Unchanged,
    Increased
}

public sealed record BatchResult
{
    public BatchResult(
        BatchJobId batchId,
        ImageJobType type,
        BatchJobStatus status,
        int totalCount,
        IReadOnlyList<ImageJobResult> items,
        AtomPixError? error)
    {
        if (status is BatchJobStatus.Pending or BatchJobStatus.Running)
        {
            throw new ArgumentException("Batch result status must be terminal.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(items);

        if (totalCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count must be greater than zero.");
        }

        if (totalCount < items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be smaller than completed item count.");
        }

        if (status == BatchJobStatus.Canceled && error is null)
        {
            throw new ArgumentNullException(nameof(error), "Canceled batch results require a batch-level error.");
        }

        BatchId = batchId;
        Type = type;
        Status = status;
        TotalCount = totalCount;
        Items = items.ToArray();
        Error = error;
    }

    public BatchJobId BatchId { get; }
    public ImageJobType Type { get; }
    public BatchJobStatus Status { get; }
    public int TotalCount { get; }
    public IReadOnlyList<ImageJobResult> Items { get; }
    public AtomPixError? Error { get; }

    public int CompletedCount => Items.Count;
    public int SucceededCount => Items.Count(item => item.Status == ImageJobStatus.Succeeded);
    public int FailedCount => Items.Count(item => item.Status == ImageJobStatus.Failed);
    public int SkippedCount => Items.Count(item => item.Status == ImageJobStatus.Skipped);
    public int CanceledCount => Items.Count(item => item.Status == ImageJobStatus.Canceled);
    public int SizeComparedItemCount => ComparableItems.Count;
    public long ProcessedInputSizeBytes => ComparableItems.Sum(item => item.InputSizeBytes!.Value);
    public long ProcessedOutputSizeBytes => ComparableItems.Sum(item => item.OutputSizeBytes!.Value);
    public long? TotalSizeDeltaBytes => SizeComparedItemCount == 0 ? null : ProcessedOutputSizeBytes - ProcessedInputSizeBytes;
    public double? TotalSizeDeltaRatio => SizeComparedItemCount > 0 && ProcessedInputSizeBytes > 0
        ? TotalSizeDeltaBytes / (double)ProcessedInputSizeBytes
        : null;
    public FileSizeChangeKind? TotalSizeChangeKind => TotalSizeDeltaBytes switch
    {
        < 0 => FileSizeChangeKind.Reduced,
        0 => FileSizeChangeKind.Unchanged,
        > 0 => FileSizeChangeKind.Increased,
        _ => null
    };
    public int ReducedItemCount => ComparableItems.Count(item => item.SizeChangeKind == FileSizeChangeKind.Reduced);
    public int UnchangedItemCount => ComparableItems.Count(item => item.SizeChangeKind == FileSizeChangeKind.Unchanged);
    public int IncreasedItemCount => ComparableItems.Count(item => item.SizeChangeKind == FileSizeChangeKind.Increased);

    private IReadOnlyList<ImageJobResult> ComparableItems => Items
        .Where(item => item.Status == ImageJobStatus.Succeeded
            && item.InputSizeBytes is not null
            && item.OutputSizeBytes is not null)
        .ToArray();
}
public sealed record BatchProgressSnapshot
{
    public BatchProgressSnapshot(
        BatchJobId batchId,
        ImageJobType type,
        int totalCount,
        int completedCount,
        int succeededCount,
        int failedCount,
        int skippedCount,
        int canceledCount,
        LocalPath? currentInputPath)
    {
        if (totalCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count must be greater than zero.");
        }

        if (completedCount < 0 || succeededCount < 0 || failedCount < 0 || skippedCount < 0 || canceledCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedCount), "Progress counters cannot be negative.");
        }

        if (completedCount > totalCount)
        {
            throw new ArgumentOutOfRangeException(nameof(completedCount), completedCount, "Completed count cannot exceed total count.");
        }

        if (succeededCount + failedCount + skippedCount + canceledCount != completedCount)
        {
            throw new ArgumentException("Completed count must equal the sum of terminal item counters.");
        }

        BatchId = batchId;
        Type = type;
        TotalCount = totalCount;
        CompletedCount = completedCount;
        SucceededCount = succeededCount;
        FailedCount = failedCount;
        SkippedCount = skippedCount;
        CanceledCount = canceledCount;
        CurrentInputPath = currentInputPath;
    }

    public BatchJobId BatchId { get; }
    public ImageJobType Type { get; }
    public int TotalCount { get; }
    public int CompletedCount { get; }
    public int SucceededCount { get; }
    public int FailedCount { get; }
    public int SkippedCount { get; }
    public int CanceledCount { get; }
    public LocalPath? CurrentInputPath { get; }
    public bool IsCompleted => CompletedCount == TotalCount;
    public double CompletionRatio => CompletedCount / (double)TotalCount;

    public static BatchProgressSnapshot FromResults(
        BatchJobId batchId,
        ImageJobType type,
        int totalCount,
        IReadOnlyList<ImageJobResult> completedItems,
        LocalPath? currentInputPath)
    {
        ArgumentNullException.ThrowIfNull(completedItems);
        return new BatchProgressSnapshot(
            batchId,
            type,
            totalCount,
            completedItems.Count,
            completedItems.Count(item => item.Status == ImageJobStatus.Succeeded),
            completedItems.Count(item => item.Status == ImageJobStatus.Failed),
            completedItems.Count(item => item.Status == ImageJobStatus.Skipped),
            completedItems.Count(item => item.Status == ImageJobStatus.Canceled),
            currentInputPath);
    }
}
