namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spice86.ViewModels;

public partial class CfgCpuView : UserControl
{
    public CfgCpuView()
    {
        InitializeComponent();
    }
    
    private void OnAutoCompleteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CfgCpuViewModel viewModel)
        {
            viewModel.NavigateToSelectedNodeCommand.Execute(null);
            e.Handled = true;
        }
    }
}
