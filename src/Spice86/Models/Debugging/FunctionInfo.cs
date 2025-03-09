namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Shared.Emulator.Memory;

public partial class FunctionInfo : ObservableObject {
    [ObservableProperty] private string? _name;
    [ObservableProperty] private SegmentedAddress _address;

    public override string ToString() {
        return $"{Address}: {Name}";
    }
}
