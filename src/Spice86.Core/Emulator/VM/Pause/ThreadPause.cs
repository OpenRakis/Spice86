namespace Spice86.Core.Emulator.VM.Pause;
internal static class ThreadPause {
    /// <summary>
    /// Makes the <see cref="Thread"/> sleep for 1ms repeatedly while <see cref="IPauseable.IsPaused"/> is <c>true</c>.
    /// </summary>
    /// <param name="pauseable"></param>
    internal static void SleepWhilePaused(IPauseable pauseable) {
        while (pauseable.IsPaused) {
            Thread.Sleep(1);
        }
    }
}
