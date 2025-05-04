namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

internal sealed partial class SplashWindowViewModel : ObservableObject {

    [ObservableProperty]
    private string _splashText = "Loading Spice86...";

    [ObservableProperty]
    private int _progress = 0;
}
