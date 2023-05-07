namespace Spice86.Core.Emulator.Sound;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// The NativeMethods class provides access to the winmm.dll library, which provides multimedia services for Windows. It contains several methods for MIDI output management, including opening and closing MIDI output devices, sending short MIDI messages, and resetting a MIDI output device.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods {
    /// <summary>
    /// Opens a MIDI output device and returns a handle to it.
    /// </summary>
    /// <param name="lphmo">A pointer to a handle that will receive the device handle.</param>
    /// <param name="uDeviceID">The device identifier. Set to MIDI_MAPPER to use the default MIDI output device.</param>
    /// <param name="dwCallback">Reserved for future use. Must be set to zero.</param>
    /// <param name="dwCallbackInstance">Reserved for future use. Must be set to zero.</param>
    /// <param name="dwFlags">Set to zero.</param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutOpen(out IntPtr lphmo, uint uDeviceID, IntPtr dwCallback, IntPtr dwCallbackInstance, uint dwFlags);

    /// <summary>
    /// Closes the specified MIDI output device.
    /// </summary>
    /// <param name="hmo">A handle to the MIDI output device.</param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutClose(IntPtr hmo);

    /// <summary>
    /// Sends a short MIDI message to the specified MIDI output device.
    /// </summary>
    /// <param name="hmo">A handle to the MIDI output device.</param>
    /// <param name="dwMsg">The MIDI message to send.</param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutShortMsg(IntPtr hmo, uint dwMsg);

    /// <summary>
    /// Resets the specified MIDI output device.
    /// </summary>
    /// <param name="hmo">A handle to the MIDI output device.</param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutReset(IntPtr hmo);

    /// <summary>
    /// The device identifier for the default MIDI output device.
    /// </summary>
    public const uint MIDI_MAPPER = 0xFFFFFFFF;
}