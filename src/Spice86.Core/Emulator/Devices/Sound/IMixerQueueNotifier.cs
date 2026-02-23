namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Interface for audio devices that run on the main thread and use a queue
/// that the mixer thread can block on. Devices implementing this interface
/// are notified before the mixer mutex is acquired (to stop their queues
/// and avoid deadlocks) and after the mutex is released (to restart them).
/// </summary>
public interface IMixerQueueNotifier {
    /// <summary>
    /// Called before the mixer mutex is acquired. Implementations must stop
    /// their output queue so the mixer thread is not blocked waiting on it.
    /// </summary>
    void NotifyLockMixer();

    /// <summary>
    /// Called before the mixer mutex is released. Implementations must
    /// restart their output queue so normal operation can resume.
    /// </summary>
    void NotifyUnlockMixer();
}
