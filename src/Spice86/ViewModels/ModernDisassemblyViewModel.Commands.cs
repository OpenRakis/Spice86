namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Messages;
using Spice86.Models.Debugging;

public partial class ModernDisassemblyViewModel {
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() {
        _messenger.Send(new RemoveViewModelMessage<ModernDisassemblyViewModel>(this));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOver() {
        if (!DebuggerLines.TryGetValue(CurrentInstructionAddress, out DebuggerLineViewModel? debuggerLine)) {
            return;
        }

        uint currentAddress = CurrentInstructionAddress;

        if (!debuggerLine.CanBeSteppedOver) {
            _logger.Debug("Setting unconditional breakpoint for step over");

            _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
                Pause("Step over unconditional breakpoint was reached");
                _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, State.IpPhysicalAddress);
            }, true);

            _logger.Debug("Resuming execution for step over");
            _pauseHandler.Resume();

            return;
        }

        uint nextInstructionAddress = debuggerLine.NextAddress;

        _breakpointsViewModel.AddAddressBreakpoint(nextInstructionAddress, BreakPointType.CPU_EXECUTION_ADDRESS, true, () => {
            Pause($"Step over execution breakpoint was reached at address {nextInstructionAddress}");
            _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, State.IpPhysicalAddress);
        }, "Step over breakpoint");

        _logger.Debug("Resuming execution for step over");
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        _logger.Debug("Setting unconditional breakpoint for step into");

        uint currentAddress = CurrentInstructionAddress;

        _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
            Pause("Step into unconditional breakpoint was reached");
            _logger.Debug("Step into breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, State.IpPhysicalAddress);
        }, true);

        _logger.Debug("Resuming execution for step into");
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void ToggleBreakpoint(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint != null) {
            debuggerLine.Breakpoint.Toggle();
        } else {
            _breakpointsViewModel.AddAddressBreakpoint(debuggerLine.Address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
                PauseAndReportAddress(debuggerLine.Address);
            });
        }
    }

    [RelayCommand]
    private async Task NewDisassemblyView() {
        ModernDisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, State, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, _logger, true) {
            IsPaused = IsPaused
        };
        await Task.Run(() => _messenger.Send(new AddViewModelMessage<ModernDisassemblyViewModel>(disassemblyViewModel)));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task UpdateDisassembly(uint currentInstructionAddress) {
        IsLoading = true;
        Dictionary<uint, EnrichedInstruction> enrichedInstructions = await Task.Run(() => _instructionsDecoder.DecodeInstructionsExtended(currentInstructionAddress, 2048));

        UpdateDebuggerLinesInBatch(enrichedInstructions);

        IsLoading = false;
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CopyLine() {
        if (SelectedDebuggerLine is not null) {
            await _textClipboard.SetTextAsync(SelectedDebuggerLine.ToString());
        }
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
    private void GoToFunction(object? parameter) {
        if (parameter is FunctionInfo functionInfo) {
            _logger.Debug("Go to function: {FunctionName} at address {FunctionAddress:X8}", functionInfo.Name, functionInfo.Address.Linear);

            GoToAddress(functionInfo.Address.Linear);
        }
    }

    [RelayCommand]
    public void GoToAddress(uint? address) {
        _logger.Debug("Go to address: {Address:X8}", address);
        if (address == null) {
            return;
        }
        DebuggerLineViewModel debuggerLine = EnsureAddressIsLoaded(address.Value);
        ScrollToAddress(debuggerLine.Address);
        SelectedDebuggerLine = debuggerLine;
    }

    [RelayCommand]
    private void ScrollToAddress(uint address) {
        CurrentInstructionAddress = address;
        OnPropertyChanged(nameof(CurrentInstructionAddress));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void CreateExecutionBreakpointHere(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint == null) {
            _breakpointsViewModel.AddAddressBreakpoint(debuggerLine.Address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
                PauseAndReportAddress(debuggerLine.Address);
            });
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void RemoveExecutionBreakpointHere(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint != null) {
            _breakpointsViewModel.RemoveBreakpointInternal(debuggerLine.Breakpoint);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void DisableBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Disable();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void EnableBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Enable();
    }
}