namespace Spice86.ViewModels.Messages;
using Spice86.Core.Emulator.InternalDebugger;

internal record RemoveViewModelMessage<T>(T Sender) where T : IInternalDebugger;
