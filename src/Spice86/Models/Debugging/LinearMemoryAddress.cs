﻿namespace Spice86.Models.Debugging;

using Spice86.Core.Emulator.Memory;

using System.ComponentModel.DataAnnotations;

public readonly record struct LinearMemoryAddress {
    private readonly string? _sourceInput;
    public LinearMemoryAddress([Range(0, A20Gate.EndOfHighMemoryArea)] uint address, string? sourceInput = null) {
        Address = address;
        _sourceInput = sourceInput;
    }

    [Range(0, A20Gate.EndOfHighMemoryArea)]
    public uint Address { get; init; }

    /// <summary>
    /// Returns the hexadecimal representation of the address.
    /// </summary>
    /// <returns>The hexadecimal representation of the address.</returns>
    public override string ToString() {
        return string.IsNullOrWhiteSpace(_sourceInput) ? $"0x{Address:X}" : _sourceInput;
    }

    public static implicit operator uint(LinearMemoryAddress address) => address.Address;

    public static implicit operator LinearMemoryAddress(uint address) => new(address);
}
