namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Serilog.Events;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Messages;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;

public partial class DisassemblyViewModel {
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() {
        _messenger.Send(new RemoveViewModelMessage<DisassemblyViewModel>(this));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOver() {
        SegmentedAddress currentAddress = State.IpSegmentedAddress;
        DebuggerLineViewModel debuggerLine = EnsureAddressIsLoaded(currentAddress);

        if (!debuggerLine.CanBeSteppedOver) {
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("Setting unconditional breakpoint for step over");
            }

            _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
                Pause("Step over unconditional breakpoint was reached");
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, State.IpPhysicalAddress);
                }
            }, true);

            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("Resuming execution for step over");
            }
            _pauseHandler.Resume();

            return;
        }

        uint nextInstructionAddress = debuggerLine.NextAddress;

        _breakpointsViewModel.AddAddressBreakpoint(nextInstructionAddress, BreakPointType.CPU_EXECUTION_ADDRESS, true, () => {
            Pause($"Step over execution breakpoint was reached");
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress}, New address: {StateCsIp}", currentAddress, State.IpSegmentedAddress);
            }
        }, "Step over breakpoint");

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Resuming execution for step over");
        }
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Setting unconditional breakpoint for step into");
        }

        SegmentedAddress? currentAddress = State.IpSegmentedAddress;

        _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
            Pause("Step into unconditional breakpoint was reached");
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("Step into breakpoint reached. Previous address: {CurrentAddress}, New address: {StateCsIp}", currentAddress, State.IpSegmentedAddress);
            }
        }, true);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Resuming execution for step into");
        }
        _pauseHandler.Resume();
    }

    [RelayCommand]
    private void ToggleBreakpoint(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint != null) {
            debuggerLine.Breakpoint.Toggle();
        } else {
            _breakpointsViewModel.AddAddressBreakpoint(debuggerLine.Address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
                Pause($"Execution breakpoint was reached at address {debuggerLine.SegmentedAddress}.");
            });
        }
    }

    [RelayCommand]
    private void NewDisassemblyView() {
        DisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, State, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, _logger, true) {
            IsPaused = IsPaused
        };
        _messenger.Send(new AddViewModelMessage<DisassemblyViewModel>(disassemblyViewModel));
    }

    [RelayCommand]
    private void MoveCsIpHere() {
        if (SelectedDebuggerLine is null) {
            return;
        }

        State.CS = SelectedDebuggerLine.SegmentedAddress.Segment;
        State.IP = SelectedDebuggerLine.SegmentedAddress.Offset;

        _pauseHandler.Resume();
    }

    [RelayCommand]
    private void GoToFunction(FunctionInfo functionInfo) {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Go to function: {FunctionName} at address {FunctionAddress:X8}", functionInfo.Name, functionInfo.Address.Linear);
        }
        GoToAddress(functionInfo.Address);
    }

    [RelayCommand]
    private void GoToCsIp() {
        GoToAddress(State.IpSegmentedAddress);
    }

    [RelayCommand]
    public void GoToAddress(SegmentedAddress? address) {
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Go to address: {Address}", address);
        }
        if (address == null) {
            return;
        }
        DebuggerLineViewModel debuggerLine = EnsureAddressIsLoaded(address.Value);
        ScrollToAddress(debuggerLine.SegmentedAddress);
        SelectedDebuggerLine = debuggerLine;
    }

    private void ScrollToAddress(SegmentedAddress address) {
        CurrentInstructionAddress = address;
        OnPropertyChanged(nameof(CurrentInstructionAddress));
    }

    [RelayCommand]
    private void CreateExecutionBreakpointHere(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint == null) {
            _breakpointsViewModel.AddAddressBreakpoint(debuggerLine.Address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
                Pause($"Execution breakpoint was reached at address {debuggerLine.SegmentedAddress}.");
            });
        }
    }

    [RelayCommand]
    private void RemoveExecutionBreakpointHere(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint != null) {
            _breakpointsViewModel.RemoveBreakpointInternal(debuggerLine.Breakpoint);
        }
    }

    [RelayCommand]
    private void DisableBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Disable();
    }

    [RelayCommand]
    private void EnableBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Enable();
    }
}