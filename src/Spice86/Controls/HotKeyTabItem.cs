namespace Spice86.Controls;

using Avalonia.Controls;
using Avalonia.Input;

using System.Windows.Input;

/// <summary>
/// A TabItem with a hot key.
/// <remarks>Source is: https://github.com/AvaloniaUI/Avalonia/discussions/14836</remarks>
/// </summary>
public class HotKeyTabItem : TabItem, ICommandSource {
    protected override Type StyleKeyOverride => typeof(TabItem);

    public HotKeyTabItem() {
        Command = new TabItemSelectCommand(this);
        CommandParameter = null;
    }

    public void CanExecuteChanged(object sender, EventArgs e) {
    }

    public ICommand? Command { get; }
    public object? CommandParameter { get; }

    public class TabItemSelectCommand : ICommand {
        private readonly TabItem _tabItem;

        public TabItemSelectCommand(TabItem tabItem) {
            _tabItem = tabItem;
        }

        public bool CanExecute(object? parameter) {
            return _tabItem.IsEffectivelyEnabled;
        }

        public void Execute(object? parameter) {
            _tabItem.IsSelected = true;
        }

        public event EventHandler? CanExecuteChanged;
    }
}