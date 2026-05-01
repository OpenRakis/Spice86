// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

using Avalonia.Input;
using Avalonia.Input.Platform;
using System.Reflection;

namespace Avalonia.Controls.Utils
{
    internal static class KeyboardHelper
    {
        public static void GetMetaKeyState(Control target, KeyModifiers modifiers, out bool ctrlOrCmd, out bool shift)
        {
            ctrlOrCmd = modifiers.HasFlag(GetPlatformCtrlOrCmdKeyModifier(target));
            shift = modifiers.HasFlag(KeyModifiers.Shift);
        }

        public static void GetMetaKeyState(Control target, KeyModifiers modifiers, out bool ctrlOrCmd, out bool shift, out bool alt)
        {
            ctrlOrCmd = modifiers.HasFlag(GetPlatformCtrlOrCmdKeyModifier(target));
            shift = modifiers.HasFlag(KeyModifiers.Shift);
            alt = modifiers.HasFlag(KeyModifiers.Alt);
        }

        public static KeyModifiers GetPlatformCtrlOrCmdKeyModifier(Control target)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(target);
            if (topLevel == null)
            {
                return KeyModifiers.Control;
            }

            PropertyInfo? platformSettingsProperty = topLevel.GetType().GetProperty("PlatformSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? platformSettings = platformSettingsProperty?.GetValue(topLevel);
            if (platformSettings == null)
            {
                return KeyModifiers.Control;
            }

            PropertyInfo? hotkeyConfigurationProperty = platformSettings.GetType().GetProperty("HotkeyConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? hotkeyConfiguration = hotkeyConfigurationProperty?.GetValue(platformSettings);
            if (hotkeyConfiguration == null)
            {
                return KeyModifiers.Control;
            }

            PropertyInfo? commandModifiersProperty = hotkeyConfiguration.GetType().GetProperty(nameof(PlatformHotkeyConfiguration.CommandModifiers), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? commandModifiers = commandModifiersProperty?.GetValue(hotkeyConfiguration);
            return commandModifiers is KeyModifiers keyModifiers ? keyModifiers : KeyModifiers.Control;
        }
    }
}
