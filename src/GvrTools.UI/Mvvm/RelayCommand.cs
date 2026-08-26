using System;
using System.Windows.Input;
using GvrTools.Core.Diagnostics;

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

        /// <summary>
        /// Every button/menu item in every GVR Tools window goes through here. WPF invokes
        /// ICommand.Execute from its own dispatcher loop, and this add-in has no
        /// Dispatcher.UnhandledException handler anywhere (by design -- see RevitRestart.cs) -- so
        /// an exception that escaped this call would not just fail one command, it would crash the
        /// whole Revit process with an "unrecoverable error". Every command gets this net once,
        /// here, instead of relying on each view model to remember to add its own try/catch.
        /// </summary>
        public void Execute(object parameter)
        {
            try
            {
                _execute(parameter);
            }
            catch (Exception ex)
            {
                LogFailure(ex);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        private static void LogFailure(Exception ex)
        {
            try
            {
                new RollingFileLog("App").Error("Un comando falló sin manejar.", ex);
            }
            catch
            {
                // el logging nunca debe ser la razón de un segundo fallo.
            }
        }
    }
}
