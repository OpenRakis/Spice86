namespace Bufdio.Spice86.Utilities;

using System.Runtime.InteropServices;

/// <summary>
/// Provides platform detection utilities.
/// </summary>
internal static class PlatformInfo {
    /// <summary>
    /// Gets a value indicating whether the current platform is Windows.
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Gets a value indicating whether the current platform is Linux.
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Gets a value indicating whether the current platform is macOS.
    /// </summary>
    public static bool IsOSX => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}