namespace AtomPix.Desktop.ViewModels;

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
    public static string FromPicker(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "无法打开系统选择器，请重试。" : message;

    public static string FromWorkflow(AtomPix.Core.Errors.AtomPixError? error) => error?.Code switch
    {
        AtomPix.Core.Errors.AtomPixErrorCode.InputFileNotFound => "图片已不存在，请重新选择。",
        AtomPix.Core.Errors.AtomPixErrorCode.InputDirectoryNotFound => "文件夹已不存在，请重新选择。",
        AtomPix.Core.Errors.AtomPixErrorCode.UnsupportedInputFormat => "该文件不是 AtomPix 当前支持的图片格式。",
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidImageFile => "图片已损坏或无法识别，请选择其他图片。",
        AtomPix.Core.Errors.AtomPixErrorCode.InputFileTooLarge => "图片文件超过当前处理上限。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageDimensionsExceedLimit => "图片分辨率超过当前处理上限。",
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidResizeOptions => "调整尺寸参数无效，请检查宽度、高度或百分比。",
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidCropOptions => "裁剪区域无效，请检查位置和尺寸。",
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidCompressionQuality => "压缩质量必须是 1 到 100 的整数。",
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidConversionOptions => "转换参数无效，请检查输出格式、质量或透明背景色。",
        AtomPix.Core.Errors.AtomPixErrorCode.InvalidOutputNamingPattern => "文件名格式无效，请只使用 {name} 和 {index} 占位符。",
        AtomPix.Core.Errors.AtomPixErrorCode.OutputPathConflictsWithInput => "无法覆盖原始图片，请改用自动重命名。",
        AtomPix.Core.Errors.AtomPixErrorCode.OutputFileAlreadyExists => "目标文件已存在。",
        AtomPix.Core.Errors.AtomPixErrorCode.SettingsLoadFailed => "设置读取失败，请恢复默认设置或检查配置目录。",
        AtomPix.Core.Errors.AtomPixErrorCode.SettingsSaveFailed => "设置保存失败，修改尚未丢失，请重试。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageCompressFailed => "压缩失败，请重试或更换图片。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageConvertFailed => "转换失败，请重试或更换图片。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageResizeFailed => "调整尺寸失败，请重试或更换图片。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageCropFailed => "裁剪失败，请重试或更换图片。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageWriteFailed => "无法写入输出文件，请检查目录权限或选择其他输出位置。",
        AtomPix.Core.Errors.AtomPixErrorCode.ImageResourceLimitExceeded => "图片处理超出当前资源保护上限，请使用更小的图片或尺寸。",
        AtomPix.Core.Errors.AtomPixErrorCode.InsufficientDiskSpace => "磁盘空间不足，请释放空间或选择其他输出目录。",
        AtomPix.Core.Errors.AtomPixErrorCode.OperationCanceled => string.Empty,
        AtomPix.Core.Errors.AtomPixErrorCode.Unknown => "发生未预期错误，请使用诊断编号定位本地日志。",
        _ => "操作未完成，请检查输入与输出设置后重试。"
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
    public static string FormatSizeChange(AtomPix.Core.Jobs.ImageJobResult? result)
    {
        if (result?.SizeChangeKind is null || result.SizeDeltaBytes is null)
        {
            return string.Empty;
        }

        var bytes = Math.Abs(result.SizeDeltaBytes.Value);
        var ratio = Math.Abs(result.SizeDeltaRatio ?? 0) * 100;
        return result.SizeChangeKind switch
        {
            AtomPix.Core.Jobs.FileSizeChangeKind.Reduced => $"体积减少 {FormatBytes(bytes)}（{ratio:0.##}%）",
            AtomPix.Core.Jobs.FileSizeChangeKind.Unchanged => "文件大小未变化",
            AtomPix.Core.Jobs.FileSizeChangeKind.Increased => $"体积增加 {FormatBytes(bytes)}（{ratio:0.##}%）",
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
