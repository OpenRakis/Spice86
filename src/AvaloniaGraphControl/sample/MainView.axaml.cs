using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AvaloniaGraphControlSample;

public partial class MainView : UserControl
{
  public MainView()
  {
    InitializeComponent();
    zoomBorderHelper = new ZoomBorderHelper(this.Find<ZoomBorder>("ZoomBorder")!);
    SelectIndexOnContentLoad("Graphs", 0);
    SelectIndexOnContentLoad("LayoutMethods", 0);
  }

  private void SelectIndexOnContentLoad(string name, int index)
  {
    this.FindControl<SelectingItemsControl>(name)!.PropertyChanged += (sender, args) =>
    {
      if (args.Property == ItemsControl.ItemCountProperty)
        (sender as SelectingItemsControl)!.SelectedIndex = index;
    };
  }

  private readonly ZoomBorderHelper zoomBorderHelper;
  
  private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

  private void ResetDisplay(object? sender, RoutedEventArgs e) => zoomBorderHelper.Reset();
}

