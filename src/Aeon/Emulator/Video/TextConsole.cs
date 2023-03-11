using Aeon.Emulator.Video.Modes;

using System.Text;

namespace Aeon.Emulator.Video
{
    /// <summary>
    /// Defines methods and properties for an emulated text console.
    /// </summary>
    public sealed class TextConsole
    {
        private const byte ansiEscape = 0x1B;
        private readonly StringBuilder ansiCommand = new();
        private Point savedPosition = new(0, 24);
        private readonly IAeonVgaCard video;
        private bool boldEnabled;
        private bool negativeEnabled;
        private byte _screenColumns;
        private byte _screenRows;

        /// <summary>
        /// Initializes a new instance of the TextConsole class.
        /// </summary>
        /// <param name="video">Current VideoHandler instance.</param>
        /// <param name="screenColumns"></param>
        /// <param name="screenRows"></param>
        public TextConsole(IAeonVgaCard video, int screenColumns, int screenRows)
        {
            this.video = video;
            _screenColumns = (byte)screenColumns;
            _screenRows = (byte)screenRows;

            AnsiEnabled = true;
            Width = 80;
            Height = 25;
            ForegroundColor = 7;
        }

        /// <summary>
        /// Gets or sets a value indicating whether ANSI control codes are processed.
        /// </summary>
        public bool AnsiEnabled { get; set; }
        /// <summary>
        /// Gets or sets the position of the cursor in the console.
        /// </summary>
        public Point CursorPosition { get; set; }
        /// <summary>
        /// Gets the width of the console.
        /// </summary>
        public int Width
        {
            get => _screenColumns;
            set => _screenColumns = (byte)value;
        }
        /// <summary>
        /// Gets the height of the console.
        /// </summary>
        public int Height
        {
            get => _screenRows + 1;
            set => _screenRows = (byte)(value - 1);
        }
        /// <summary>
        /// Gets or sets the current foreground color of the console.
        /// </summary>
        public byte ForegroundColor { get; set; }
        /// <summary>
        /// Gets or sets the current background color of the console.
        /// </summary>
        public byte BackgroundColor { get; set; }

        /// <summary>
        /// Clears the console and moves the cursor to the top left corner.
        /// </summary>
        public void Clear()
        {
            if (video.CurrentMode is TextMode mode)
                mode.Clear();

            CursorPosition = new Point(0, 0);
        }
        /// <summary>
        /// Clears a rectangle in the console and does not move the cursor.
        /// </summary>
        /// <param name="offset">Top left corner of the rectangle to clear.</param>
        /// <param name="width">Width of the rectangle to clear.</param>
        /// <param name="height">Height of the rectangle to clear.</param>
        public void Clear(Point offset, int width, int height)
        {
            if (video.CurrentMode is TextMode mode)
                mode.Clear(offset, width, height);
        }
        /// <summary>
        /// Copies a block of text in the console from one location to another
        /// and clears the source rectangle.
        /// </summary>
        /// <param name="sourceOffset">Top left corner of source rectangle to copy.</param>
        /// <param name="destinationOffset">Top left corner of destination rectangle to copy to.</param>
        /// <param name="width">Width of rectangle to copy.</param>
        /// <param name="height">Height of rectangle to copy.</param>
        public void MoveBlock(Point sourceOffset, Point destinationOffset, int width, int height, ushort background)
        {
            if (video.CurrentMode is TextMode mode)
                mode.MoveBlock(sourceOffset, destinationOffset, width, height, 0, 0);
        }
        /// <summary>
        /// Writes a byte to the console.
        /// </summary>
        /// <param name="b">Byte to write to the console.</param>
        public void Write(byte b)
        {
            if (AnsiEnabled && (b == ansiEscape || ansiCommand.Length > 0))
                WriteAnsiCommand((char)b);
            else
                WriteByte(b, ForegroundColor, BackgroundColor, true);
        }
        public void Write(byte b, byte foreground, byte background, bool advanceCursor) => WriteByte(b, foreground, background, advanceCursor);
        /// <summary>
        /// Writes a string to the console.
        /// </summary>
        /// <param name="s">String to write to the console.</param>
        public void Write(string s)
        {
            if (AnsiEnabled)
            {
                foreach (char c in s)
                    Write((byte)c);
            }
            else
            {
                WriteString(s, ForegroundColor, BackgroundColor, true);
            }
        }
        /// <summary>
        /// Scrolls lines of text up in a rectangle.
        /// </summary>
        /// <param name="x1">Left coordinate of scroll region.</param>
        /// <param name="y1">Top coordinate of scroll region.</param>
        /// <param name="x2">Right coordinate of scroll region.</param>
        /// <param name="y2">Bottom coordinate of scroll region.</param>
        /// <param name="lines">Number of lines to scroll.</param>
        /// <param name="background">Background color to fill in bottom rows.</param>
        /// <param name="foreground">Foreground color to fill in bottom rows.</param>
        public void ScrollTextUp(int x1, int y1, int x2, int y2, int lines, byte foreground, byte background)
        {
            if (video.CurrentMode is TextMode mode)
                mode.ScrollUp(x1, y1, x2, y2, lines, (byte)(foreground | (background << 4)));
        }
        /// <summary>
        /// Returns the character at the specified coordinates.
        /// </summary>
        /// <param name="x">Horizontal character coordinate.</param>
        /// <param name="y">Vertical character coordinate.</param>
        /// <returns>Character and attribute at this specified position.</returns>
        public ushort GetCharacter(int x, int y) {
            if (video.CurrentMode is TextMode mode)
                return mode.GetCharacter(x, y);
            return 0;
        }
        public void SetCursorPosition(int offset)
        {
            var p = new Point(offset % Width, offset / Width);
            if (p != CursorPosition)
                CursorPosition = p;
        }

        /// <summary>
        /// Writes a byte to the console.
        /// </summary>
        /// <param name="b">Byte to write to the console.</param>
        /// <param name="foreground">Foreground color of byte to write.</param>
        /// <param name="background">Background color of byte to write.</param>
        /// <param name="advanceCursor">Value indicating whether the cursor should be automatically advanced after writing the character.</param>
        private void WriteByte(byte b, byte foreground, byte background, bool advanceCursor) => WriteCharacter((char)b, foreground, background, advanceCursor);
        /// <summary>
        /// Writes a string to the console.
        /// </summary>
        /// <param name="s">String to write to the console.</param>
        /// <param name="foreground">Foreground color of string to write.</param>
        /// <param name="background">Background color of string to write.</param>
        /// <param name="advanceCursor">Value indicating whether the cursor should be automatically advanced after writing the character.</param>
        private void WriteString(string s, byte foreground, byte background, bool advanceCursor)
        {
            foreach (char c in s)
                WriteCharacter(c, foreground, background, advanceCursor);
        }
        /// <summary>
        /// Writes a character to the console performing bounds checking and
        /// moving the cursor as necessary.
        /// </summary>
        /// <param name="c">Character to write to the console.</param>
        /// <param name="foreground">Foreground color of character to write.</param>
        /// <param name="background">Background color of character to write.</param>
        /// <param name="advanceCursor">Value indicating whether the cursor should be automatically advanced after writing the character.</param>
        private void WriteCharacter(char c, byte foreground, byte background, bool advanceCursor)
        {
            var cursorPos = CursorPosition;
            if (cursorPos.X >= 0 && cursorPos.X < Width && cursorPos.Y >= 0 && cursorPos.Y < Height)
            {
                if (c == '\n')
                {
                    if (cursorPos.Y < Height - 1)
                    {
                        cursorPos.X = 0;
                        cursorPos.Y++;
                    }
                    else if (cursorPos.Y == Height - 1)
                    {
                        cursorPos.X = 0;
                        MoveBlock(new Point(0, 1), new Point(0, 0), Width, Height - 1, 0);
                    }
                }
                else if (c == '\r')
                {
                    cursorPos.X = 0;
                }
                else
                {
                    if (c == 8 && advanceCursor)
                    {
                        if (cursorPos.X > 0)
                            cursorPos.X--;

                        SendToProvider(cursorPos.X, cursorPos.Y, 0, foreground, background);
                    }
                    else
                    {
                        SendToProvider(cursorPos.X, cursorPos.Y, c, foreground, background);

                        if (cursorPos.X < Width - 1)
                        {
                            cursorPos.X++;
                        }
                        else if (cursorPos.Y < Height - 1)
                        {
                            cursorPos.X = 0;
                            cursorPos.Y++;
                        }
                    }
                }

                if (advanceCursor)
                    CursorPosition = cursorPos;
            }
        }

        private void SendToProvider(int x, int y, int index, byte foreground, byte background) => video.CurrentMode?.WriteCharacter(x, y, index, foreground, background);
        private void WriteAnsiCommand(char c)
        {
            ansiCommand.Append(c);

            // Make sure the second character coming in is the second byte of the escape sequence.
            if (ansiCommand.Length == 2)
            {
                if (c != '[')
                {
                    WriteString(ansiCommand.ToString(), ForegroundColor, BackgroundColor, true);
                    ansiCommand.Length = 0;
                }
            }
            else
            {
                if (c >= 0x40 && c <= 0x7E)
                    ParseAnsiCommand();
            }
        }
        private void ParseAnsiCommand()
        {
            string commandBody = ansiCommand.ToString(2, ansiCommand.Length - 2).Trim();
            char commandCode = commandBody[^1];

            commandBody = commandBody[..^1];
            string[]? commandArgs = null;
            if (commandBody.Length > 0)
                commandArgs = commandBody.Split(';');

            switch (commandCode)
            {
                case 'A':
                    AnsiCursorMove(commandArgs, Direction.Up);
                    break;

                case 'B':
                    AnsiCursorMove(commandArgs, Direction.Down);
                    break;

                case 'C':
                    AnsiCursorMove(commandArgs, Direction.Right);
                    break;

                case 'D':
                    AnsiCursorMove(commandArgs, Direction.Left);
                    break;

                case 'E':
                    AnsiCursorMoveToLine(commandArgs, Direction.Down);
                    break;

                case 'F':
                    AnsiCursorMoveToLine(commandArgs, Direction.Up);
                    break;

                case 'G':
                    AnsiCursorSetColumn(commandArgs);
                    break;

                case 'H':
                case 'f':
                    AnsiSetPosition(commandArgs);
                    break;

                case 'J':
                    AnsiClearScreen(commandArgs);
                    break;

                case 'K':
                    AnsiClearLine(commandArgs);
                    break;

                case 'm':
                    AnsiGraphics(commandArgs);
                    break;

                case 's':
                    savedPosition = CursorPosition;
                    break;

                case 'u':
                    CursorPosition = savedPosition;
                    break;

                default:
                    WriteString(ansiCommand.ToString(), ForegroundColor, BackgroundColor, true);
                    break;
            }

            ansiCommand.Length = 0;
        }
        private void AnsiCursorMove(string[]? args, Direction direction)
        {
            int offset = 1;

            if (args != null && args.Length >= 1)
            {
                if (!int.TryParse(args[0].AsSpan().Trim(), out offset))
                    offset = 1;
            }

            var pos = CursorPosition;
            switch (direction)
            {
                case Direction.Up:
                    pos.Y -= offset;
                    break;

                case Direction.Down:
                    pos.Y += offset;
                    break;

                case Direction.Left:
                    pos.X -= offset;
                    break;

                case Direction.Right:
                    pos.X += offset;
                    break;
            }

            CursorPosition = pos;
        }
        private void AnsiCursorMoveToLine(string[]? args, Direction direction)
        {
            int offset = 1;

            if (args != null && args.Length > 0)
            {
                if (!int.TryParse(args[0].AsSpan().Trim(), out offset))
                    offset = 1;
            }

            var pos = CursorPosition;
            pos.X = 0;
            switch (direction)
            {
                case Direction.Up:
                    pos.Y -= offset;
                    break;

                case Direction.Down:
                    pos.Y += offset;
                    break;
            }

            CursorPosition = pos;
        }
        private void AnsiCursorSetColumn(string[]? args)
        {
            int column = 0;
            if (args != null && args.Length > 0)
            {
                if (int.TryParse(args[0].AsSpan().Trim(), out column))
                    column--;
                else
                    column = 0;
            }

            var pos = CursorPosition;
            pos.X = column;
            CursorPosition = pos;
        }
        private void AnsiSetPosition(string[]? args)
        {
            int row = 0;
            int column = 0;

            if (args != null && args.Length > 0)
            {
                if (int.TryParse(args[0].AsSpan().Trim(), out row))
                    row--;
                else
                    row = 0;

                if (args.Length > 1)
                {
                    if (int.TryParse(args[1].AsSpan().Trim(), out column))
                        column--;
                    else
                        column = 0;
                }
            }

            CursorPosition = new Point(column, row);
        }
        private void AnsiClearScreen(string[]? args)
        {
            int code = 0;
            if (args != null && args.Length > 0)
            {
                if (!int.TryParse(args[0].AsSpan().Trim(), out code))
                    code = 0;
            }

            Point pos;

            switch (code)
            {
                case 2:
                    Clear();
                    break;

                case 1:
                    pos = CursorPosition;
                    Clear(new Point(), Width, pos.Y);
                    Clear(new Point(0, pos.Y), pos.X + 1, 1);
                    break;

                default:
                    pos = CursorPosition;
                    Clear(pos, Width - pos.X, 1);
                    Clear(new Point(0, pos.Y + 1), Width, Height - pos.Y - 1);
                    break;
            }
        }
        private void AnsiClearLine(string[]? args)
        {
            int code = 0;
            if (args != null && args.Length > 0)
            {
                if (!int.TryParse(args[0].AsSpan().Trim(), out code))
                    code = 0;
            }

            var pos = CursorPosition;

            switch (code)
            {
                case 2:
                    Clear(new Point(0, pos.Y), Width, 1);
                    break;

                case 1:
                    Clear(new Point(0, pos.Y), pos.X + 1, 1);
                    break;

                default:
                    Clear(pos, Width - pos.X, 1);
                    break;
            }
        }
        private void AnsiGraphics(string[]? args)
        {
            int code = 0;
            if (args != null && args.Length > 0)
            {
                if (!int.TryParse(args[0].AsSpan().Trim(), out code))
                    code = 0;
            }

            RunGraphicsCommand(code);

            if (args != null && args.Length > 1)
            {
                if (int.TryParse(args[1].AsSpan().Trim(), out code))
                    RunGraphicsCommand(code);
            }
        }
        private void RunGraphicsCommand(int code)
        {
            byte swap;

            // Reset all attributes
            if (code == 0)
            {
                boldEnabled = false;
                if (negativeEnabled)
                {
                    swap = ForegroundColor;
                    ForegroundColor = BackgroundColor;
                    BackgroundColor = swap;
                    negativeEnabled = false;
                }

                savedPosition = new Point(0, 24);
            }
            // Enable bold text.
            else if (code == 1)
            {
                boldEnabled = true;
            }
            // Swap foreground and background colors.
            else if (code == 7 && !negativeEnabled)
            {
                swap = ForegroundColor;
                ForegroundColor = BackgroundColor;
                BackgroundColor = swap;
                negativeEnabled = true;
            }
            // Set foreground color (low intensity)
            else if (code >= 30 && code <= 39)
            {
                ForegroundColor = GetConsoleColor(code - 30, boldEnabled);
            }
            // Set background color (low intensity)
            else if (code >= 40 && code <= 49)
            {
                BackgroundColor = GetConsoleColor(code - 40, false);
            }
            // Set foreground color (high intensity)
            else if (code >= 90 && code <= 99)
            {
                ForegroundColor = GetConsoleColor(code - 90, true);
            }
            // Set background color (high intensity)
            else if (code >= 100 && code <= 109)
            {
                BackgroundColor = GetConsoleColor(code - 100, true);
            }
        }

        private static byte GetConsoleColor(int code, bool brightIntensity) => brightIntensity ? (byte)(code + 8) : (byte)code;
        private static ushort MakeCharacter(byte character, byte foreground, byte background) => (ushort)(character | (foreground << 8) | ((background & 0x07) << 12));

        /// <summary>
        /// Specifies a direction on the console.
        /// </summary>
        private enum Direction
        {
            /// <summary>
            /// Towards the top of the console.
            /// </summary>
            Up,
            /// <summary>
            /// Towards the bottom of the console.
            /// </summary>
            Down,
            /// <summary>
            /// Towards the left of the console.
            /// </summary>
            Left,
            /// <summary>
            /// Towards the right of the console.
            /// </summary>
            Right
        }
    }
}
