namespace Spice86.Tests.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Support for DOSBox Raw OPL (DRO) file format for OPL register capture and playback.
/// Mirrors DOSBox Staging's opl_capture.cpp/.h implementation for test compatibility.
/// </summary>
public static class DroFileFormat {
    /// <summary>
    /// DRO file header structure matching DOSBox format.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DroHeader {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Id;              // "DBRAWOPL"
        public ushort VersionHigh;     // 0x0002
        public ushort VersionLow;      // 0x0000
        public uint Commands;          // Number of command/data pairs
        public uint Milliseconds;      // Total milliseconds
        public byte Hardware;          // 0=OPL2, 1=Dual OPL2, 2=opl
        public byte Format;            // 0=interleaved
        public byte Compression;       // 0=none
        public byte Delay256;          // Index for 1-256ms delay command
        public byte DelayShift8;       // Index for (n+1)*256ms delay
        public byte ConvTableSize;     // Size of register conversion table
        
        public static DroHeader CreateDefault() {
            return new DroHeader {
                Id = System.Text.Encoding.ASCII.GetBytes("DBRAWOPL"),
                VersionHigh = 0x0002,
                VersionLow = 0x0000,
                Commands = 0,
                Milliseconds = 0,
                Hardware = 2, // opl
                Format = 0,
                Compression = 0,
                Delay256 = 0,
                DelayShift8 = 0,
                ConvTableSize = 0
            };
        }
    }
    
    /// <summary>
    /// Single OPL register write command with timing.
    /// </summary>
    public struct DroCommand {
        public byte Register;
        public byte Value;
        public uint DelayMs;
    }
    
    /// <summary>
    /// Complete DRO file data.
    /// </summary>
    public class DroFile {
        public DroHeader Header { get; set; }
        public byte[] RegisterTable { get; set; } = Array.Empty<byte>();
        public List<DroCommand> Commands { get; } = new();
        
        /// <summary>
        /// Save DRO file to disk in DOSBox format.
        /// </summary>
        public void SaveToFile(string filePath) {
            using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
            using BinaryWriter writer = new(fs);
            
            // Write header
            byte[] headerBytes = StructureToByteArray(Header);
            writer.Write(headerBytes);
            
            // Write register conversion table
            writer.Write(RegisterTable);
            
            // Write commands (register/value pairs with delays)
            foreach (DroCommand cmd in Commands) {
                if (cmd.DelayMs > 0) {
                    WriteDelay(writer, cmd.DelayMs);
                }
                writer.Write(cmd.Register);
                writer.Write(cmd.Value);
            }
        }
        
        /// <summary>
        /// Load DRO file from disk.
        /// </summary>
        public static DroFile LoadFromFile(string filePath) {
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException($"DRO file not found: {filePath}");
            }
            
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(fs);
            
            DroFile dro = new();
            
            // Read header
            byte[] headerBytes = reader.ReadBytes(Marshal.SizeOf<DroHeader>());
            dro.Header = ByteArrayToStructure<DroHeader>(headerBytes);
            
            // Validate magic
            string magic = System.Text.Encoding.ASCII.GetString(dro.Header.Id);
            if (magic != "DBRAWOPL") {
                throw new InvalidDataException($"Invalid DRO file magic: {magic}");
            }
            
            // Read register conversion table
            if (dro.Header.ConvTableSize > 0) {
                dro.RegisterTable = reader.ReadBytes(dro.Header.ConvTableSize);
            }
            
            // Read commands
            uint commandsRemaining = dro.Header.Commands;
            uint currentDelay = 0;
            
            while (commandsRemaining > 0 && fs.Position < fs.Length) {
                byte reg = reader.ReadByte();
                byte val = reader.ReadByte();
                commandsRemaining--;
                
                // Check for delay commands
                if (reg == dro.Header.Delay256) {
                    currentDelay += (uint)(val + 1);
                    continue;
                } else if (reg == dro.Header.DelayShift8) {
                    currentDelay += (uint)((val + 1) * 256);
                    continue;
                }
                
                // Regular register write
                dro.Commands.Add(new DroCommand {
                    Register = reg,
                    Value = val,
                    DelayMs = currentDelay
                });
                
                currentDelay = 0; // Reset delay after applying to command
            }
            
            return dro;
        }
        
        private void WriteDelay(BinaryWriter writer, uint delayMs) {
            // Write delay using DOSBox delay encoding
            while (delayMs > 0) {
                if (delayMs < 257) {
                    writer.Write(Header.Delay256);
                    writer.Write((byte)(delayMs - 1));
                    delayMs = 0;
                } else {
                    uint shift = delayMs >> 8;
                    writer.Write(Header.DelayShift8);
                    writer.Write((byte)(shift - 1));
                    delayMs -= shift << 8;
                }
            }
        }
        
        private static byte[] StructureToByteArray<T>(T structure) where T : struct {
            int size = Marshal.SizeOf(structure);
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try {
                Marshal.StructureToPtr(structure, ptr, false);
                Marshal.Copy(ptr, bytes, 0, size);
            } finally {
                Marshal.FreeHGlobal(ptr);
            }
            return bytes;
        }
        
        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct {
            int size = Marshal.SizeOf<T>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try {
                Marshal.Copy(bytes, 0, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            } finally {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
