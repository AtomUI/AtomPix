namespace AtomPix.Desktop.ViewModels;

using AtomUI.City.Mvvm;
using AtomUI.City.Presentation;

/// <summary>
/// Keeps direct, headless ViewModel construction on the same City command contracts.
/// Production composition always supplies Presentation's dispatcher-aware factory.
/// </summary>
internal sealed class HeadlessUiCommandFactory : IUiCommandFactory
{
    internal static IUiCommandFactory Instance { get; } = new HeadlessUiCommandFactory();

    internal static IActivationScopeOwner? ResolveActivationOwner(
        IUiCommandFactory? suppliedFactory,
        IActivationScopeOwner owner) =>
        suppliedFactory is null || ReferenceEquals(suppliedFactory, Instance) ? null : owner;

    private HeadlessUiCommandFactory()
    {
    }

    public IRelayCommand Create(Action execute, Func<bool>? canExecute = null, CommandExecutionState? state = null) =>
        CommandFactory.Create(execute, canExecute, state);

    public IRelayCommand<T> Create<T>(Action<T?> execute, Predicate<T?>? canExecute = null, CommandExecutionState? state = null) =>
        CommandFactory.Create(execute, canExecute, state);

    public IAsyncRelayCommand CreateAsync(
        Func<CancellationToken, Task> execute,
        CommandExecutionState? state = null,
        IActivationScope? activationScope = null) =>
        CommandFactory.CreateAsync(execute, state, activationScope);

    public IAsyncRelayCommand CreateAsync(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute,
        CommandExecutionState? state = null,
        IActivationScopeOwner? activationOwner = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject,
        ICommandExceptionHandler? exceptionHandler = null) =>
        CommandFactory.CreateAsync(execute, canExecute, state, activationOwner, concurrencyPolicy, exceptionHandler);

    public IAsyncRelayCommand CreateAsync(
        CommandExecutionDomain executionDomain,
        Func<CommandExecutionLease, CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        CommandExecutionState? state = null,
        IActivationScopeOwner? activationOwner = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject,
        ICommandExceptionHandler? exceptionHandler = null) =>
        CommandFactory.CreateAsync(
            executionDomain,
            execute,
            canExecute,
            state,
            activationOwner,
            concurrencyPolicy,
            exceptionHandler);

    public IAsyncRelayCommand<T> CreateAsync<T>(
        Func<T?, CancellationToken, Task> execute,
        Predicate<T?>? canExecute = null,
        CommandExecutionState? state = null,
        IActivationScopeOwner? activationOwner = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject,
        Func<T?, object?>? concurrencyKeySelector = null,
        ICommandExceptionHandler? exceptionHandler = null) =>
        CommandFactory.CreateAsync(
            execute,
            canExecute,
            state,
            activationOwner,
            concurrencyPolicy,
            concurrencyKeySelector,
            exceptionHandler);

    public IAsyncRelayCommand<T> CreateAsync<T>(
        CommandExecutionDomain executionDomain,
        Func<T?, CommandExecutionLease, CancellationToken, Task> execute,
        Predicate<T?>? canExecute = null,
        CommandExecutionState? state = null,
        IActivationScopeOwner? activationOwner = null,
        CommandConcurrencyPolicy concurrencyPolicy = CommandConcurrencyPolicy.Reject,
        Func<T?, object?>? concurrencyKeySelector = null,
        ICommandExceptionHandler? exceptionHandler = null) =>
        CommandFactory.CreateAsync(
            executionDomain,
            execute,
            canExecute,
            state,
            activationOwner,
            concurrencyPolicy,
            concurrencyKeySelector,
            exceptionHandler);
}

public static class AsyncRelayCommandExecutionExtensions
{
    public static Task ExecuteAsync(this IAsyncRelayCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.ExecuteAsync(null);
    }
}
