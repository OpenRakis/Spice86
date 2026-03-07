namespace Bufdio.Spice86.Utilities;

using System.Runtime.InteropServices;

internal static class PlatformInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsOSX => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
