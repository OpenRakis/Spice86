namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

/// <summary>
/// Character devices are things like the console, the printer, the clock, etc.
/// </summary>
public abstract class CharacterDevice : VirtualDeviceBase {
    private string _name = string.Empty;

    /// <summary>
    /// Create a new character device.
    /// </summary>
    /// <param name="attributes">The device attributes.</param>
    /// <param name="name">The name of the device.</param>
    /// <param name="loggerService">The logging service.</param>
    /// <param name="strategy">Optional entrypoint for the strategy routine.</param>
    /// <param name="interrupt">Optional entrypoint for the interrupt routine.</param>
    public CharacterDevice(ILoggerService loggerService,
        DeviceAttributes attributes, string name, ushort strategy = 0,
        ushort interrupt = 0)
        : base(loggerService, attributes, strategy, interrupt) {
        Attributes |= DeviceAttributes.Character;
        _name = name.Length > 8 ? name[..8] : name;
        Logger = loggerService;
    }

    public override string Name { get => _name; set => _name = value; }
}