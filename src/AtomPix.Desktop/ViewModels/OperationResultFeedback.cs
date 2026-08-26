namespace AtomPix.Desktop.ViewModels;

using AtomPix.Core.Jobs;
using AtomPix.Core.Output;

public enum OperationFeedbackSeverity
{
    Success,
    Warning,
    Information
}

/// <summary>
/// Semantic result state for a completed single-image operation. The shell consumes
/// each new generation and presents it through the window-level feedback service.
/// </summary>
public sealed class OperationResultFeedback : ObservableObject
{
    private bool _isVisible;
    private string _message = string.Empty;
    private OperationFeedbackSeverity _severity;
    private int _generation;

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public OperationFeedbackSeverity Severity
    {
        get => _severity;
        private set
        {
            if (!SetProperty(ref _severity, value)) return;
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(IsInformation));
        }
    }

    public int Generation
    {
        get => _generation;
        private set => SetProperty(ref _generation, value);
    }

    public bool IsSuccess => Severity == OperationFeedbackSeverity.Success;
    public bool IsWarning => Severity == OperationFeedbackSeverity.Warning;
    public bool IsInformation => Severity == OperationFeedbackSeverity.Information;

    public void Show(ImageJobResult result, OutputWriteDisposition disposition, string completedLabel, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        Severity = result.Status switch
        {
            ImageJobStatus.Skipped => OperationFeedbackSeverity.Warning,
            ImageJobStatus.Canceled => OperationFeedbackSeverity.Information,
            _ => OperationFeedbackSeverity.Success
        };

        var fileName = result.OutputPath is null ? string.Empty : Path.GetFileName(result.OutputPath.Value.Value);
        var primary = result.Status switch
        {
            ImageJobStatus.Succeeded when disposition == OutputWriteDisposition.AutoRenamed =>
                $"{completedLabel}；目标已存在，已自动保存为 {fileName}",
            ImageJobStatus.Succeeded when disposition == OutputWriteDisposition.Overwritten =>
                $"{completedLabel}；已覆盖已有输出 {fileName}",
            ImageJobStatus.Succeeded => $"{completedLabel}；已保存为 {fileName}",
            ImageJobStatus.Skipped => "未生成新文件；目标文件已存在，已按设置跳过",
            ImageJobStatus.Canceled => "任务已取消；未生成新文件",
            _ => string.Empty
        };

        Message = string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(primary)
            ? primary
            : $"{primary}{Environment.NewLine}{detail}";
        IsVisible = !string.IsNullOrWhiteSpace(Message);
        Generation++;
    }

    public void ShowCanceled()
    {
        Severity = OperationFeedbackSeverity.Information;
        Message = "任务已取消；未生成新文件";
        IsVisible = true;
        Generation++;
    }

    public void ShowWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Severity = OperationFeedbackSeverity.Warning;
        Message = message;
        IsVisible = true;
        Generation++;
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Message = string.Empty;
    }
}

public interface IOperationResultFeedbackSource
{
    OperationResultFeedback ResultFeedback { get; }
}
