// AsyncRelayCommand.cs - An ICommand for async handlers that guards against re-entry.
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TID3.ViewModels
{
    /// <summary>
    /// An <see cref="ICommand"/> wrapping an async handler. While the handler is
    /// running the command reports <see cref="CanExecute"/> = false so it cannot be
    /// invoked re-entrantly (e.g. double-clicking a Search button). This replaces the
    /// <c>async void</c> click handlers, so exceptions are observed rather than
    /// crashing the app.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Func<object?, bool>? _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute())
        {
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                _isRunning = value;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CanExecute(object? parameter)
            => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            IsRunning = true;
            try
            {
                await _execute(parameter);
            }
            finally
            {
                IsRunning = false;
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
