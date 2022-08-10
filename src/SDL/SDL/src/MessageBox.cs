using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class MessageBox
    {
        public static void ShowError(
            string title,
            string message,
            Window? parent = null)
          => Show(MessageBoxFlags.Error, title, message, parent);

        public static void ShowWarning(
            string title,
            string message,
            Window? parent = null)
          => Show(MessageBoxFlags.Error, title, message, parent);

        public static void ShowInformation(
            string title,
            string message,
            Window? parent = null)
          => Show(MessageBoxFlags.Error, title, message, parent);

        public static int ShowError(
            string title,
            string message,
            IEnumerable<MessageBoxButton> buttons,
            Window? parent = null,
            MessageBoxColors? colorScheme = null)
          => Show(MessageBoxFlags.Error, title, message, buttons, colorScheme, parent);

        public static int ShowWarning(
            string title,
            string message,
            IEnumerable<MessageBoxButton> buttons,
            Window? parent = null,
            MessageBoxColors? colorScheme = null)
          => Show(MessageBoxFlags.Error, title, message, buttons, colorScheme, parent);

        public static int ShowInformation(
            string title,
            string message,
            IEnumerable<MessageBoxButton> buttons,
            Window? parent = null,
            MessageBoxColors? colorScheme = null)
          => Show(MessageBoxFlags.Error, title, message, buttons, colorScheme, parent);


        private static unsafe void Show(MessageBoxFlags flags, string title, string message, Window? parent)
        {
            Span<byte> tbuf = stackalloc byte[SL(title)];
            Span<byte> msgbuf = stackalloc byte[SL(message)];
            StringToUTF8(title, tbuf);
            StringToUTF8(message, msgbuf);
            fixed (byte* t = &MemoryMarshal.GetReference(tbuf))
            fixed (byte* msg = &MemoryMarshal.GetReference(msgbuf))
            {
                if (parent == null)
                    ErrorIfNegative(SDL_ShowSimpleMessageBox((uint)flags, t, msg, IntPtr.Zero));
                else
                    ErrorIfNegative(SDL_ShowSimpleMessageBox((uint)flags, t, msg, parent));
            }
        }

        private static unsafe int Show(
          MessageBoxFlags flags,
          string title,
          string message,
          IEnumerable<MessageBoxButton> buttons,
          MessageBoxColors? colorScheme = null,
          Window? parent = null
          )
        {
            int clicked;

            Span<byte> tbuf = stackalloc byte[SL(title)];
            Span<byte> msgbuf = stackalloc byte[SL(message)];
            StringToUTF8(title, tbuf);
            StringToUTF8(message, msgbuf);
            fixed (byte* t = tbuf)
            fixed (byte* msg = msgbuf)
            {
                Span<SDL_MessageBoxButtonData> btns = new SDL_MessageBoxButtonData[16];
                SDL_MessageBoxData data;
                data.flags = (uint)flags;
                data.window = IntPtr.Zero;
                data.title = t;
                data.message = msg;
                data.numbuttons = 0;
                data.buttons = null;
                data.colorScheme = null;
                try
                {
                    SDL_MessageBoxColorScheme scheme;
                    if (colorScheme != null)
                    {
                        scheme = MarshalColors(colorScheme);
                        data.colorScheme = &scheme;
                    }
                    if (parent != null)
                    {
                        bool ok = false;
                        parent.DangerousAddRef(ref ok);
                        if (ok)
                            data.window = parent.DangerousGetHandle();
                    }
                    data.numbuttons = MarshalButtons(buttons, btns);
                    fixed (SDL_MessageBoxButtonData* bptr = &MemoryMarshal.GetReference(btns))
                    {
                        data.buttons = bptr;
                        ErrorIfNegative(SDL_ShowMessageBox(data, out clicked));
                    }
                }
                finally
                {
                    if (parent != null && data.window != IntPtr.Zero)
                        parent.DangerousRelease();
                    if (data.numbuttons > 0)
                    {
                        for (int i = 0; i < data.numbuttons; ++i)
                        {
                            Marshal.FreeHGlobal((IntPtr)btns[i].text);
                        }
                    }
                }
            }
            return clicked;
        }

        private static unsafe SDL_MessageBoxColorScheme MarshalColors(MessageBoxColors colors)
        {
            SDL_MessageBoxColorScheme scheme;
            int i = 0;
            scheme.bytes[i++] = colors.Background.r;
            scheme.bytes[i++] = colors.Background.g;
            scheme.bytes[i++] = colors.Background.b;
            scheme.bytes[i++] = colors.Text.r;
            scheme.bytes[i++] = colors.Text.g;
            scheme.bytes[i++] = colors.Text.b;
            scheme.bytes[i++] = colors.ButtonBorder.r;
            scheme.bytes[i++] = colors.ButtonBorder.g;
            scheme.bytes[i++] = colors.ButtonBorder.b;
            scheme.bytes[i++] = colors.ButtonBackground.r;
            scheme.bytes[i++] = colors.ButtonBackground.g;
            scheme.bytes[i++] = colors.ButtonBackground.b;
            scheme.bytes[i++] = colors.ButtonSelected.r;
            scheme.bytes[i++] = colors.ButtonSelected.g;
            scheme.bytes[i++] = colors.ButtonSelected.b;
            return scheme;
        }

        private static unsafe int MarshalButtons(
            IEnumerable<MessageBoxButton> buttons,
            Span<SDL_MessageBoxButtonData> output
        )
        {
            int i = 0;
            try
            {
                foreach (MessageBoxButton? button in buttons)
                {
                    int l = SL(button.Text);
                    byte* buf = (byte*)Marshal.AllocHGlobal(l);
                    try
                    {
                        StringToUTF8(button.Text, new Span<byte>(buf, l));
                        output[i].buttonid = button.ID;
                        output[i].flags = (uint)button.Key;
                        output[i].text = buf;
                    }
                    catch
                    {
                        Marshal.FreeHGlobal((IntPtr)buf);
                        throw;
                    }
                    i++;
                }
            }
            catch
            {
                for (int j = 0; j < i; ++i)
                {
                    Marshal.FreeHGlobal((IntPtr)output[j].text);
                }
            }
            return i;
        }
    }

    public class MessageBoxColors
    {
        public Color Background { get; set; }
        public Color Text { get; set; }
        public Color ButtonBorder { get; set; }
        public Color ButtonBackground { get; set; }
        public Color ButtonSelected { get; set; }
    }

    public class MessageBoxButton
    {
        public int ID { get; set; }
        public string Text { get; set; }
        public MessageBoxButtonDefaultKey Key { get; set; }

        public MessageBoxButton(int id, string text)
        {
            this.ID = id;
            this.Text = text;
        }
    }

    public enum MessageBoxButtonDefaultKey
    {
        None,
        Return,
        Escape,
    }
}
