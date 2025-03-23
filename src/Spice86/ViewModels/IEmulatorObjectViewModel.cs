namespace Spice86.ViewModels;
using System;

public interface IEmulatorObjectViewModel {
    public bool IsVisible { get; set; }
    public void UpdateValues(object? sender, EventArgs e);
}
