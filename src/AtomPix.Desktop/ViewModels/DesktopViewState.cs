namespace AtomPix.Desktop.ViewModels;

using AtomPix.Desktop.Localization;

public enum DesktopContentState
{
    Empty,
    Loading,
    Ready,
    Failure
}

public enum DesktopExecutionState
{
    Draft,
    Processing,
    Success,
    Failure,
    Canceled,
    Skipped
}

public interface IDesktopForegroundTask
{
    bool IsProcessing { get; }

    void RequestCancellation();
}

public interface IToolEditorActions : IDesktopForegroundTask
{
    string StartActionLabel { get; }

    System.Windows.Input.ICommand StartActionCommand { get; }

    System.Windows.Input.ICommand CancelActionCommand { get; }
}

public sealed record DesktopChoiceOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public static class DesktopErrorText
{
    public static string FromPicker(string? message, IAtomPixLocalizedText text) =>
        string.IsNullOrWhiteSpace(message) ? text.GetString("Errors.PickerFailed") : message;

    public static string FromWorkflow(AtomPix.Core.Errors.AtomPixError? error, IAtomPixLocalizedText text) => error?.Code switch
    {
        AtomPix.Core.Errors.AtomPixErrorCode.InputFileNotFound => text.GetString("Errors.InputFileNotFound"),
        AtomPix.Core.Errors.AtomPixErrorCode.InputDirectoryNotFound => text.GetString("Errors.InputDirectoryNotFound"),
        AtomPix.Core.Errors.AtomPixErrorCode.UnsupportedInputFormat => text.GetString("Errors.UnsupportedInputFormat"),
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidImageFile => text.GetString("Errors.InvalidImageFile"),
        AtomPix.Core.Errors.AtomPixErrorCode.InputFileTooLarge => text.GetString("Errors.InputFileTooLarge"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageDimensionsExceedLimit => text.GetString("Errors.ImageDimensionsExceedLimit"),
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidResizeOptions => text.GetString("Errors.InvalidResizeOptions"),
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidCropOptions => text.GetString("Errors.InvalidCropOptions"),
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidCompressionQuality => text.GetString("Errors.InvalidCompressionQuality"),
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidConversionOptions => text.GetString("Errors.InvalidConversionOptions"),
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidOutputNamingPattern => text.GetString("Errors.InvalidOutputNamingPattern"),
        AtomPix.Core.Errors.AtomPixErrorCode.OutputPathConflictsWithInput => text.GetString("Errors.OutputPathConflictsWithInput"),
        AtomPix.Core.Errors.AtomPixErrorCode.OutputFileAlreadyExists => text.GetString("Errors.OutputFileAlreadyExists"),
        AtomPix.Core.Errors.AtomPixErrorCode.SettingsLoadFailed => text.GetString("Errors.SettingsLoadFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.SettingsSaveFailed => text.GetString("Errors.SettingsSaveFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageCompressFailed => text.GetString("Errors.ImageCompressFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageConvertFailed => text.GetString("Errors.ImageConvertFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageResizeFailed => text.GetString("Errors.ImageResizeFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageCropFailed => text.GetString("Errors.ImageCropFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageWriteFailed => text.GetString("Errors.ImageWriteFailed"),
        AtomPix.Core.Errors.AtomPixErrorCode.ImageResourceLimitExceeded => text.GetString("Errors.ImageResourceLimitExceeded"),
        AtomPix.Core.Errors.AtomPixErrorCode.InsufficientDiskSpace => text.GetString("Errors.InsufficientDiskSpace"),
        AtomPix.Core.Errors.AtomPixErrorCode.OperationCanceled => string.Empty,
        AtomPix.Core.Errors.AtomPixErrorCode.Unknown => text.GetString("Errors.Unknown"),
        _ => text.GetString("Errors.Default")
    };

    public static string? DiagnosticId(AtomPix.Core.Errors.AtomPixError? error) =>
        error?.Details is not null
        && error.Details.TryGetValue("DiagnosticId", out var diagnosticId)
        && diagnosticId.StartsWith("APX-", StringComparison.Ordinal)
            ? diagnosticId
            : null;
}

public static class DesktopResultText
{
    public static string FormatSizeChange(AtomPix.Core.Jobs.ImageJobResult? result, IAtomPixLocalizedText text)
    {
        if (result?.SizeChangeKind is null || result.SizeDeltaBytes is null)
        {
            return string.Empty;
        }

        var bytes = Math.Abs(result.SizeDeltaBytes.Value);
        var ratio = Math.Abs(result.SizeDeltaRatio ?? 0) * 100;
        return result.SizeChangeKind switch
        {
            AtomPix.Core.Jobs.FileSizeChangeKind.Reduced => text.Format("Common.Result.SizeReduced", FormatBytes(bytes), ratio),
            AtomPix.Core.Jobs.FileSizeChangeKind.Unchanged => text.GetString("Common.Result.SizeUnchanged"),
            AtomPix.Core.Jobs.FileSizeChangeKind.Increased => text.Format("Common.Result.SizeIncreased", FormatBytes(bytes), ratio),
            _ => string.Empty
        };
    }

    public static string FormatBytes(long value) => value switch
    {
        >= 1024L * 1024L * 1024L => $"{value / (1024d * 1024d * 1024d):0.##} GB",
        >= 1024L * 1024L => $"{value / (1024d * 1024d):0.##} MB",
        >= 1024L => $"{value / 1024d:0.##} KB",
        _ => $"{value} B"
    };
}
