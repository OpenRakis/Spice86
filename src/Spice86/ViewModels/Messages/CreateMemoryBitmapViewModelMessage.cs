namespace Spice86.ViewModels.Messages;

using System;

public record CreateMemoryBitmapViewModelMessage(Action<MemoryBitmapViewModel> SetInstance);