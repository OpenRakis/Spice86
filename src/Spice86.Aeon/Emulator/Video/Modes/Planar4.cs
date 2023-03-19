using System.Runtime.CompilerServices;

namespace Spice86.Aeon.Emulator.Video.Modes
{
    /// <summary>
    /// Implements functionality for 4-plane video modes.
    /// </summary>
    public abstract class Planar4 : VideoMode
    {
        private readonly unsafe uint* videoRam;
        private uint latches;
        private readonly Graphics graphics;
        private readonly Sequencer sequencer;

        public Planar4(int width, int height, int bpp, int fontHeight, VideoModeType modeType, IAeonVgaCard video)
            : base(width, height, bpp, true, fontHeight, modeType, video)
        {
            unsafe
            {
                videoRam = (uint*)video.VideoRam.ToPointer();
            }

            graphics = video.Graphics;
            sequencer = video.Sequencer;
        }

        public override byte GetVramByte(uint offset)
        {
            offset %= 65536u;

            unsafe
            {
                latches = videoRam[offset];

                if ((graphics.GraphicsMode & (1 << 3)) == 0)
                {
                    return ReadByte(latches, graphics.ReadMapSelect & 0x3u);
                }

                uint colorDontCare = graphics.ColorDontCare.Expanded;
                uint colorCompare = graphics.ColorCompare * 0x01010101u;
                uint results = Intrinsics.AndNot(colorDontCare, latches ^ colorCompare);
                byte* bytes = (byte*)&results;
                return (byte)(bytes[0] | bytes[1] | bytes[2] | bytes[3]);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public override void SetVramByte(uint offset, byte value)
        {
            offset %= 65536u;

            uint writeMode = graphics.GraphicsMode & 0x3u;
            if (writeMode == 0)
            {
                SetByteMode0(offset, value);
            }
            else if (writeMode == 1)
            {
                // when mapMask = 0 keep value in vram
                // whem mapMask = 1 take value from latches
                // input value is not used at all

                uint mapMask = sequencer.MapMask.Expanded;

                unsafe
                {
                    uint current = Intrinsics.AndNot(videoRam[offset], mapMask); // read value and clear mask bits
                    current |= latches & mapMask; // set latch bits
                    videoRam[offset] = current;
                }
            }
            else if (writeMode == 2)
            {
                SetByteMode2(offset, value);
            }
            else
            {
                SetByteMode3(offset, value);
            }
        }
        internal override ushort GetVramWord(uint offset)
        {
            return (ushort)(GetVramByte(offset) | (GetVramByte(offset + 1u) << 8));
        }
        internal override void SetVramWord(uint offset, ushort value)
        {
            SetVramByte(offset, (byte)value);
            SetVramByte(offset + 1u, (byte)(value >> 8));
        }
        internal override uint GetVramDWord(uint offset)
        {
            return (uint)(GetVramByte(offset) | (GetVramByte(offset + 1u) << 8) | (GetVramByte(offset + 2u) << 16) | (GetVramByte(offset + 3u) << 24));
        }
        internal override void SetVramDWord(uint offset, uint value)
        {
            SetVramByte(offset, (byte)value);
            SetVramByte(offset + 1u, (byte)(value >> 8));
            SetVramByte(offset + 2u, (byte)(value >> 16));
            SetVramByte(offset + 3u, (byte)(value >> 24));
        }
        internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
        {
            unsafe
            {
                uint fg = new MaskValue(foreground).Expanded;
                //uint bg = VideoComponent.ExpandRegister(background);

                int stride = Stride;
                int startPos = y * stride * 16 + x;
                byte[] font = Font;

                for (int row = 0; row < 16; row++)
                {
                    uint fgMask = font[index * 16 + row] * 0x01010101u;
                    //uint bgMask = ~fgMask;
                    uint value = fg & fgMask;

                    if ((background & 0x08) == 0)
                        videoRam[startPos + (row * stride)] = value;
                    else
                        videoRam[startPos + (row * stride)] ^= value;
                }
            }
        }

        /// <summary>
        /// Writes a byte to video RAM using the rules for write mode 0.
        /// </summary>
        /// <param name="offset">Video RAM offset to write byte.</param>
        /// <param name="input">Byte to write to video RAM.</param>
        private void SetByteMode0(uint offset, byte input)
        {
            unsafe
            {
                if (graphics.DataRotate == 0)
                {
                    uint source = (uint)input * 0x01010101;
                    uint mask = (uint)graphics.BitMask * 0x01010101;

                    // when mapMask is set, use computed value; otherwise keep vram value
                    uint mapMask = sequencer.MapMask.Expanded;

                    uint original = videoRam[offset];

                    uint setResetEnabled = graphics.EnableSetReset.Expanded;
                    uint setReset = graphics.SetReset.Expanded;

                    source = Intrinsics.AndNot(source, setResetEnabled);
                    source |= setReset & setResetEnabled;
                    source &= mask;
                    source |= Intrinsics.AndNot(latches, mask);

                    videoRam[offset] = (source & mapMask) | Intrinsics.AndNot(original, mapMask);
                }
                else
                {
                    SetByteMode0_Extended(offset, input);
                }
            }
        }
        /// <summary>
        /// Writes a byte to video RAM using the rules for write mode 0.
        /// </summary>
        /// <param name="offset">Video RAM offset to write byte.</param>
        /// <param name="input">Byte to write to video RAM.</param>
        /// <remarks>
        /// This method handles the uncommon case when DataRotate is not 0.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetByteMode0_Extended(uint offset, byte input)
        {
            unsafe
            {
                uint source = (uint)input * 0x01010101;
                uint mask = (uint)graphics.BitMask * 0x01010101;

                // when mapMask is set, use computed value; otherwise keep vram value
                uint mapMask = sequencer.MapMask.Expanded;
                uint original = videoRam[offset];

                uint setResetEnabled = graphics.EnableSetReset.Expanded;
                uint setReset = graphics.SetReset.Expanded;

                source = Intrinsics.AndNot(source, setResetEnabled);
                source |= setReset & setResetEnabled;

                int rotateCount = graphics.DataRotate & 0x07;
                source = RotateBytes(source, rotateCount);

                uint logicalOp = Intrinsics.ExtractBits(graphics.DataRotate, 3, 2, 0b11000);

                if (logicalOp == 0)
                {
                }
                else if (logicalOp == 1)
                {
                    source &= latches;
                }
                else if (logicalOp == 2)
                {
                    source |= latches;
                }
                else
                {
                    source ^= latches;
                }

                source &= mask;
                source |= Intrinsics.AndNot(latches, mask);

                videoRam[offset] = (source & mapMask) | Intrinsics.AndNot(original, mapMask);
            }

        }
        /// <summary>
        /// Writes a byte to video RAM using the rules for write mode 2.
        /// </summary>
        /// <param name="offset">Video RAM offset to write byte.</param>
        /// <param name="input">Byte to write to video RAM.</param>
        private void SetByteMode2(uint offset, byte input)
        {
            unsafe
            {
                uint values = new MaskValue(input).Expanded;

                uint logicalOp = Intrinsics.ExtractBits(graphics.DataRotate, 3, 2, 0b11000);

                if (logicalOp == 0)
                {
                }
                else if (logicalOp == 1)
                {
                    values &= latches;
                }
                else if (logicalOp == 2)
                {
                    values |= latches;
                }
                else
                {
                    values ^= latches;
                }

                uint mask = (uint)graphics.BitMask * 0x01010101;

                values &= mask;
                values |= Intrinsics.AndNot(latches, mask);

                // when mapMask = 0 keep value in vram
                // whem mapMask = 1 take value from latches
                // input value is not used at all

                uint mapMask = sequencer.MapMask.Expanded;
                uint current = Intrinsics.AndNot(videoRam[offset], mapMask); // read value and clear mask bits
                current |= values & mapMask; // set value bits
                videoRam[offset] = current;
            }
        }
        private void SetByteMode3(uint offset, byte input)
        {
            unsafe
            {
                int rotateCount = graphics.DataRotate & 0x07;
                uint source = (byte)(((uint)input >> rotateCount) | ((uint)input << (8 - rotateCount)));
                source &= graphics.BitMask;
                source *= 0x01010101;

                uint result = source & graphics.SetReset.Expanded;
                result |= Intrinsics.AndNot(latches, source);

                videoRam[offset] = result;
            }
        }
        private static uint RotateBytes(uint value, int count)
        {
            unsafe
            {
                byte* v = (byte*)&value;
                int count2 = 8 - count;
                v[0] = (byte)((v[0] >> count) | (v[0] << count2));
                v[1] = (byte)((v[1] >> count) | (v[1] << count2));
                v[2] = (byte)((v[2] >> count) | (v[2] << count2));
                v[3] = (byte)((v[3] >> count) | (v[3] << count2));
                return value;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ReadByte(uint value, uint index)
        {
            return (byte)Intrinsics.ExtractBits(value, (byte)(index * 8u), 8, 0xFFu << ((int)index * 8));
        }
    }
}
