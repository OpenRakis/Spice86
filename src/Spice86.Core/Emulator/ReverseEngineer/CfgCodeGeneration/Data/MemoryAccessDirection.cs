namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

/// <summary>Whether a memory place the generated code touches at a constant offset is read,
/// written, or both; the flags combine.</summary>
[Flags]
internal enum MemoryAccessDirection {
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write
}
