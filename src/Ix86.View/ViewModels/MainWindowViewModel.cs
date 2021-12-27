using Ix86.View.Views;

using System;
using System.Collections.Generic;
using System.Text;

namespace Ix86.View.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private MainWindow _mainWindow;

        public MainWindowViewModel(MainWindow window)
        {
            _mainWindow = window;
        }

        public string Greeting => "Welcome to Avalonia!";

        internal static MainWindowViewModel Create(MainWindow mainWindow)
        {
            return new MainWindowViewModel(mainWindow);
        }
    }
}
