using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class Keyboard
    {
        public static bool IsDown(Keycode key)
        {
            return IsDown(GetScancode(key));
        }

        public static bool IsDown(Scancode scancode)
        {
            return GetKeyboardState()[(int)scancode] != 0;
        }

        public static Keymod Modifiers
        {
            get { return SDL_GetModState(); }
            set { SDL_SetModState(value); }
        }

        public static unsafe ReadOnlySpan<byte> GetKeyboardState()
        {
            int length;
            byte* state = SDL_GetKeyboardState(out length);
            return new ReadOnlySpan<byte>(state, length);
        }

        public static unsafe string GetKeyName(Keycode key)
        {
            return UTF8ToString(SDL_GetKeyName(key)) ?? "";
        }

        public static unsafe Keycode GetKeycode(string name)
        {
            Span<byte> buffer = stackalloc byte[SL(name)];
            StringToUTF8(name, buffer);
            Keycode key;
            fixed (byte* ptr = &MemoryMarshal.GetReference(buffer))
                key = SDL_GetKeyFromName(ptr);
            if (key == Keycode.Unknown)
                Throw();
            return key;
        }

        public static Keycode GetKeycode(Scancode scancode)
        {
            return SDL_GetKeyFromScancode(scancode);
        }

        public static unsafe string GetScancodeName(Scancode scancode)
        {
            return UTF8ToString(SDL_GetScancodeName(scancode)) ?? "";
        }

        public static unsafe Scancode GetScancode(string name)
        {
            Span<byte> buffer = stackalloc byte[SL(name)];
            StringToUTF8(name, buffer);
            Scancode scancode;
            fixed (byte* ptr = &MemoryMarshal.GetReference(buffer))
                scancode = SDL_GetScancodeFromName(ptr);
            if (scancode == Scancode.Unknown)
                Throw();
            return scancode;
        }

        public static Scancode GetScancode(Keycode key)
        {
            return SDL_GetScancodeFromKey(key);
        }

        public static void StartTextInput()
          => SDL_StartTextInput();

        public static void StopTextInput()
          => SDL_StopTextInput();

        public static bool IsTextInputActive()
          => SDL_IsTextInputActive() == SDL_Bool.True;

        public static unsafe void SetInputRect(in Rect rect)
        {
            fixed (Rect* ptr = &rect)
                SDL_SetInputRect(ptr);
        }

        public static bool HasScreenKeyboardSupport()
          => SDL_HasScreenKeyboardSupport() == SDL_Bool.True;

        public static bool IsScreenKeyboardShown(Window window)
          => SDL_IsScreenKeyboardShown(window) == SDL_Bool.True;

        public static Window? FocusedWindow()
        {
            IntPtr ptr = SDL_GetKeyboardFocus();
            if (ptr != IntPtr.Zero)
                return new Window(ptr, false);
            else
                return null;
        }

    }
}
