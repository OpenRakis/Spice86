namespace Spice86.ViewModels;

using Spice86.Views;

public class MainWindowViewModel : ViewModelBase {
    private readonly MainWindow _mainWindow;

    public string Greeting => "Welcome to Avalonia!";

    public MainWindowViewModel(MainWindow window) {
        _mainWindow = window;
    }

    internal static MainWindowViewModel Create(MainWindow mainWindow) {
        return new MainWindowViewModel(mainWindow);
    }
}