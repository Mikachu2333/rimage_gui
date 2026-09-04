using System;
using System.Windows.Input;

namespace RimageGui.ViewModels
{
    /// <summary>Minimal <see cref="ICommand"/> so the XAML can bind actions directly.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();

        /// <summary>
        /// Routed through <see cref="CommandManager"/> so WPF re-queries on the
        /// same cadence it uses for built-in commands; the view model only has to
        /// call <see cref="RaiseCanExecuteChanged"/> for changes WPF cannot see.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
