namespace Spice86.ViewModels.Messages;
using Spice86.Core.Emulator.InternalDebugger;

internal record AddViewModelMessage<T>() where T : IInternalDebugger;
