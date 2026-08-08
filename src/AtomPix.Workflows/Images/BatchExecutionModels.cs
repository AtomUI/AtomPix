namespace AtomPix.Workflows.Images;

using AtomPix.Core.Errors;
using AtomPix.Core.Jobs;
using AtomPix.Core.Output;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;

public sealed record BatchOutputPlan
{
    public BatchOutputPlan(IReadOnlyList<BatchOutputPlanItem> items, string effectivePattern)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new ArgumentException("Batch output plan must contain at least one item.", nameof(items));
        if (string.IsNullOrWhiteSpace(effectivePattern)) throw new ArgumentException("Effective pattern cannot be empty.", nameof(effectivePattern));
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].ItemIndex != index || items[index].SequenceNumber != index + 1)
            {
                throw new ArgumentException("Batch output plan items must be ordered and contiguous.", nameof(items));
            }
        }

        Items = items.ToArray();
        EffectivePattern = effectivePattern;
    }

    public IReadOnlyList<BatchOutputPlanItem> Items { get; }

    public string EffectivePattern { get; }
}

public sealed record BatchOutputPlanItem
{
    public BatchOutputPlanItem(
        int itemIndex,
        int sequenceNumber,
        LocalPath inputPath,
        LocalPath outputPath,
        BatchOutputDecision decision,
        AtomPixError? reason)
    {
        if (itemIndex < 0) throw new ArgumentOutOfRangeException(nameof(itemIndex));
        if (sequenceNumber != itemIndex + 1) throw new ArgumentException("Sequence number must equal item index plus one.", nameof(sequenceNumber));
        if (!Enum.IsDefined(decision)) throw new ArgumentOutOfRangeException(nameof(decision));
        if (decision == BatchOutputDecision.Process && reason is not null)
        {
            throw new ArgumentException("Process decisions cannot carry a skip reason.", nameof(reason));
        }
        if (decision == BatchOutputDecision.Skip && reason?.Code != AtomPixErrorCode.OutputFileAlreadyExists)
        {
            throw new ArgumentException("Skip decisions require an OutputFileAlreadyExists reason.", nameof(reason));
        }

        ItemIndex = itemIndex;
        SequenceNumber = sequenceNumber;
        InputPath = inputPath;
        OutputPath = outputPath;
        Decision = decision;
        Reason = reason;
    }

    public int ItemIndex { get; }
    public int SequenceNumber { get; }
    public LocalPath InputPath { get; }
    public LocalPath OutputPath { get; }
    public BatchOutputDecision Decision { get; }
    public AtomPixError? Reason { get; }
}

public enum BatchOutputDecision
{
    Process,
    Skip
}

public sealed record BatchExecutionProgress<TItemResult>
    where TItemResult : class
{
    public BatchExecutionProgress(
        long sequence,
        BatchProgressSnapshot summary,
        BatchItemProgress<TItemResult>? changedItem,
        BatchOutputPlan outputPlan)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        OutputPlan = outputPlan ?? throw new ArgumentNullException(nameof(outputPlan));
        if (outputPlan.Items.Count != summary.TotalCount)
        {
            throw new ArgumentException("Output plan count must equal progress total count.", nameof(outputPlan));
        }
        ChangedItem = changedItem;
    }

    public long Sequence { get; }
    public BatchProgressSnapshot Summary { get; }
    public BatchItemProgress<TItemResult>? ChangedItem { get; }
    public BatchOutputPlan OutputPlan { get; }
}

public sealed record BatchItemProgress<TItemResult>
    where TItemResult : class
{
    public BatchItemProgress(
        int index,
        ImageJobId jobId,
        LocalPath inputPath,
        ImageJobStatus status,
        TItemResult? result)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (status == ImageJobStatus.Pending) throw new ArgumentException("Pending items are not published.", nameof(status));
        if (status == ImageJobStatus.Running && result is not null) throw new ArgumentException("Running progress cannot carry a terminal result.", nameof(result));
        if (status != ImageJobStatus.Running && result is null) throw new ArgumentNullException(nameof(result), "Terminal progress requires an item result.");

        Index = index;
        JobId = jobId;
        InputPath = inputPath;
        Status = status;
        Result = result;
    }

    public int Index { get; }
    public ImageJobId JobId { get; }
    public LocalPath InputPath { get; }
    public ImageJobStatus Status { get; }
    public TItemResult? Result { get; }
}

internal sealed class BatchOutputPlanner
{
    private readonly IFileSystemService _fileSystem;

    public BatchOutputPlanner(IFileSystemService fileSystem) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public OperationResult<BatchOutputPlan> CreatePlan(
        IReadOnlyList<LocalPath> inputPaths,
        OutputPolicy outputPolicy,
        Func<LocalPath, string?> outputExtension)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(outputPolicy);
        ArgumentNullException.ThrowIfNull(outputExtension);
        if (inputPaths.Count == 0)
        {
            return OperationResult<BatchOutputPlan>.Failure(WorkflowHelpers.ValidationError("Input path list cannot be empty."));
        }

        var normalizedInputs = new List<LocalPath>(inputPaths.Count);
        foreach (var input in inputPaths)
        {
            var normalized = _fileSystem.NormalizePath(input);
            if (!normalized.Succeeded) return OperationResult<BatchOutputPlan>.Failure(normalized.Error!);
            if (normalizedInputs.Any(item => _fileSystem.PathsEqual(item, normalized.Value)))
            {
                return OperationResult<BatchOutputPlan>.Failure(WorkflowHelpers.ValidationError("Batch input paths must be unique."));
            }
            normalizedInputs.Add(normalized.Value);
        }

        var basePattern = outputPolicy.NamingPolicy.GetBasePattern();
        var effectivePattern = inputPaths.Count > 1 && !basePattern.Contains("{index}", StringComparison.Ordinal)
            ? basePattern + "_{index}"
            : basePattern;
        var indexWidth = Math.Max(3, inputPaths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);

        var desiredItems = new List<(LocalPath Input, LocalPath Desired)>(inputPaths.Count);
        for (var index = 0; index < normalizedInputs.Count; index++)
        {
            var input = normalizedInputs[index];
            var extension = outputExtension(input);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return OperationResult<BatchOutputPlan>.Failure(new AtomPixError(
                    AtomPixErrorCode.UnsupportedOutputFormat,
                    AtomPixErrorCategory.UnsupportedFormat,
                    "Batch output extension cannot be resolved."));
            }

            var name = _fileSystem.GetFileNameWithoutExtension(input);
            if (string.IsNullOrWhiteSpace(name))
            {
                return OperationResult<BatchOutputPlan>.Failure(WorkflowHelpers.ValidationError("Input path must contain a file name."));
            }

            var stem = effectivePattern
                .Replace("{name}", name, StringComparison.Ordinal)
                .Replace("{index}", (index + 1).ToString($"D{indexWidth}", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (!IsValidExpandedStem(stem))
            {
                return InvalidPattern("Expanded output naming pattern is not a valid file name.");
            }

            var directory = ResolveOutputDirectory(input, outputPolicy.LocationPolicy);
            var desired = _fileSystem.Combine(directory, stem + NormalizeExtension(extension));
            desiredItems.Add((input, desired));
        }

        if (outputPolicy.OverwritePolicy == OverwritePolicy.Overwrite)
        {
            var conflicts = desiredItems
                .Where(item => normalizedInputs.Any(input => _fileSystem.PathsEqual(input, item.Desired)))
                .ToArray();
            if (conflicts.Length > 0)
            {
                return OperationResult<BatchOutputPlan>.Failure(new AtomPixError(
                    AtomPixErrorCode.OutputPathConflictsWithInput,
                    AtomPixErrorCategory.Validation,
                    "One or more batch outputs would overwrite an input image.",
                    new Dictionary<string, string>
                    {
                        ["ConflictCount"] = conflicts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["InputPath"] = conflicts[0].Input.Value,
                        ["OutputPath"] = conflicts[0].Desired.Value
                    }));
            }
        }

        var reserved = new List<LocalPath>();
        var planItems = new List<BatchOutputPlanItem>(desiredItems.Count);
        for (var index = 0; index < desiredItems.Count; index++)
        {
            var (input, desired) = desiredItems[index];
            var collides = _fileSystem.FileExists(desired)
                || normalizedInputs.Any(path => _fileSystem.PathsEqual(path, desired))
                || reserved.Any(path => _fileSystem.PathsEqual(path, desired));

            if (outputPolicy.OverwritePolicy == OverwritePolicy.Skip && collides)
            {
                planItems.Add(new BatchOutputPlanItem(
                    index,
                    index + 1,
                    input,
                    desired,
                    BatchOutputDecision.Skip,
                    WorkflowHelpers.OutputExistsError(desired)));
                continue;
            }

            LocalPath output;
            if (outputPolicy.OverwritePolicy == OverwritePolicy.AutoRename && collides)
            {
                output = FindAvailablePath(desired, normalizedInputs, reserved);
            }
            else
            {
                output = desired;
            }

            if (reserved.Any(path => _fileSystem.PathsEqual(path, output)))
            {
                return InvalidPattern("Output naming pattern expands to duplicate paths.");
            }

            reserved.Add(output);
            planItems.Add(new BatchOutputPlanItem(index, index + 1, input, output, BatchOutputDecision.Process, null));
        }

        return OperationResult<BatchOutputPlan>.Success(new BatchOutputPlan(planItems, effectivePattern));
    }

    public async Task<OperationResult> PrepareOutputDirectoriesAsync(
        BatchOutputPlan plan,
        CancellationToken cancellationToken)
    {
        var directories = new List<LocalPath>();
        foreach (var item in plan.Items.Where(item => item.Decision == BatchOutputDecision.Process))
        {
            var directoryText = Path.GetDirectoryName(item.OutputPath.Value);
            var directory = new LocalPath(string.IsNullOrWhiteSpace(directoryText) ? "." : directoryText);
            if (directories.Any(existing => _fileSystem.PathsEqual(existing, directory))) continue;
            directories.Add(directory);
        }

        foreach (var directory in directories)
        {
            var create = await _fileSystem.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            if (!create.Succeeded) return create;
        }

        return OperationResult.Success();
    }

    private LocalPath FindAvailablePath(
        LocalPath desired,
        IReadOnlyList<LocalPath> normalizedInputs,
        IReadOnlyList<LocalPath> reserved)
    {
        for (var index = 1; ; index++)
        {
            var candidate = _fileSystem.BuildIndexedPath(desired, index);
            if (!_fileSystem.FileExists(candidate)
                && !normalizedInputs.Any(path => _fileSystem.PathsEqual(path, candidate))
                && !reserved.Any(path => _fileSystem.PathsEqual(path, candidate)))
            {
                return candidate;
            }
        }
    }

    internal static LocalPath ResolveOutputDirectory(LocalPath inputPath, OutputLocationPolicy policy)
    {
        var inputDirectory = Path.GetDirectoryName(inputPath.Value);
        var sameAsInputDirectory = string.IsNullOrWhiteSpace(inputDirectory) ? "." : inputDirectory;
        var subfolderBaseDirectory = string.IsNullOrWhiteSpace(inputDirectory) ? string.Empty : inputDirectory;
        return policy.Mode switch
        {
            OutputLocationMode.SameAsInput => new LocalPath(sameAsInputDirectory),
            OutputLocationMode.Subfolder => new LocalPath(Path.Combine(subfolderBaseDirectory, policy.SubfolderName!)),
            OutputLocationMode.CustomDirectory => new LocalPath(policy.CustomDirectory!),
            _ => throw new InvalidOperationException("Unsupported output location mode passed Core validation.")
        };
    }

    internal static string ExpandSingleStem(OutputNamingPolicy policy, string inputName)
    {
        var pattern = policy.GetBasePattern();
        return pattern
            .Replace("{name}", inputName, StringComparison.Ordinal)
            .Replace("{index}", "001", StringComparison.Ordinal);
    }

    private static bool IsValidExpandedStem(string stem) =>
        !string.IsNullOrWhiteSpace(stem)
        && stem is not ("." or "..")
        && !stem.Contains('/')
        && !stem.Contains('\\')
        && stem.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && (!OperatingSystem.IsWindows() || (!stem.EndsWith('.') && !stem.EndsWith(' ')));

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;

    private static OperationResult<BatchOutputPlan> InvalidPattern(string message) =>
        OperationResult<BatchOutputPlan>.Failure(new AtomPixError(
            AtomPixErrorCode.InvalidOutputNamingPattern,
            AtomPixErrorCategory.Validation,
            message));
}
