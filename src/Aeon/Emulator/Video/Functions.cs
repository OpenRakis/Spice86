namespace Aeon.Emulator.Video
{
    internal static class Functions
    {
        public const byte SetVideoMode = 0x00;
        public const byte SetCursorType = 0x01;
        public const byte SetCursorPosition = 0x02;
        public const byte GetCursorPosition = 0x03;
        public const byte ReadLightPen = 0x04;
        public const byte SelectActiveDisplayPage = 0x05;
        public const byte ScrollActivePageUp = 0x06;
        public const byte ScrollActivePageDown = 0x07;
        public const byte ReadCharacterAndAttributeAtCursor = 0x08;
        public const byte WriteCharacterAndAttributeAtCursor = 0x09;
        public const byte WriteCharacterAtCursor = 0x0A;
        public const byte Video = 0x0B;
        public const byte Video_SetBackgroundColor = 0x00;
        public const byte Video_SetPalette = 0x01;
        public const byte WriteGraphicsPixelAtCoordinate = 0x0C;
        public const byte ReadGraphicsPixelAtCoordinate = 0x0D;
        public const byte WriteTextInTeletypeMode = 0x0E;
        public const byte GetVideoMode = 0x0F;
        public const byte Palette = 0x10;
        public const byte Palette_SetSingleRegister = 0x00;
        public const byte Palette_SetBorderColor = 0x01;
        public const byte Palette_SetAllRegisters = 0x02;
        public const byte Palette_ToggleBlink = 0x03;
        public const byte Palette_SetSingleDacRegister = 0x10;
        public const byte Palette_SetDacRegisters = 0x12;
        public const byte Palette_SelectDacColorPage = 0x13;
        public const byte Palette_ReadSingleDacRegister = 0x15;
        public const byte Palette_ReadDacRegisters = 0x17;
        public const byte Font = 0x11;
        public const byte Font_Load8x8 = 0x12;
        public const byte Font_Load8x16 = 0x14;
        public const byte Font_GetFontInfo = 0x30;
        public const byte EGA = 0x12;
        public const byte EGA_GetInfo = 0x10;
        public const byte EGA_SelectVerticalResolution = 0x30;
        public const byte EGA_PaletteLoading = 0x31;
        public const byte GetDisplayCombinationCode = 0x1A;
        public const byte GetFunctionalityInfo = 0x1B;
    }
}
