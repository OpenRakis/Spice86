namespace Spice86.Core.Emulator.Sound.Midi.Windows;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// This <see cref="NativeMethods"/> class provides access to the winmm.dll library, which provides multimedia services for Windows.<br/>
/// It contains several methods for MIDI output management, including opening and closing MIDI output devices, sending short MIDI messages, and resetting a MIDI output device.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods {
    /// <summary>
    /// Opens a MIDI output device for playback.
    /// </summary>
    /// <param name="lphmo">Pointer to an HMIDIOUT handle. This location is filled with a handle identifying the opened MIDI output device. The handle is used to identify the device in calls to other MIDI output functions.</param>
    /// <param name="uDeviceID">Identifier of the MIDI output device that is to be opened.</param>
    /// <param name="dwCallback">Pointer to a callback function, an event handle, a thread identifier, or a handle of a window or thread called during MIDI playback to process messages related to the progress of the playback. If no callback is desired, specify NULL for this parameter. For more information on the callback function, see MidiOutProc.</param>
    /// <param name="dwCallbackInstance">User instance data passed to the callback. This parameter is not used with window callbacks or threads.</param>
    /// <param name="dwFlags">Callback flag for opening the device. Set to 0.</param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutOpen(out IntPtr lphmo, uint uDeviceID, IntPtr dwCallback, IntPtr dwCallbackInstance, uint dwFlags);

    /// <summary>
    /// Closes the specified MIDI output device.
    /// </summary>
    /// <param name="hmo">Handle to the MIDI output device. If the function is successful, the handle is no longer valid after the call to this function.</param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutClose(IntPtr hmo);

    /// <summary>
    /// Sends a short MIDI message to the specified MIDI output device.
    /// </summary>
    /// <param name="hmo">Handle to the MIDI output device. This parameter can also be the handle of a MIDI stream cast to HMIDIOUT.</param>
    /// <param name="dwMsg">
    ///     MIDI message. The message is packed into a DWORD value with the first byte of the message in the low-order byte.
    ///     The message is packed into this parameter as follows (first table)
    ///     <br/>
    ///     <list type="table">
    ///         <item>
    ///             <term>Word</term>
    ///             <description>Byte (usage)</description>
    ///         </item>
    ///         <item>
    ///             <term>High</term>
    ///             <description>High-order (not used)</description>
    ///         </item>
    ///         <item>
    ///             <term></term>
    ///             <description>Low-order (The second byte of MIDI data (when needed))</description>
    ///         </item>
    ///         <item>
    ///             <term>Low</term>
    ///             <description>High-order (The first byte of MIDI data (when needed))</description>
    ///         </item>
    ///         <item>
    ///             <term></term>
    ///             <description>Low-order (The MIDI status)</description>
    ///         </item>
    ///     </list>
    /// <br/>
    /// The two MIDI data bytes are optional, depending on the MIDI status byte. <br/>
    /// When a series of messages have the same status byte, the status byte can be omitted from messages after the first one in the series, creating a running status. <br/>
    /// Pack a message for running status as follows (second table) <br/>
    ///     <list type="table">
    ///         <item>
    ///             <term>Word</term>
    ///             <description>Byte (usage)</description>
    ///         </item>
    ///         <item>
    ///             <term>High</term>
    ///             <description>High-order (not used)</description>
    ///         </item>
    ///         <item>
    ///             <term></term>
    ///             <description>Low-order (not used)</description>
    ///         </item>
    ///         <item>
    ///             <term>Low</term>
    ///             <description>High-order (The second byte of MIDI data (when needed))</description>
    ///         </item>
    ///         <item>
    ///             <term></term>
    ///             <description>Low-order (The first byte of MIDI data)</description>
    ///         </item>
    ///     </list>
    /// </param>
    /// <returns>Returns MMSYSERR_NOERROR if successful or an error code otherwise.</returns>
    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, SetLastError = false)]
    public static extern uint midiOutShortMsg(IntPtr hmo, uint dwMsg);

    /// <summary>
    /// The device identifier for the default MIDI output device.
    /// </summary>
    public const uint MIDI_MAPPER = 0xFFFFFFFF;
}