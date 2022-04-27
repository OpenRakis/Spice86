namespace Spice86.Emulator.Devices.Input.Keyboard;

using Avalonia.Input;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public class Keyboard : DefaultIOPortHandler {
    private const int KeyboardIoPort = 0x60;
    private static readonly ILogger _logger = Program.Logger.ForContext<Keyboard>();
    private readonly MainWindowViewModel? _gui;

    public bool IsHardwareQueueEmpty => LastKeyboardInput is null;

    public Keyboard(Machine machine, MainWindowViewModel? gui, Configuration configuration) : base(machine, configuration) {
        _gui = gui;
        if (_gui is not null) {
            _gui.KeyUp += OnKeyUp;
            _gui.KeyDown += OnKeyDown;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e) {
        LastKeyboardInput = new(e.Key, true);
        RaiseAndProcessKeyboardInterruptRequest();
    }

    private void RaiseAndProcessKeyboardInterruptRequest() {
        _machine.Cpu.ExternalInterrupt(9);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) {
        LastKeyboardInput = new(e.Key, false);
        RaiseAndProcessKeyboardInterruptRequest();
    }

    public KeyboardInput? LastKeyboardInput { get; private set; } = null;

    public int LastKeyRepeatCount { get; private set; }

    public byte? GetScanCode() {
        if (_gui == null) {
            return null;
        }
        byte? scancode = null;
        if (LastKeyboardInput is not null) {
            if (LastKeyboardInput.Value.IsPressed == true) {
                scancode = KeyScancodeConverter.GetKeyPressedScancode(LastKeyboardInput.Value.Key);
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key pressed {@KeyCode} scancode {@ScanCode}", LastKeyboardInput.Value.Key, scancode);

                }
            } else {
                scancode = KeyScancodeConverter.GetKeyReleasedScancode(LastKeyboardInput.Value.Key);
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("Getting scancode. Key released {@KeyCode} scancode {@ScanCode}", LastKeyboardInput.Value.Key, scancode);
                }
            }

            if (scancode == null) {
                return null;
            }
        }
        return scancode;
    }

    public override byte ReadByte(int port) {
        byte? scancode = GetScanCode();
        if (scancode == null) {
            return 0;
        }
        return scancode.Value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardIoPort, this);
    }
}