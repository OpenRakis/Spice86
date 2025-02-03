﻿namespace Spice86.Core.Emulator.Devices.Input.Joystick;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

/// <summary>
/// Joystick implementation. Emulates an unplugged joystick for now.
/// </summary>
public class Joystick : DefaultIOPortHandler {
    private const int JoystickPositionAndStatus = 0x201;

    private byte _joystickPositionAndStatusValue = 0xFF;

    /// <summary>
    /// Initializes a new instance of the <see cref="Joystick"/>
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher"></param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Joystick(State state, IOPortDispatcher ioPortDispatcher, bool failOnUnhandledPort,
        ILoggerService loggerService) : base(state, failOnUnhandledPort, loggerService) {
        InitPortHandlers(ioPortDispatcher);
    }

   private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(JoystickPositionAndStatus, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        return port switch {
            JoystickPositionAndStatus => _joystickPositionAndStatusValue,
            _ => base.ReadByte(port),
        };
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case JoystickPositionAndStatus:
                _joystickPositionAndStatusValue = value;
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }
}