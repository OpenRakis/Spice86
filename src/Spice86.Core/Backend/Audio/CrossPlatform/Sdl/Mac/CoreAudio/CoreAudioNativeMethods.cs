namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Mac.CoreAudio;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// P/Invoke bindings for Apple AudioToolbox (AudioQueue API).
/// Reference: SDL_coreaudio.m uses AudioQueueNewOutput, AudioQueueAllocateBuffer,
/// AudioQueueEnqueueBuffer, AudioQueueStart, AudioQueueStop, AudioQueueFlush,
/// AudioQueueDispose, AudioQueueSetProperty, and CoreFoundation's CFRunLoop.
/// 
/// These are macOS-only APIs from AudioToolbox.framework and CoreFoundation.framework.
/// </summary>
internal static class CoreAudioNativeMethods {
    private const string AudioToolboxLib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // OSStatus is Int32
    // noErr = 0

    /// <summary>
    /// AudioStreamBasicDescription structure.
    /// Reference: CoreAudioTypes.h
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription {
        /// <summary>Sample rate in Hz.</summary>
        public double SampleRate;

        /// <summary>Audio format ID (e.g., kAudioFormatLinearPCM).</summary>
        public uint FormatId;

        /// <summary>Format flags (e.g., kLinearPCMFormatFlagIsFloat | kLinearPCMFormatFlagIsPacked).</summary>
        public uint FormatFlags;

        /// <summary>Bytes per packet.</summary>
        public uint BytesPerPacket;

        /// <summary>Frames per packet (1 for PCM).</summary>
        public uint FramesPerPacket;

        /// <summary>Bytes per frame.</summary>
        public uint BytesPerFrame;

        /// <summary>Channels per frame.</summary>
        public uint ChannelsPerFrame;

        /// <summary>Bits per channel.</summary>
        public uint BitsPerChannel;

        /// <summary>Reserved, must be 0.</summary>
        public uint Reserved;
    }

    /// <summary>
    /// AudioQueueBuffer structure.
    /// Reference: AudioQueue.h
    /// The native struct has more fields but we only need AudioData and the byte sizes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioQueueBuffer {
        /// <summary>The size in bytes of the allocated buffer data.</summary>
        public uint AudioDataBytesCapacity;

        /// <summary>Pointer to the audio data buffer.</summary>
        public IntPtr AudioData;

        /// <summary>The number of bytes of valid audio data in the buffer.</summary>
        public uint AudioDataByteSize;

        /// <summary>User data pointer.</summary>
        public IntPtr UserData;

        /// <summary>Number of packet descriptions (for VBR data).</summary>
        public uint PacketDescriptionCapacity;

        /// <summary>Pointer to packet descriptions.</summary>
        public IntPtr PacketDescriptions;

        /// <summary>Number of valid packet descriptions.</summary>
        public uint PacketDescriptionCount;
    }

    /// <summary>
    /// AudioChannelLayout structure (simplified - only the tag is used).
    /// Reference: CoreAudioTypes.h
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioChannelLayout {
        /// <summary>Channel layout tag.</summary>
        public uint ChannelLayoutTag;

        /// <summary>Channel bitmap.</summary>
        public uint ChannelBitmap;

        /// <summary>Number of channel descriptions.</summary>
        public uint NumberChannelDescriptions;

        // AudioChannelDescription array follows (variable length)
        // We don't need it for our use case
    }

    /// <summary>
    /// AudioQueue output callback delegate.
    /// Reference: AudioQueue.h AudioQueueOutputCallback
    /// Called by CoreAudio when a buffer has been consumed and needs refilling.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AudioQueueOutputCallback(
        IntPtr inUserData,
        IntPtr inAudioQueue,
        IntPtr inBuffer);

    // ========================================================================
    // AudioQueue API
    // ========================================================================

    /// <summary>
    /// Creates a new output audio queue.
    /// Reference: AudioQueueNewOutput from AudioQueue.h
    /// SDL_coreaudio.m: prepare_audioqueue line ~908
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueNewOutput")]
    internal static extern int AudioQueueNewOutput(
        ref AudioStreamBasicDescription inFormat,
        AudioQueueOutputCallback inCallbackProc,
        IntPtr inUserData,
        IntPtr inCallbackRunLoop,
        IntPtr inCallbackRunLoopMode,
        uint inFlags,
        out IntPtr outAQ);

    /// <summary>
    /// Allocates an audio queue buffer.
    /// Reference: AudioQueueAllocateBuffer from AudioQueue.h
    /// SDL_coreaudio.m: prepare_audioqueue line ~965
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueAllocateBuffer")]
    internal static extern int AudioQueueAllocateBuffer(
        IntPtr inAQ,
        uint inBufferByteSize,
        out IntPtr outBuffer);

    /// <summary>
    /// Enqueues a buffer for playback.
    /// Reference: AudioQueueEnqueueBuffer from AudioQueue.h
    /// SDL_coreaudio.m: outputCallback line ~489 and prepare_audioqueue line ~969
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueEnqueueBuffer")]
    internal static extern int AudioQueueEnqueueBuffer(
        IntPtr inAQ,
        IntPtr inBuffer,
        uint inNumPacketDescs,
        IntPtr inPacketDescs);

    /// <summary>
    /// Starts the audio queue.
    /// Reference: AudioQueueStart from AudioQueue.h
    /// SDL_coreaudio.m: prepare_audioqueue line ~972
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueStart")]
    internal static extern int AudioQueueStart(
        IntPtr inAQ,
        IntPtr inStartTime);

    /// <summary>
    /// Stops the audio queue.
    /// Reference: AudioQueueStop from AudioQueue.h
    /// SDL_coreaudio.m: COREAUDIO_CloseDevice line ~683
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueStop")]
    internal static extern int AudioQueueStop(
        IntPtr inAQ,
        byte inImmediate);

    /// <summary>
    /// Flushes the audio queue.
    /// Reference: AudioQueueFlush from AudioQueue.h
    /// SDL_coreaudio.m: COREAUDIO_CloseDevice line ~682
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueFlush")]
    internal static extern int AudioQueueFlush(IntPtr inAQ);

    /// <summary>
    /// Disposes the audio queue.
    /// Reference: AudioQueueDispose from AudioQueue.h
    /// SDL_coreaudio.m: COREAUDIO_CloseDevice line ~683
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueDispose")]
    internal static extern int AudioQueueDispose(
        IntPtr inAQ,
        byte inImmediate);

    /// <summary>
    /// Sets a property on the audio queue.
    /// Reference: AudioQueueSetProperty from AudioQueue.h
    /// SDL_coreaudio.m: prepare_audioqueue line ~930 (channel layout)
    /// </summary>
    [DllImport(AudioToolboxLib, EntryPoint = "AudioQueueSetProperty")]
    internal static extern int AudioQueueSetProperty(
        IntPtr inAQ,
        uint inID,
        IntPtr inData,
        uint inDataSize);

    // ========================================================================
    // CoreFoundation RunLoop API
    // Used by the audioqueue_thread to run the AudioQueue callbacks.
    // Reference: SDL_coreaudio.m audioqueue_thread line ~1015
    // ========================================================================

    /// <summary>
    /// Gets the current thread's run loop.
    /// Reference: CFRunLoopGetCurrent from CFRunLoop.h
    /// </summary>
    [DllImport(CoreFoundationLib, EntryPoint = "CFRunLoopGetCurrent")]
    internal static extern IntPtr CFRunLoopGetCurrent();

    /// <summary>
    /// Runs the run loop in a specific mode for a given duration.
    /// Reference: CFRunLoopRunInMode from CFRunLoop.h
    /// SDL_coreaudio.m: audioqueue_thread line ~1028
    /// </summary>
    [DllImport(CoreFoundationLib, EntryPoint = "CFRunLoopRunInMode")]
    internal static extern int CFRunLoopRunInMode(
        IntPtr mode,
        double seconds,
        byte returnAfterSourceHandled);

    /// <summary>
    /// Gets the kCFRunLoopDefaultMode constant string.
    /// This is a CFStringRef constant.
    /// </summary>
    [DllImport(CoreFoundationLib, EntryPoint = "CFRunLoopGetMain")]
    internal static extern IntPtr CFRunLoopGetMain();

    // ========================================================================
    // CoreFoundation String Constants
    // kCFRunLoopDefaultMode is a global CFStringRef
    // ========================================================================

    /// <summary>
    /// Get kCFRunLoopDefaultMode - we need to load this from the framework.
    /// </summary>
    internal static IntPtr GetDefaultRunLoopMode() {
        IntPtr lib = NativeLibrary.Load(CoreFoundationLib);
        IntPtr symbolAddr = NativeLibrary.GetExport(lib, "kCFRunLoopDefaultMode");
        return Marshal.ReadIntPtr(symbolAddr);
    }
}

/// <summary>
/// CoreAudio / AudioToolbox constants.
/// Reference: CoreAudioTypes.h, AudioQueue.h
/// </summary>
internal static class CoreAudioConstants {
    // noErr
    internal const int NoErr = 0;

    // kAudioFormatLinearPCM
    internal const uint AudioFormatLinearPcm = 0x6C70636D; // 'lpcm'

    // Format flags
    // Reference: CoreAudioTypes.h kLinearPCMFormatFlagIsFloat, kLinearPCMFormatFlagIsPacked
    internal const uint LinearPcmFormatFlagIsFloat = 1 << 0;        // kAudioFormatFlagIsFloat
    internal const uint LinearPcmFormatFlagIsBigEndian = 1 << 1;    // kAudioFormatFlagIsBigEndian
    internal const uint LinearPcmFormatFlagIsSignedInteger = 1 << 2; // kAudioFormatFlagIsSignedInteger
    internal const uint LinearPcmFormatFlagIsPacked = 1 << 3;       // kAudioFormatFlagIsPacked

    // AudioQueue property IDs
    // kAudioQueueProperty_ChannelLayout
    internal const uint AudioQueuePropertyChannelLayout = 0x61716368; // 'aqch'

    // Channel layout tags
    // Reference: CoreAudioTypes.h
    internal const uint AudioChannelLayoutTagMono = (100 << 16) | 1;          // kAudioChannelLayoutTag_Mono
    internal const uint AudioChannelLayoutTagStereo = (101 << 16) | 2;        // kAudioChannelLayoutTag_Stereo
    internal const uint AudioChannelLayoutTagDvd4 = (134 << 16) | 3;          // kAudioChannelLayoutTag_DVD_4 (L R LFE)
    internal const uint AudioChannelLayoutTagQuadraphonic = (108 << 16) | 4;  // kAudioChannelLayoutTag_Quadraphonic
    internal const uint AudioChannelLayoutTagDvd6 = (136 << 16) | 5;          // kAudioChannelLayoutTag_DVD_6 (L R LFE Ls Rs)
    internal const uint AudioChannelLayoutTagDvd12 = (142 << 16) | 6;         // kAudioChannelLayoutTag_DVD_12 (L R C LFE Ls Rs)

    // Minimum audio buffer time in ms
    // Reference: SDL_coreaudio.m prepare_audioqueue line ~958
    internal const double MinimumAudioBufferTimeMs = 15.0;
}
