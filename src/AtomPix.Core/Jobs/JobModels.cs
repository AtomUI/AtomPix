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
    Convert
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

    public void MarkSkipped(AtomPixError? error, DateTimeOffset completedAt)
    {
        EnsureCanComplete(nameof(MarkSkipped));
        EnsureCompletionTime(completedAt);
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
        if (items.Count == 0)
        {
            throw new ArgumentException("Batch job must contain at least one item.", nameof(items));
        }

        Id = id;
        Type = type;
        Items = items;
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

    public void Complete(BatchJobStatus status, DateTimeOffset completedAt)
    {
        if (status is BatchJobStatus.Pending or BatchJobStatus.Running)
        {
            throw new ArgumentException("Batch completion status must be terminal.", nameof(status));
        }

        if (Status is not (BatchJobStatus.Pending or BatchJobStatus.Running))
        {
            throw new InvalidOperationException($"Cannot complete batch job when status is {Status}.");
        }

        var lowerBound = StartedAt ?? CreatedAt;
        if (completedAt < lowerBound)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAt), "Completion time cannot be earlier than start or creation time.");
        }

        Status = status;
        CompletedAt = completedAt;
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

    public long? SavedBytes => InputSizeBytes is { } input && OutputSizeBytes is { } output ? input - output : null;

    public double? SavedRatio => InputSizeBytes is > 0 && SavedBytes is { } saved ? saved / (double)InputSizeBytes.Value : null;
}

public sealed record BatchResult
{
    public BatchResult(BatchJobId batchId, ImageJobType type, BatchJobStatus status, IReadOnlyList<ImageJobResult> items, int? totalCount = null)
    {
        if (status is BatchJobStatus.Pending or BatchJobStatus.Running)
        {
            throw new ArgumentException("Batch result status must be terminal.", nameof(status));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("Batch result must contain at least one item.", nameof(items));
        }

        if (totalCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count must be greater than zero.");
        }

        if (totalCount is { } planned && planned < items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be smaller than completed item count.");
        }

        BatchId = batchId;
        Type = type;
        Status = status;
        Items = items;
        TotalCount = totalCount ?? items.Count;
    }

    public BatchJobId BatchId { get; }
    public ImageJobType Type { get; }
    public BatchJobStatus Status { get; }
    public IReadOnlyList<ImageJobResult> Items { get; }

    public int TotalCount { get; }
    public int CompletedCount => Items.Count;
    public int SucceededCount => Items.Count(item => item.Status == ImageJobStatus.Succeeded);
    public int FailedCount => Items.Count(item => item.Status == ImageJobStatus.Failed);
    public int SkippedCount => Items.Count(item => item.Status == ImageJobStatus.Skipped);
    public int CanceledCount => Items.Count(item => item.Status == ImageJobStatus.Canceled);
    public long TotalInputSizeBytes => Items.Sum(item => item.InputSizeBytes ?? 0);
    public long TotalOutputSizeBytes => Items.Sum(item => item.OutputSizeBytes ?? 0);
    public long TotalSavedBytes => TotalInputSizeBytes - TotalOutputSizeBytes;
    public double? TotalSavedRatio => TotalInputSizeBytes > 0 ? TotalSavedBytes / (double)TotalInputSizeBytes : null;
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
