namespace AtomPix.Desktop.ViewModels;

using AtomPix.Desktop.Navigation;
using System.ComponentModel;
using System.Windows.Input;

/// <summary>
/// Keeps one tool editor mounted while projecting single or batch execution into
/// the same footer. Batch is an execution mode, never a second content page.
/// </summary>
public sealed class ToolDrawerSessionViewModel : ObservableObject, IDisposable, IDesktopForegroundTask, IResultAvailabilityAware
{
    private readonly DesktopNavigationCoordinator _navigation;
    private readonly Func<CancellationToken, Task<bool>> _prepareBatch;
    private readonly Action _synchronizeSingleFromBatch;
    private readonly IToolEditorActions _singleActions;
    private readonly INotifyPropertyChanged? _singleNotifier;
    private bool _isPreparingBatch;
    private int _itemCount;

    public ToolDrawerSessionViewModel(
        object singleContent,
        BatchTaskViewModel batchContent,
        int itemCount,
        Func<CancellationToken, Task<bool>> prepareBatch,
        Action synchronizeSingleFromBatch,
        DesktopNavigationCoordinator navigation)
    {
        SingleContent = singleContent ?? throw new ArgumentNullException(nameof(singleContent));
        _singleActions = singleContent as IToolEditorActions
            ?? throw new ArgumentException("工具编辑器必须公开统一操作契约。", nameof(singleContent));
        _singleNotifier = singleContent as INotifyPropertyChanged;
        BatchContent = batchContent ?? throw new ArgumentNullException(nameof(batchContent));
        _itemCount = itemCount;
        _prepareBatch = prepareBatch ?? throw new ArgumentNullException(nameof(prepareBatch));
        _synchronizeSingleFromBatch = synchronizeSingleFromBatch ?? throw new ArgumentNullException(nameof(synchronizeSingleFromBatch));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        StartBatchCommand = new AsyncCommand(StartBatchAsync, () => CanStartBatch);
        _navigation.NavigationLockChanged += HandleNavigationLockChanged;
        BatchContent.PropertyChanged += HandleBatchPropertyChanged;
        if (_singleNotifier is not null) _singleNotifier.PropertyChanged += HandleSinglePropertyChanged;
        _singleActions.StartActionCommand.CanExecuteChanged += HandleSingleCanExecuteChanged;
    }

    public object SingleContent { get; }
    public BatchTaskViewModel BatchContent { get; }
    public int ItemCount
    {
        get => _itemCount;
        private set
        {
            if (!SetProperty(ref _itemCount, value)) return;
            OnPropertyChanged(nameof(HasBatchOption));
            OnPropertyChanged(nameof(SingleActionColumnSpan));
            NotifyState();
        }
    }

    public bool HasBatchOption => ItemCount > 1;
    public int SingleActionColumnSpan => HasBatchOption ? 1 : 2;
    public string SingleActionLabel => _singleActions.StartActionLabel;
    public string BatchActionLabel => "批量处理";
    public ICommand SingleStartCommand => _singleActions.StartActionCommand;
    public ICommand SingleCancelCommand => _singleActions.CancelActionCommand;
    public ICommand BatchCancelCommand => BatchContent.CancelCommand;
    public bool IsSingleProcessing => _singleActions.IsProcessing;
    public bool IsBatchProcessing => BatchContent.IsProcessing;
    public bool IsBatchExecuting => IsPreparingBatch || IsBatchProcessing;
    public bool IsIdle => !IsSingleProcessing && !IsBatchExecuting;
    public bool CanEditContent => !IsBatchExecuting;
    public bool CanStartBatch =>
        HasBatchOption
        && IsIdle
        && !_navigation.IsNavigationLocked
        && _singleActions.StartActionCommand.CanExecute(null);
    public double BatchProgressRatio => BatchContent.ProgressRatio;
    public string BatchProgressSummary => IsPreparingBatch ? $"正在准备 {ItemCount} 张图片" : BatchContent.ProgressSummary;

    public bool IsPreparingBatch
    {
        get => _isPreparingBatch;
        private set
        {
            if (!SetProperty(ref _isPreparingBatch, value)) return;
            NotifyState();
        }
    }

    public bool IsProcessing => IsSingleProcessing || IsBatchProcessing;
    public AsyncCommand StartBatchCommand { get; }

    public void RequestCancellation()
    {
        if (IsBatchProcessing) BatchContent.RequestCancellation();
        else if (IsSingleProcessing) _singleActions.CancelActionCommand.Execute(null);
    }

    public void RefreshResultAvailability()
    {
        if (SingleContent is IResultAvailabilityAware single) single.RefreshResultAvailability();
        BatchContent.RefreshResultAvailability();
    }

    public void UpdateItemCount(int value) => ItemCount = Math.Max(0, value);
    public void SynchronizeDraftFromBatch() => _synchronizeSingleFromBatch();

    public void Dispose()
    {
        _navigation.NavigationLockChanged -= HandleNavigationLockChanged;
        BatchContent.PropertyChanged -= HandleBatchPropertyChanged;
        if (_singleNotifier is not null) _singleNotifier.PropertyChanged -= HandleSinglePropertyChanged;
        _singleActions.StartActionCommand.CanExecuteChanged -= HandleSingleCanExecuteChanged;
    }

    private async Task StartBatchAsync(CancellationToken cancellationToken)
    {
        if (!CanStartBatch) return;
        IsPreparingBatch = true;
        try
        {
            if (!await _prepareBatch(cancellationToken)) return;
            IsPreparingBatch = false;
            await BatchContent.StartCommand.ExecuteAsync(cancellationToken);
        }
        finally
        {
            IsPreparingBatch = false;
        }
    }

    private void HandleNavigationLockChanged(object? sender, bool value) => NotifyState();

    private void HandleSinglePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Batch submission consumes the same visible draft as the single action, so
        // every draft change can affect the batch command's CanExecute state.
        NotifyState();
    }

    private void HandleSingleCanExecuteChanged(object? sender, EventArgs e) => NotifyState();

    private void HandleBatchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BatchTaskViewModel.IsProcessing)
            or nameof(BatchTaskViewModel.ProgressRatio)
            or nameof(BatchTaskViewModel.ProgressSummary)
            or null or "") NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsSingleProcessing));
        OnPropertyChanged(nameof(IsBatchProcessing));
        OnPropertyChanged(nameof(IsBatchExecuting));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(CanEditContent));
        OnPropertyChanged(nameof(CanStartBatch));
        OnPropertyChanged(nameof(BatchProgressRatio));
        OnPropertyChanged(nameof(BatchProgressSummary));
        StartBatchCommand.NotifyCanExecuteChanged();
    }
}
