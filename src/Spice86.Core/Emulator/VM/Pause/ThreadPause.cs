namespace Spice86.Core.Emulator.VM.Pause;
internal static class ThreadPause {
    internal static void SleepWhilePaused(IPauseable pauseable) {
        while (pauseable.IsPaused) {
            Thread.Sleep(1);
        }
    }
}
