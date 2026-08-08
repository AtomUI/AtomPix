namespace AtomPix.Workflows.Images;

using AtomPix.Core.Errors;
using AtomPix.Core.Ports;
using AtomPix.Core.Results;
using AtomPix.Core.ValueObjects;
using AtomPix.Imaging.Abstractions.Formats;
using AtomPix.Imaging.Abstractions.Processing;

public sealed record OpenFolderRequest(LocalPath DirectoryPath);

public sealed record OpenFolderResult(
    LocalPath DirectoryPath,
    IReadOnlyList<BrowserImageCandidate> Items,
    int UnsupportedFileCount);

public sealed record BrowserImageCandidate(LocalPath Path, string DisplayName);

public sealed class OpenFolderWorkflow
{
    private readonly IFileSystemService _fileSystem;
    private readonly IImageProcessor _imageProcessor;

    public OpenFolderWorkflow(IFileSystemService fileSystem, IImageProcessor imageProcessor)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
    }

    public async Task<OperationResult<OpenFolderResult>> ExecuteAsync(
        OpenFolderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedDirectory = _fileSystem.NormalizePath(request.DirectoryPath);
        if (!normalizedDirectory.Succeeded)
        {
            return OperationResult<OpenFolderResult>.Failure(normalizedDirectory.Error!);
        }

        var enumeration = await _fileSystem
            .EnumerateFilesAsync(normalizedDirectory.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!enumeration.Succeeded)
        {
            return OperationResult<OpenFolderResult>.Failure(enumeration.Error!);
        }

        var candidates = new List<BrowserImageCandidate>();
        var unsupportedCount = 0;
        foreach (var path in enumeration.Value!)
        {
            var normalized = _fileSystem.NormalizePath(path);
            if (!normalized.Succeeded)
            {
                return OperationResult<OpenFolderResult>.Failure(normalized.Error!);
            }

            var normalizedPath = normalized.Value;
            if (!ImageInputFormatExtensions.IsSupported(
                    _fileSystem.GetExtension(normalizedPath),
                    _imageProcessor.Capabilities.SupportedInputFormats))
            {
                unsupportedCount++;
                continue;
            }

            if (candidates.Any(item => _fileSystem.PathsEqual(item.Path, normalizedPath)))
            {
                continue;
            }

            candidates.Add(new BrowserImageCandidate(normalizedPath, _fileSystem.GetFileName(normalizedPath)));
        }

        candidates.Sort((left, right) =>
        {
            var displayNameOrder = NaturalFileNameComparer.Instance.Compare(left.DisplayName, right.DisplayName);
            return displayNameOrder != 0 ? displayNameOrder : _fileSystem.ComparePaths(left.Path, right.Path);
        });

        return OperationResult<OpenFolderResult>.Success(new OpenFolderResult(
            normalizedDirectory.Value,
            candidates.ToArray(),
            unsupportedCount));
    }
}

public sealed record AppendBatchInputsRequest(
    IReadOnlyList<LocalPath> ExistingInputs,
    IReadOnlyList<LocalPath> SelectedFiles,
    IReadOnlyList<LocalPath> SelectedDirectories,
    bool IncludeSubdirectories = false);

public sealed record BatchInputPlan(
    IReadOnlyList<LocalPath> InputPaths,
    int AddedCount,
    int DuplicateCount,
    int UnsupportedCount,
    int UnreadableCount,
    IReadOnlyList<BatchInputSkip> SkippedItems);

public sealed record BatchInputSkip(LocalPath Path, BatchInputSkipReason Reason);

public enum BatchInputSkipReason
{
    Duplicate,
    UnsupportedFormat,
    Missing,
    Unreadable
}

public sealed class AppendBatchInputsWorkflow
{
    private readonly IFileSystemService _fileSystem;
    private readonly IImageProcessor _imageProcessor;

    public AppendBatchInputsWorkflow(IFileSystemService fileSystem, IImageProcessor imageProcessor)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
    }

    public async Task<OperationResult<BatchInputPlan>> ExecuteAsync(
        AppendBatchInputsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExistingInputs);
        ArgumentNullException.ThrowIfNull(request.SelectedFiles);
        ArgumentNullException.ThrowIfNull(request.SelectedDirectories);

        if (request.IncludeSubdirectories)
        {
            return OperationResult<BatchInputPlan>.Failure(new AtomPixError(
                AtomPixErrorCode.InvalidInputPath,
                AtomPixErrorCategory.Validation,
                "Recursive directory input is not supported in the first release."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult<BatchInputPlan>.Failure(WorkflowHelpers.CanceledError());
        }

        var inputs = new List<LocalPath>(request.ExistingInputs.Count + request.SelectedFiles.Count);
        foreach (var existing in request.ExistingInputs)
        {
            var normalized = _fileSystem.NormalizePath(existing);
            if (!normalized.Succeeded)
            {
                return OperationResult<BatchInputPlan>.Failure(normalized.Error!);
            }

            if (!inputs.Any(path => _fileSystem.PathsEqual(path, normalized.Value)))
            {
                inputs.Add(normalized.Value);
            }
        }

        var additions = new List<LocalPath>(request.SelectedFiles);
        foreach (var directory in request.SelectedDirectories)
        {
            var normalizedDirectory = _fileSystem.NormalizePath(directory);
            if (!normalizedDirectory.Succeeded)
            {
                return OperationResult<BatchInputPlan>.Failure(normalizedDirectory.Error!);
            }

            var enumeration = await _fileSystem
                .EnumerateFilesAsync(normalizedDirectory.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!enumeration.Succeeded)
            {
                return OperationResult<BatchInputPlan>.Failure(enumeration.Error!);
            }

            additions.AddRange(enumeration.Value!
                .OrderBy(path => _fileSystem.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, Comparer<LocalPath>.Create(_fileSystem.ComparePaths)));
        }

        var skipped = new List<BatchInputSkip>();
        var addedCount = 0;
        foreach (var candidate in additions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationResult<BatchInputPlan>.Failure(WorkflowHelpers.CanceledError());
            }

            var normalized = _fileSystem.NormalizePath(candidate);
            if (!normalized.Succeeded)
            {
                skipped.Add(new BatchInputSkip(candidate, BatchInputSkipReason.Unreadable));
                continue;
            }

            var normalizedPath = normalized.Value;
            if (!_fileSystem.FileExists(normalizedPath))
            {
                skipped.Add(new BatchInputSkip(normalizedPath, BatchInputSkipReason.Missing));
                continue;
            }

            if (!ImageInputFormatExtensions.IsSupported(
                    _fileSystem.GetExtension(normalizedPath),
                    _imageProcessor.Capabilities.SupportedInputFormats))
            {
                skipped.Add(new BatchInputSkip(normalizedPath, BatchInputSkipReason.UnsupportedFormat));
                continue;
            }

            var readability = await _fileSystem.GetFileSizeAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            if (!readability.Succeeded)
            {
                var reason = readability.Error?.Code == AtomPixErrorCode.InputFileNotFound
                    ? BatchInputSkipReason.Missing
                    : BatchInputSkipReason.Unreadable;
                skipped.Add(new BatchInputSkip(normalizedPath, reason));
                continue;
            }

            if (inputs.Any(path => _fileSystem.PathsEqual(path, normalizedPath)))
            {
                skipped.Add(new BatchInputSkip(normalizedPath, BatchInputSkipReason.Duplicate));
                continue;
            }

            inputs.Add(normalizedPath);
            addedCount++;
        }

        var skippedSnapshot = skipped.ToArray();
        return OperationResult<BatchInputPlan>.Success(new BatchInputPlan(
            inputs.ToArray(),
            addedCount,
            skippedSnapshot.Count(item => item.Reason == BatchInputSkipReason.Duplicate),
            skippedSnapshot.Count(item => item.Reason == BatchInputSkipReason.UnsupportedFormat),
            skippedSnapshot.Count(item => item.Reason is BatchInputSkipReason.Missing or BatchInputSkipReason.Unreadable),
            skippedSnapshot));
    }
}

internal static class ImageInputFormatExtensions
{
    public static bool IsSupported(string extension, IReadOnlySet<ImageFormatKind> supportedFormats)
    {
        var format = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormatKind.Jpeg,
            ".png" => ImageFormatKind.Png,
            ".webp" => ImageFormatKind.WebP,
            ".bmp" => ImageFormatKind.Bmp,
            ".gif" => ImageFormatKind.Gif,
            ".tif" or ".tiff" => ImageFormatKind.Tiff,
            _ => ImageFormatKind.Unknown
        };
        return format != ImageFormatKind.Unknown && supportedFormats.Contains(format);
    }
}

internal sealed class NaturalFileNameComparer : IComparer<string>
{
    public static NaturalFileNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
            {
                var leftEnd = DigitRunEnd(left, leftIndex);
                var rightEnd = DigitRunEnd(right, rightIndex);
                var leftSignificant = SkipLeadingZeros(left, leftIndex, leftEnd);
                var rightSignificant = SkipLeadingZeros(right, rightIndex, rightEnd);
                var leftLength = leftEnd - leftSignificant;
                var rightLength = rightEnd - rightSignificant;
                if (leftLength != rightLength) return leftLength.CompareTo(rightLength);

                var numberOrder = left.AsSpan(leftSignificant, leftLength)
                    .SequenceCompareTo(right.AsSpan(rightSignificant, rightLength));
                if (numberOrder != 0) return numberOrder;

                leftIndex = leftEnd;
                rightIndex = rightEnd;
                continue;
            }

            var characterOrder = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (characterOrder != 0) return characterOrder;
            leftIndex++;
            rightIndex++;
        }

        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }

    private static int DigitRunEnd(string value, int start)
    {
        var index = start;
        while (index < value.Length && char.IsDigit(value[index])) index++;
        return index;
    }

    private static int SkipLeadingZeros(string value, int start, int end)
    {
        var index = start;
        while (index < end - 1 && value[index] == '0') index++;
        return index;
    }
}
