using System.Windows;
using System.Windows.Input;

namespace UIMarkerEditor;

public static class DialogWindowCommands
{
    public static ICommand CloseWindowCommand { get; } = new CloseWindowCommandImplementation();

    private sealed class CloseWindowCommandImplementation : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            return parameter is Window;
        }

        public void Execute(object? parameter)
        {
            if (parameter is Window window)
            {
                SystemCommands.CloseWindow(window);
            }
        }
    }
}