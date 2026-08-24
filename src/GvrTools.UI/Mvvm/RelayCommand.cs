using System;
using System.Windows.Input;

namespace GvrTools.UI.Mvvm
{
    /// <summary>
    /// Straightforward ICommand.
    ///
    /// It raises its own CanExecuteChanged instead of piggybacking on WPF's CommandManager: during
    /// a long batch the command state changes at known moments (start, finish, cancel), and having
    /// the view model say so explicitly is both cheaper and easier to follow than re-querying every
    /// command in the window on every keystroke.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Func<object, bool>)null : _ => canExecute())
        {
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
