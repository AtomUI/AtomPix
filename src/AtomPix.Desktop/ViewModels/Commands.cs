namespace AtomPix.Desktop.ViewModels;

using System.Windows.Input;

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        TryGetParameter(parameter, out var value) && (_canExecute?.Invoke(value) ?? true);

    public void Execute(object? parameter)
    {
        if (TryGetParameter(parameter, out var value) && CanExecute(value))
        {
            _execute(value);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static bool TryGetParameter(object? parameter, out T value)
    {
        if (parameter is T typed)
        {
            value = typed;
            return true;
        }

        if (parameter is null && default(T) is null)
        {
            value = default!;
            return true;
        }

        value = default!;
        return false;
    }
}

public sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _executionCancellation;

    public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _executionCancellation is not null;

    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke() ?? true);

    public void Execute(object? parameter) => _ = ExecuteAsync();

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExecute(null))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = linked;
        NotifyCanExecuteChanged();
        try
        {
            await _execute(linked.Token).ConfigureAwait(true);
        }
        finally
        {
            _executionCancellation = null;
            NotifyCanExecuteChanged();
        }
    }

    public void Cancel() => _executionCancellation?.Cancel();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand<T> : ICommand
{
    private readonly Func<T, CancellationToken, Task> _execute;
    private readonly Func<T, bool>? _canExecute;
    private CancellationTokenSource? _executionCancellation;

    public AsyncCommand(Func<T, CancellationToken, Task> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _executionCancellation is not null;

    public bool CanExecute(object? parameter) =>
        !IsRunning && TryGetParameter(parameter, out var value) && (_canExecute?.Invoke(value) ?? true);

    public void Execute(object? parameter)
    {
        if (TryGetParameter(parameter, out var value))
        {
            _ = ExecuteAsync(value);
        }
    }

    public async Task ExecuteAsync(T parameter, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellation = linked;
        NotifyCanExecuteChanged();
        try
        {
            await _execute(parameter, linked.Token).ConfigureAwait(true);
        }
        finally
        {
            _executionCancellation = null;
            NotifyCanExecuteChanged();
        }
    }

    public void Cancel() => _executionCancellation?.Cancel();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static bool TryGetParameter(object? parameter, out T value)
    {
        if (parameter is T typed)
        {
            value = typed;
            return true;
        }

        if (parameter is null && default(T) is null)
        {
            value = default!;
            return true;
        }

        value = default!;
        return false;
    }
}
