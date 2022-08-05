namespace Spice86.WPF.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

// <summary>Provides an async implementation of the <see cref="ICommand"/> interface. 
/// The command is inactive when the command's task is running. </summary>
public class AsyncRelayCommand : CommandBase {
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning = false;

    /// <summary>Initializes a new instance of the <see cref="AsyncRelayCommand"/> class. </summary>
    /// <param name="execute">The function to execute. </param>
    public AsyncRelayCommand(Func<Task> execute)
        : this(execute, null) { }

    /// <summary>Initializes a new instance of the <see cref="AsyncRelayCommand"/> class. </summary>
    /// <param name="execute">The function. </param>
    /// <param name="canExecute">The predicate to check whether the function can be executed. </param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute) {
        if (execute == null)
            throw new ArgumentNullException("execute");

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Gets a value indicating whether the command is currently running. </summary>
    public bool IsRunning {
        get { return _isRunning; }
        private set {
            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Defines the method to be called when the command is invoked. </summary>
    protected override async void Execute() {
        var task = _execute();
        if (task != null) {
            IsRunning = true;
            await task;
            IsRunning = false;
        }
    }

    /// <summary>Gets a value indicating whether the command can execute in its current state. </summary>
    public override bool CanExecute {
        get { return !IsRunning && (_canExecute == null || _canExecute()); }
    }
}

/// <summary>Provides an implementation of the <see cref="ICommand"/> interface. </summary>
/// <typeparam name="TParameter">The type of the command parameter. </typeparam>
public class AsyncRelayCommand<TParameter> : CommandBase<TParameter> {
    private readonly Func<TParameter, Task> _execute;
    private readonly Predicate<TParameter>? _canExecute;
    private bool _isRunning = false;

    /// <summary>Initializes a new instance of the <see cref="AsyncRelayCommand"/> class. </summary>
    /// <param name="execute">The function. </param>
    public AsyncRelayCommand(Func<TParameter, Task> execute)
        : this(execute, null) {
    }

    /// <summary>Initializes a new instance of the <see cref="AsyncRelayCommand"/> class. </summary>
    /// <param name="execute">The function. </param>
    /// <param name="canExecute">The predicate to check whether the function can be executed. </param>
    public AsyncRelayCommand(Func<TParameter, Task> execute, Predicate<TParameter>? canExecute) {
        if (execute == null)
            throw new ArgumentNullException("execute");

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Gets a value indicating whether the command is currently running. </summary>
    public bool IsRunning {
        get { return _isRunning; }
        private set {
            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>Gets a value indicating whether the command can execute in its current state. </summary>
    [DebuggerStepThrough]
    public override bool CanExecute(TParameter parameter) {
        return !IsRunning && (_canExecute == null || _canExecute(parameter));
    }

    /// <summary>Defines the method to be called when the command is invoked. </summary>
    protected override async void Execute(TParameter parameter) {
        var task = _execute(parameter);
        if (task != null) {
            IsRunning = true;
            await task;
            IsRunning = false;
        }
    }
}